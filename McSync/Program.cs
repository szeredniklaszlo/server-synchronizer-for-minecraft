using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using McSync.Exceptions;
using McSync.Files;
using McSync.Server;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync
{
    internal static class Program
    {
        private const string HashesFilePath = "hashes.json";
        private const string FlagsFilePath = "flags.json";
        private static readonly string AppPath = Directory.GetCurrentDirectory();
        private static readonly string ServerDirectoryPath =
            Directory.GetParent(AppPath)?.FullName;
            //@"E:\Desktop\Folders\Jatek\Minecraft_Server\WildUpdateServer";


        private static readonly Logger Log = Logger.Instance;
        private static readonly DriveService Drive = CreateDriveService();
        private static readonly HardwareInfoRepository HardwareInfoRepository = new HardwareInfoRepository();
        private static readonly ServerManager ServerManager = new ServerManager(HardwareInfoRepository);

        private static readonly ParallelOptions ParallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = 24 };
        private static double _downloadedMegabytes;
        private static readonly object DownloadedMegabytesLock = new object();
        private static double _uploadedMegabytes;
        private static readonly object UploadedMegabytesLock = new object();

        private static void Main()
        {
            DownloadAppFile(FlagsFilePath);
            var flagsDownloaded = LocalFileManager.LoadFromFile<Flags>(FlagsFilePath);
            var calculatedStatus = ServerManager.CalculateStatusByFlags(flagsDownloaded);
            HandleServer(calculatedStatus);

            Log.Info("Done");
        }
        
        private static void HandleServer(CalculatedStatus calculatedStatus)
        {
            Log.Server(calculatedStatus);

            switch (calculatedStatus)
            {
                case CalculatedStatus.AlreadyRunningElsewhere:
                case CalculatedStatus.AlreadyUpdatingElsewhere:
                case CalculatedStatus.Running:
                    Log.Info("Exiting...");
                    Environment.Exit(1);
                    return;
                case CalculatedStatus.Outdated:
                    UpdateLocalServer();
                    break;
            }

            UpdateLocalFlags(SavedStatus.RUNNING);
            UploadAndOverwriteFile(FlagsFilePath, true);
            List<Process> serverProcesses = StartServer();
            WaitProcessesToBeClosed(serverProcesses);

            UpdateLocalFlags(SavedStatus.UPDATING);
            UploadAndOverwriteFile(FlagsFilePath, true);
            UpdateRemoteServer(calculatedStatus);

            UpdateLocalFlags(SavedStatus.STOPPED);
            UploadAndOverwriteFile(FlagsFilePath, true);
        }
        private static void UpdateRemoteServer(CalculatedStatus calculatedStatus)
        {
            DownloadAppFile(HashesFilePath);
            var remoteHashes = LocalFileManager.LoadFromFile<Dictionary<string, string>>(HashesFilePath);

            var calculatedHashes = LocalFileManager.CalculateHashOfFilesInDirectory(ServerDirectoryPath);
            LocalFileManager.SaveObjectIntoFile(HashesFilePath, calculatedHashes);

            var filesToDelete = remoteHashes.Except(calculatedHashes).ToDictionary(pair => pair.Key, pair => pair.Value);
            var filesToUpload = calculatedHashes.Except(remoteHashes).ToDictionary(pair => pair.Key, pair => pair.Value);

            var remoteHashesFiltered = remoteHashes.Except(filesToDelete).ToDictionary(pair => pair.Key, pair => pair.Value);
            var calculatedHashesFiltered = calculatedHashes.Except(filesToUpload).ToDictionary(pair => pair.Key, pair => pair.Value);

            var filesToUpdate = remoteHashesFiltered
                .Where(remoteHash => calculatedHashesFiltered[remoteHash.Key] != remoteHash.Value)
                .ToDictionary(remoteHash => remoteHash.Key, remoteHash => remoteHash.Value);

            Log.Server(CalculatedStatus.Uploading);

            Parallel.ForEach(filesToDelete, toDelete =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();
                        DeleteRemoteFile(ownDriveService, toDelete.Key);
                        break;
                    }
                    catch (Exception)
                    {
                        Log.Error("Retrying to delete: {}", toDelete.Key);
                    }
            });

            CreateRemoteFolderTreeFromRoot(ServerDirectoryPath);
            Parallel.ForEach(filesToUpload, ParallelOptions, toUpload =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();

                        if (calculatedStatus == CalculatedStatus.UploadedCorruptly &&
                            IsFilePresentOnDrive(ownDriveService, toUpload.Key))
                        {
                            Log.DriveWarn("Already present: {}", toUpload.Key);
                            break;
                        }

                        UploadFile(ownDriveService, toUpload.Key, false);
                        break;
                    }
                    catch (Exception)
                    {
                        Log.Error("Retrying to upload: {}", toUpload.Key);
                    }
            });

            Parallel.ForEach(filesToUpdate, ParallelOptions, toUpdate =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();
                        UploadAndOverwriteFile(ownDriveService, toUpdate.Key, false);
                        break;
                    }
                    catch (Exception)
                    {
                        Log.Error("Retrying to update: {}", toUpdate.Key);
                    }
            });

            Log.Info("All files synchronized");
            UploadAndOverwriteFile(HashesFilePath, true);

            Log.Server(CalculatedStatus.Synchronized);
        }
        
        private static void UpdateLocalFlags(SavedStatus status)
        {
            var flags = LocalFileManager.LoadFromFile<Flags>(FlagsFilePath);

            flags.Owner = HardwareInfoRepository.GetPcId();
            flags.SavedStatus = status;

            LocalFileManager.SaveObjectIntoFile(FlagsFilePath, flags);
            Log.Info($"Flag 'running' updated to '{status}'");
        }
        private static void UpdateLocalServer()
        {
            DownloadAppFile(HashesFilePath);
            var remoteHashes = LocalFileManager.LoadFromFile<Dictionary<string, string>>(HashesFilePath);
            var calculatedHashes = LocalFileManager.CalculateHashOfFilesInDirectory(ServerDirectoryPath);

            var filesToDelete = calculatedHashes.Except(remoteHashes).ToDictionary(pair => pair.Key, pair => pair.Value);
            var filesToDownload = remoteHashes.Except(calculatedHashes).ToDictionary(pair => pair.Key, pair => pair.Value);

            var remoteHashesFiltered = remoteHashes.Except(filesToDownload).ToDictionary(pair => pair.Key, pair => pair.Value);
            var calculatedHashesFiltered = calculatedHashes.Except(filesToDelete).ToDictionary(pair => pair.Key, pair => pair.Value);

            var filesToUpdate = remoteHashesFiltered
                .Where(remoteHash => calculatedHashesFiltered[remoteHash.Key] != remoteHash.Value)
                .ToDictionary(remoteHash => remoteHash.Key, remoteHash => remoteHash.Value);

            Log.Server(CalculatedStatus.Updating);

            Parallel.ForEach(filesToDelete, toDelete =>
            {
                try
                {
                    File.Delete($@"{ServerDirectoryPath}\{toDelete.Key}");
                    Log.Local("Deleted: {}", toDelete.Key);
                }
                catch (Exception) { /* ignored */ }
            });

            Parallel.ForEach(filesToDownload, ParallelOptions, toDownload =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();
                        DownloadFile(ownDriveService, $@"{ServerDirectoryPath}\{toDownload.Key}");
                        break;
                    }
                    catch (Exception)
                    { Log.DriveWarn("Retrying to download: {}", toDownload.Key); }
            });

            Parallel.ForEach(filesToUpdate, ParallelOptions, toUpdate =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();
                        DownloadFile(ownDriveService, $@"{ServerDirectoryPath}\{toUpdate.Key}");
                        break;
                    }
                    catch (Exception)
                    { Log.DriveWarn("Retrying to update: {}", toUpdate.Key); }
            });

            Log.Info("All files synchronized");
            Log.Server(CalculatedStatus.Synchronized);
        }


        private static DriveService CreateDriveService()
        {
            string[] scopes = { DriveService.Scope.Drive };
            string appName = "Minecraft Server Sync";

            UserCredential userCredential;

            using (var stream = File.Open(@"Key\credentials.json", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = "token.json";
                userCredential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
            }

            // Create Drive API service.
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = userCredential,
                ApplicationName = appName,
                DefaultExponentialBackOffPolicy = Google.Apis.Http.ExponentialBackOffPolicy.Exception
            });

            return service;
        }
        private static DownloadStatus DownloadFile(string path)
        {
            return DownloadFile(Drive, path);
        }
        private static DownloadStatus DownloadFile(DriveService driveService, string path)
        {
            var pathSplit = path.Split(new[] { ServerDirectoryPath + @"\" }, StringSplitOptions.None);
            string choppedPath = pathSplit.Last();

            Log.Info("Downloading: {}", choppedPath);
            string fileId = GetIdOfPath(choppedPath);

            if (fileId == null)
                return DownloadStatus.Failed;

            string directoryName = Path.GetDirectoryName(path) ?? string.Empty;
            
            if (directoryName.Length > 0)
                Directory.CreateDirectory(directoryName);

            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                var getRequest = driveService.Files.Get(fileId);
                getRequest.MediaDownloader.ProgressChanged += DownloadRequest_ProgressChanged;

                var downloadStatus = getRequest.DownloadWithStatus(fileStream).Status;

                if (downloadStatus == DownloadStatus.Failed)
                {
                    Log.Error("Download failed: {}", choppedPath);
                    throw new DownloadFailedException("Download failed.");
                }
                else Log.Drive("Downloaded: {}", choppedPath);

                return downloadStatus;
            }
        }

        private static void DownloadRequest_ProgressChanged(IDownloadProgress obj)
        {
            lock (DownloadedMegabytesLock)
            {
                double previousDownloadedMegabytes = _downloadedMegabytes;
                _downloadedMegabytes += Math.Ceiling(obj.BytesDownloaded / 10000d) / 100d;

                if (_downloadedMegabytes > previousDownloadedMegabytes)
                    UpdateConsoleTitle();
            }
        }

        private static void UpdateConsoleTitle()
        {
            Console.Title = $"Minecraft Synchronizer | Total downloaded / uploaded: {_downloadedMegabytes} / {_uploadedMegabytes} MB";
        }

        private static void DownloadAppFile(string appFilePath)
        {
            var flagsDownloadStatus = DownloadFile(appFilePath);

            if (flagsDownloadStatus == DownloadStatus.Failed)
            {
                LocalFileManager.SaveObjectIntoFile(appFilePath, new object());
                Log.Info($"{appFilePath.Split('.')[0]} not found on Drive, created new locally");
            }
            else Log.Info($"{appFilePath.Split('.')[0]} downloaded from Drive");
        }

        private static void UploadFile(DriveService driveService, string path, bool isAppFile)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string relativePath = LocalFileManager.GetRelativeFilePath(path, ServerDirectoryPath);
            Log.Info("Uploading: {}", relativePath);

            const string mimeType = "application/octet-stream";

            string[] pathSplitted = path.Split('\\');
            Array.Resize(ref pathSplitted, pathSplitted.Length - 1);
            string parentPath = string.Join(@"\", pathSplitted);
            string parentId = GetIdOfPath(driveService, parentPath) ?? CreateRemoteFolder(driveService, parentPath);

            using (var fs = new FileStream(isAppFile ? path : $@"{ServerDirectoryPath}\{path}", FileMode.Open))
                ExecuteCreateRequest(driveService, parentId, path.Split('\\').LastOrDefault(), mimeType, fs);
            Log.Drive("Uploaded: {}", relativePath);
        }
        private static void UploadAndOverwriteFile(string path, bool isAppFile)
        {
            UploadAndOverwriteFile(Drive, path, isAppFile);
        }
        private static void UploadAndOverwriteFile(DriveService driveService, string path, bool isAppFile)
        {
            DeleteRemoteFile(driveService, path);
            UploadFile(driveService, path, isAppFile);

            if (!isAppFile) return;
            
            switch (path)
            {
                case "flags.json":
                    Log.Info($"Server status updated on the Drive");
                    break;
                case "hashes.json":
                    Log.Info($"Hashes of server files saved to the Drive");
                    break;
            }
        }

        private static string CreateRemoteFolder(DriveService driveService, string path)
        {
            Log.Info("Creating: {}", path);
            const string mimeType = "application/vnd.google-apps.folder";

            bool haveCreated = false;
            string lastParent = string.Empty;
            string[] folders = LocalFileManager.GetRelativeFilePath(path, ServerDirectoryPath).Split('\\');
            foreach (string folder in folders)
            {
                string folderId = ExecuteGetIdOfPathRequest(driveService, folder, lastParent);

                if (string.IsNullOrEmpty(folderId))
                {
                    folderId = ExecuteCreateRequest(driveService, lastParent, folder, mimeType);
                    haveCreated = true;
                }

                lastParent = folderId;
            }

            if (haveCreated)
                Log.Drive("Created: {}", path);
            else Log.DriveWarn("Already created: {}", path);

            return lastParent;
        }
        private static void CreateRemoteFolderTreeFromRoot(string rootPath)
        {
            DirectoryInfo currentDirectory = new DirectoryInfo(rootPath);
            var childDirectories = currentDirectory.GetDirectories()
                .Where(directory => IsFileNotFiltered(directory.FullName))
                .ToList();

            Parallel.ForEach(childDirectories, childDirectory =>
            {
                while (true)
                    try
                    {
                        var ownDriveService = CreateDriveService();
                        CreateRemoteFolder(ownDriveService, childDirectory.FullName);
                        break;
                    }
                    catch (Exception)
                    {
                        Log.Error("Retrying to create: {}", childDirectory);
                    }
            });

            foreach (var childDirectory in childDirectories)
            {
                CreateRemoteFolderTreeFromRoot(childDirectory.FullName);
            }
        }

        private static void DeleteRemoteFile(DriveService driveService, string path)
        {
            Log.Info("Deleting: {}", path);

            string fileId = GetIdOfPath(path);

            if (string.IsNullOrEmpty(fileId))
            {
                Log.DriveWarn("Not found: {}", path);
                return;
            }

            driveService.Files.Delete(fileId).Execute();
            Log.Drive("Deleted: {}", LocalFileManager.GetRelativeFilePath(path, ServerDirectoryPath));
        }

        private static bool IsFilePresentOnDrive(DriveService driveService, string path)
        {
            return !string.IsNullOrEmpty(GetIdOfPath(driveService, path));
        }

        private static string GetIdOfPath(string path)
        {
            return GetIdOfPath(Drive, path);
        }

        private static string GetIdOfPath(DriveService driveService, string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            var parents = new LinkedList<string>(path.Split(new[] { @"\" }, StringSplitOptions.None)); // folder1/folder11/file
            string fileName = parents.Last(); // file
            parents.RemoveLast(); // folder1, folder11

            return GetIdOfPathRecursively(driveService, parents, fileName);
        }
        private static string GetIdOfPathRecursively(DriveService driveService, LinkedList<string> parents, string fileName, string lastParentId = null)
        {
            if (string.IsNullOrEmpty(fileName))
                return "";

            string folderName = parents.FirstOrDefault();
            if (parents.Count != 0)
                parents.RemoveFirst();
            else return ExecuteGetIdOfPathRequest(driveService, fileName, lastParentId);

            string folderId = ExecuteGetIdOfPathRequest(driveService, folderName, lastParentId);
            return GetIdOfPathRecursively(driveService, parents, fileName, folderId);
        }
        private static string ExecuteGetIdOfPathRequest(DriveService driveService, string fileName, string parentId)
        {
            var listRequest = driveService.Files.List();
            listRequest.Q = string.IsNullOrEmpty(parentId)
                    ? $"name = '{fileName}' and 'root' in parents"
                    : $"name = '{fileName}' and '{parentId}' in parents";

            return listRequest.Execute().Files.FirstOrDefault()?.Id;
        }
        
        internal static void ListDriveFiles()
        {
            // Define parameters of request.
            FilesResource.ListRequest listRequest = Drive.Files.List();
            listRequest.PageSize = 10;
            listRequest.Fields = "nextPageToken, files(id, name, mimeType)";

            // List files.
            IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute()
                .Files;
            Console.WriteLine("Files:");
            if (files != null && files.Count > 0)
                foreach (var file in files)
                    Console.WriteLine("{0} ({1}) {2}", file.Name, file.Id, file.MimeType);
            else Console.WriteLine("No files found.");
        }

        private static string ExecuteCreateRequest(DriveService driveService, string parentId, string fileName, string mimeType, FileStream fileStream = null)
        {
            var driveFile = new Google.Apis.Drive.v3.Data.File()
            {
                Name = fileName,
                MimeType = mimeType
            };
            if (!string.IsNullOrEmpty(parentId))
                driveFile.Parents = new List<string> { parentId };

            if (fileStream != null)
            {
                var request = driveService.Files.Create(driveFile, fileStream, mimeType);
                request.Fields = "id";

                request.ProgressChanged += UploadRequest_ProgressChanged;
                request.ResponseReceived += UploadRequest_ResponseReceived;

                var progress = request.Upload();
                if (progress.Status != UploadStatus.Completed)
                    throw progress.Exception;

                return request.ResponseBody.Id;
            }
            else
            {
                var request = driveService.Files.Create(driveFile);
                request.Fields = "id";

                var folderCreated = request.Execute();
                return folderCreated.Id;
            }
        }
        private static void UploadRequest_ResponseReceived(Google.Apis.Drive.v3.Data.File obj)
        {
            // TODO: get to remember wtf is this for
        }
        private static void UploadRequest_ProgressChanged(IUploadProgress obj)
        {
            lock (UploadedMegabytesLock)
            {
                double previousUploadedMegabytes = _uploadedMegabytes;
                _uploadedMegabytes += Math.Ceiling(obj.BytesSent / 10000d) / 100d;

                if (_uploadedMegabytes > previousUploadedMegabytes)
                    UpdateConsoleTitle();
            }
        }

        private static Process RunCmdCommand(string command)
        {
            command = $"/C {command}"; 

            var cmd = new Process();
            cmd.StartInfo = new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = true,
                WorkingDirectory = ServerDirectoryPath
            };

            cmd.Start();
            return cmd;
        }

        private static void WaitProcessesToBeClosed(List<Process> processes)
        {
            if (processes == null)
                return;

            foreach (var process in processes)
            {
                process.WaitForExit();
            }

            Log.Server(CalculatedStatus.Stopped);
        }

        private static List<Process> StartServer()
        {
            var processes = new List<Process>();
            Log.Server(CalculatedStatus.Starting);

            const string createTokenCommand = "ngrok authtoken 22NyU96RrxqrNvxb5Y7eJw08ZKl_5ZVerYF6Ei5XJN5b9E2TX";
            Process tokenCreater = RunCmdCommand(createTokenCommand);
            tokenCreater.WaitForExit();

            var java17Home = $@"{AppPath}\OpenJDK64\bin";
            var java17Path = $@"{java17Home}\java";
            string serverJar = new DirectoryInfo(ServerDirectoryPath)
                .GetFiles()
                .FirstOrDefault(file => file.Extension == ".jar")?.Name;

            
            try
            {
                bool javaExists = new DirectoryInfo(java17Home).GetFiles()
                    .Any(file => file.Name == "java.exe");

                if (!javaExists)
                {
                    throw new JreNotFoundException();
                }
            }
            catch (DirectoryNotFoundException)
            {
                Log.Error($@"Java 17 is not present in {java17Path}");
                Environment.Exit(1);
            }

            string arguments = "-Xms4G -Xmx4G -XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200" +
                               " -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1HeapWastePercent=5" +
                               " -XX:G1MixedGCCountTarget=4 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5" +
                               " -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -XX:G1NewSizePercent=30" +
                               " -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:InitiatingHeapOccupancyPercent=15" +
                               $" -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true -jar {serverJar} nogui";

            processes.Add(RunCmdCommand($"{java17Path} {arguments}"));

            string openNgrokCommand = "ngrok.exe tcp 25565 --region=eu";
            processes.Add(RunCmdCommand(openNgrokCommand));

            Log.Server(CalculatedStatus.Running);

            return processes;
        }

        private static bool IsFileNotFiltered(string fullname)
        {
            fullname = fullname.ToLower();
            return !fullname.Contains("sync") &&
                !fullname.Contains("libraries") &&
                !fullname.Contains("openjdk") &&
                !fullname.Contains("net5.0") &&
                !fullname.Contains("ngrok.cmd") &&
                !fullname.Contains("run.cmd");
        }
    }
}
