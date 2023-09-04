using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using McSync.Exceptions;
using McSync.Files.Local;
using McSync.Files.Local.HashCalculator;
using McSync.Server.Info;
using McSync.Utils;
using File = Google.Apis.Drive.v3.Data.File;

namespace McSync.Files.Remote
{
    public class RemoteFileManager
    {
        private readonly object _downloadedMegabytesLock = new object();
        private readonly DriveServicePool _driveServicePool;
        private readonly DriveServiceRetrier _driveServiceRetrier;
        private readonly HashCalculatorFactory _hashCalculatorFactory;
        private readonly LocalFileManager _localFileManager;
        private readonly Log _log;
        private readonly object _uploadedMegabytesLock = new object();

        public RemoteFileManager(DriveServicePool driveServicePool, LocalFileManager localFileManager,
            HashCalculatorFactory hashCalculatorFactory, Log log, DriveServiceRetrier driveServiceRetrier)
        {
            _driveServicePool = driveServicePool;
            _localFileManager = localFileManager;
            _hashCalculatorFactory = hashCalculatorFactory;
            _log = log;
            _driveServiceRetrier = driveServiceRetrier;
        }

        public double DownloadedMegabytes { get; private set; }

        public double UploadedMegabytes { get; private set; }

        public IDictionary<string, string> CalculateFilesToDelete(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes)
        {
            return ToDictionary(remoteHashes.Except(localHashes));
        }

        public Dictionary<string, string> CalculateFilesToUpdate(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes,
            IDictionary<string, string> filesToDelete,
            IDictionary<string, string> filesToUpload)
        {
            IDictionary<string, string> remoteHashesFiltered = ToDictionary(remoteHashes.Except(filesToDelete));
            IDictionary<string, string> calculatedHashesFiltered = ToDictionary(localHashes.Except(filesToUpload));

            return remoteHashesFiltered
                .Where(remoteHash => calculatedHashesFiltered[remoteHash.Key] != remoteHash.Value)
                .ToDictionary(remoteHash => remoteHash.Key, remoteHash => remoteHash.Value);
        }

        public IDictionary<string, string> CalculateFilesToUpload(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes)
        {
            return ToDictionary(localHashes.Except(remoteHashes));
        }

        public void CreateRemoteFolderTreeFromLocalFolderRecursively(string rootPath)
        {
            var currentDirectory = new DirectoryInfo(rootPath);
            List<DirectoryInfo> childDirectories = currentDirectory.GetDirectories()
                .Where(directory => IsFileNotFiltered(directory.FullName))
                .ToList();

            Parallel.ForEach(childDirectories, Program.ParallelOptions, childDirectory =>
            {
                bool isRemoteFolderCreated = false;
                while (!isRemoteFolderCreated)
                    try
                    {
                        _driveServicePool.ExecuteWithDriveService(driveService =>
                        {
                            CreateRemoteFolder(driveService, childDirectory.FullName);
                            isRemoteFolderCreated = true;
                        });
                    }
                    catch (Exception)
                    {
                        _log.Error("Retrying to create: {}", childDirectory);
                    }
            });

            foreach (DirectoryInfo childDirectory in childDirectories)
                CreateRemoteFolderTreeFromLocalFolderRecursively(childDirectory.FullName);
        }

        public void DeleteRemoteFile(string path)
        {
            _driveServiceRetrier.RetryUntilThrowsNoException(
                () => _driveServicePool.ExecuteWithDriveService(driveService => DeleteRemoteFile(driveService, path)),
                e => _log.Error("Retrying to delete: {}", path));
        }

        public void DownloadOrCreateAppJsonFile(string appJsonFilePath)
        {
            DownloadStatus flagsDownloadStatus = DownloadServerOrAppFile(appJsonFilePath);

            if (flagsDownloadStatus == DownloadStatus.Failed)
            {
                _localFileManager.SaveObjectAsJsonFile(appJsonFilePath, new object());
                _log.Info($"{appJsonFilePath.Split('.')[0]} not found on Drive, created new locally");
            }
            else
            {
                _log.Info($"{appJsonFilePath.Split('.')[0]} downloaded from Drive");
            }
        }

