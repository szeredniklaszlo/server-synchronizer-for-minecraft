using System;
using System.IO;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using McSync.Files.Local;
using McSync.Utils;

namespace McSync.Files.Remote
{
    public class GDriveServiceFactory
    {
        private static readonly object TokenFileLock = new object();

        private readonly string _appName;
        private readonly string _credentialsPath;

        private readonly Log _log;

        public GDriveServiceFactory(string appName, string credentialsPath, Log log)
        {
            _appName = appName;
            _credentialsPath = credentialsPath;
            _log = log;
        }

        public DriveService CreateDriveService()
        {
            UserCredential userCredential = LoginToGAccountAsAppThroughBrowser();
            // TODO: create a file upload test request, and if an exception is thrown, catch it, 
            // so the application wont fail to start when the tokens expire in the token.json folder,
            // delete the token folder, and call CreateUserCredentialAndTokenJsonFile again.
            // OR easier: delete token folder on every application start,
            // so it will force the user to login again
            return NewDriveServiceAuthorized(userCredential);
        }

        private UserCredential LoginToGAccountAsAppThroughBrowser()
        {
            using (FileStream stream = File.Open(_credentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                lock (TokenFileLock)
                {
                    try
                    {
                        return LoginWithCredentials(stream);
                    }
                    catch (Exception)
                    {
                        _log.Error("Login failed. Try to login again!");
                        Directory.Delete(Paths.TokenFolder, true);
                        return LoginWithCredentials(stream);
                    }
                }
            }
        }

        // The folder "token" stores the user's access and refresh tokens after login,
        // it is created automatically when the authorization flow completes for the first time.
        private UserCredential LoginWithCredentials(FileStream credentialsFileStream)
        {
            bool wasLoggedOut = !Directory.Exists($@"{Paths.AppPath}\{Paths.TokenFolder}")
                                || Directory.GetFiles($@"{Paths.AppPath}\{Paths.TokenFolder}").Length == 0;
            if (wasLoggedOut) _log.Info("Logging in...");

            UserCredential userCredential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(credentialsFileStream).Secrets,
                new[] { DriveService.Scope.Drive },
                "user",
                CancellationToken.None,
                new FileDataStore(Paths.TokenFolder, true)).Result;

            if (wasLoggedOut) _log.Info("Logged in successfully!");
            return userCredential;
        }

        private DriveService NewDriveServiceAuthorized(UserCredential userCredential)
        {
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = userCredential,
                ApplicationName = _appName,
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.UnsuccessfulResponse503
            });
        }
    }
}