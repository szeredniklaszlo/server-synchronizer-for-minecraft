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

        // TODO: write logs
        private readonly Log _log;

        public GDriveServiceFactory(string appName, string credentialsPath, Log log)
        {
            _appName = appName;
            _credentialsPath = credentialsPath;
            _log = log;
        }

        public DriveService CreateDriveService()
        {
            lock (TokenFileLock)
            {
                UserCredential userCredential = CreateUserCredentialAndTokenJsonFile();
                // TODO: create a file upload test request, and if an exception is thrown, catch it, 
                // so the application wont fail to start when the tokens expire in the token.json folder,
                // delete the token folder, and call CreateUserCredentialAndTokenJsonFile again.
                // OR easier: delete token folder on every application close,
                // so on the next login it will force the user to login again
                return NewDriveServiceAuthorizedByCredentials(userCredential);
            }
        }

        private UserCredential CreateUserCredentialAndTokenJsonFile()
        {
            using (FileStream stream = File.Open(_credentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // The folder "token" stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                return GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] {DriveService.Scope.Drive},
                    "user",
                    CancellationToken.None,
                    new FileDataStore(Paths.TokenFolder, true)).Result;
            }
        }

        private DriveService NewDriveServiceAuthorizedByCredentials(UserCredential userCredential)
        {
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = userCredential,
                ApplicationName = _appName,
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.Exception
            });
        }
    }
}