        public DownloadStatus DownloadServerOrAppFile(string path)
        {
            string choppedPath = path.Split(new[] {Paths.ServerPath + @"\"}, StringSplitOptions.None).Last();

            return _driveServicePool.ExecuteWithDriveService(
                driveService => Download(driveService, choppedPath));
        }

        public IDictionary<string, string> UpdateHashes()
        {
            IDictionary<string, string> calculatedHashesForRelativePaths =
                CalculateHashesForServerDirWithRelativePaths();
            _localFileManager.SaveObjectAsJsonFile(Paths.Hashes, calculatedHashesForRelativePaths);
            return calculatedHashesForRelativePaths;
        }

        public void UpdateRemoteFile(string path)
        {
            _driveServiceRetrier.RetryUntilThrowsNoException(
                () => _driveServicePool.ExecuteWithDriveService(driveService =>
                    UploadAndOverwriteFile(driveService, path, false)),
                e => _log.Error("Retrying to update: {}", path));
        }

        public void UploadAndOverwriteFile(string path, bool isAppFile)
        {
            _driveServicePool.ExecuteWithDriveService(driveService =>
            {
                UploadAndOverwriteFile(driveService, path, isAppFile);
            });
        }

        public void UploadFile(CalculatedStatus calculatedStatus, string path)
        {
            _driveServiceRetrier.RetryUntilThrowsNoException(
                () => _driveServicePool.ExecuteWithDriveService(driveService =>
                {
                    if (calculatedStatus == CalculatedStatus.UploadedCorruptly &&
                        IsFilePresentOnDrive(driveService, path))
                        _log.DriveWarn("Already present: {}", path);
                    else
                        UploadFile(driveService, path, false);
                }),
                e => _log.Error("Retrying to upload: {}", path));
        }

        private IDictionary<FileInfo, string> CalculateHashesForServerDirWithAbsolutePaths()
        {
            HashCalculator serverDirHashCalculator = NewServerDirHashCalculator();
            return serverDirHashCalculator.CalculateHashes();
        }

        private IDictionary<string, string> CalculateHashesForServerDirWithRelativePaths()
        {
            IDictionary<FileInfo, string> calculatedHashesForAbsolutePaths =
                CalculateHashesForServerDirWithAbsolutePaths();
            return calculatedHashesForAbsolutePaths.ToDictionary(
                ExtractKeyFromFileToRelativePath,
                pair => pair.Value);
        }

        private string CreateRemoteFolder(DriveService driveService, string path)
        {
            _log.Info("Creating: {}", path);
            const string mimeType = "application/vnd.google-apps.folder";

            bool haveCreated = false;
            string lastParent = string.Empty;
            string[] folders = _localFileManager.GetRelativeFilePath(path, Paths.ServerPath).Split('\\');
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
                _log.Drive("Created: {}", path);
            else _log.DriveWarn("Already created: {}", path);

            return lastParent;
        }

        private void DeleteRemoteFile(DriveService driveService, string path)
        {
            _log.Info("Deleting: {}", path);

            string fileId = GetIdOfPath(path);

            if (string.IsNullOrEmpty(fileId))
            {
                _log.DriveWarn("Not found: {}", path);
                return;
            }

            driveService.Files.Delete(fileId).Execute();
            _log.Drive("Deleted: {}", _localFileManager.GetRelativeFilePath(path, Paths.ServerPath));
        }

        private DownloadStatus Download(DriveService driveService, string path)
        {
            _log.Info("Downloading: {}", path);
            string fileId = GetIdOfPath(path);

            if (fileId == null)
                return DownloadStatus.Failed;

            string directoryName = Path.GetDirectoryName(path) ?? string.Empty;

            if (directoryName.Length > 0)
                Directory.CreateDirectory(directoryName);

            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                FilesResource.GetRequest getRequest = driveService.Files.Get(fileId);
                getRequest.MediaDownloader.ProgressChanged += DownloadRequest_ProgressChanged;

                DownloadStatus downloadStatus = getRequest.DownloadWithStatus(fileStream).Status;

                if (downloadStatus == DownloadStatus.Failed)
                {
                    _log.Error("Download failed: {}", path);
                    throw new DownloadFailedException($"Download failed: {path}");
                }

                _log.Drive("Downloaded: {}", path);

                return downloadStatus;
            }
        }

        private void DownloadRequest_ProgressChanged(IDownloadProgress obj)
        {
            lock (_downloadedMegabytesLock)
            {
                double previousDownloadedMegabytes = DownloadedMegabytes;
                DownloadedMegabytes += Math.Ceiling(obj.BytesDownloaded / 10000d) / 100d;

                if (DownloadedMegabytes > previousDownloadedMegabytes)
                    Program.UpdateConsoleTitleWithNetworkTraffic();
            }
        }

        private string ExecuteCreateRequest(DriveService driveService, string parentId, string fileName,
            string mimeType, FileStream fileStream = null)
        {
            var driveFile = new File
            {
                Name = fileName,
                MimeType = mimeType
            };
            if (!string.IsNullOrEmpty(parentId))
                driveFile.Parents = new List<string> {parentId};

            if (fileStream != null)
            {
                FilesResource.CreateMediaUpload request = driveService.Files.Create(driveFile, fileStream, mimeType);
                request.Fields = "id";

                request.ProgressChanged += UploadRequest_ProgressChanged;
                request.ResponseReceived += UploadRequest_ResponseReceived;

                IUploadProgress progress = request.Upload();
                if (progress.Status != UploadStatus.Completed)
                    throw progress.Exception;

                return request.ResponseBody.Id;
            }
            else
            {
                FilesResource.CreateRequest request = driveService.Files.Create(driveFile);
                request.Fields = "id";

                File folderCreated = request.Execute();
                return folderCreated.Id;
            }
        }

        private string ExecuteGetIdOfPathRequest(DriveService driveService, string fileName, string parentId)
        {
            FilesResource.ListRequest listRequest = driveService.Files.List();
            listRequest.Q = string.IsNullOrEmpty(parentId)
                ? $"name = '{fileName}' and 'root' in parents"
                : $"name = '{fileName}' and '{parentId}' in parents";

            return listRequest.Execute().Files.FirstOrDefault()?.Id;
        }

        private string ExtractKeyFromFileToRelativePath(KeyValuePair<FileInfo, string> pair)
        {
            return _localFileManager.GetRelativeFilePath(pair.Key.FullName, Paths.ServerPath);
        }

        private string GetIdOfPath(string path)
        {
            return _driveServicePool.ExecuteWithDriveService(driveService => GetIdOfPath(driveService, path));
        }

        private string GetIdOfPath(DriveService driveService, string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            var parents =
                new LinkedList<string>(path.Split(new[] {@"\"}, StringSplitOptions.None)); // folder1/folder11/file
            string fileName = parents.Last.Value; // file
            parents.RemoveLast(); // folder1, folder11

            return GetIdOfPathRecursively(driveService, parents, fileName);
        }

        private string GetIdOfPathRecursively(DriveService driveService, LinkedList<string> parents, string fileName,
            string lastParentId = null)
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

        private bool IsFileNotFiltered(string fullname)
        {
            fullname = fullname.ToLower();
            return !fullname.Contains("sync") &&
                   !fullname.Contains("libraries") &&
                   !fullname.Contains("openjdk") &&
                   !fullname.Contains("net5.0") &&
                   !fullname.Contains("ngrok.cmd") &&
                   !fullname.Contains("run.cmd");
        }

        private bool IsFilePresentOnDrive(DriveService driveService, string path)
        {
            return !string.IsNullOrEmpty(GetIdOfPath(driveService, path));
        }

        private void ListDriveFiles()
        {
            // Define parameters of request.
            FilesResource.ListRequest listRequest =
                _driveServicePool.ExecuteWithDriveService(driveService => driveService.Files.List());
            listRequest.PageSize = 10;
            listRequest.Fields = "nextPageToken, files(id, name, mimeType)";

            // List files.
            IList<File> files = listRequest.Execute()
                .Files;
            Console.WriteLine("Files:");
            if (files != null && files.Count > 0)
                foreach (File file in files)
                    Console.WriteLine("{0} ({1}) {2}", file.Name, file.Id, file.MimeType);
            else Console.WriteLine("No files found.");
        }

        private HashCalculator NewServerDirHashCalculator()
        {
            List<FileInfo> filteredFilesInDirectory =
                _localFileManager.FilterFilesInDirectory(Paths.ServerPath);
            return _hashCalculatorFactory.CreateHashCalculator(filteredFilesInDirectory);
        }

        private IDictionary<TKey, TValue> ToDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
        {
            return enumerable.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private void UploadAndOverwriteFile(DriveService driveService, string path, bool isAppFile)
        {
            DeleteRemoteFile(driveService, path);
            UploadFile(driveService, path, isAppFile);

            if (!isAppFile) return;

            switch (path)
            {
                case "flags.json":
                    _log.Info("Server status updated on the Drive");
                    break;
                case "hashes.json":
                    _log.Info("Hashes of server files saved to the Drive");
                    break;
            }
        }

        private void UploadFile(DriveService driveService, string path, bool isAppFile)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string relativePath = _localFileManager.GetRelativeFilePath(path, Paths.ServerPath);
            _log.Info("Uploading: {}", relativePath);

            const string mimeType = "application/octet-stream";

            string[] pathSplit = path.Split('\\');
            Array.Resize(ref pathSplit, pathSplit.Length - 1);
            string parentPath = string.Join(@"\", pathSplit);
            string parentId = GetIdOfPath(driveService, parentPath) ?? CreateRemoteFolder(driveService, parentPath);

            using (var fs = new FileStream(isAppFile ? path : $@"{Paths.ServerPath}\{path}", FileMode.Open))
            {
                ExecuteCreateRequest(driveService, parentId, path.Split('\\').LastOrDefault(), mimeType, fs);
            }

            _log.Drive("Uploaded: {}", relativePath);
        }

        private void UploadRequest_ProgressChanged(IUploadProgress obj)
        {
            lock (_uploadedMegabytesLock)
            {
                double previousUploadedMegabytes = UploadedMegabytes;
                UploadedMegabytes += Math.Ceiling(obj.BytesSent / 10000d) / 100d;

                if (UploadedMegabytes > previousUploadedMegabytes)
                    Program.UpdateConsoleTitleWithNetworkTraffic();
            }
        }

        private void UploadRequest_ResponseReceived(File obj)
        {
            // TODO: get to remember wtf is this for
        }
    }
}