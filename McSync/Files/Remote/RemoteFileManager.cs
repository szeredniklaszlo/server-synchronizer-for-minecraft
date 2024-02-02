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
using McSync.Server.Info;
using McSync.Utils;
using File = Google.Apis.Drive.v3.Data.File;

namespace McSync.Files.Remote
{
    public class RemoteFileManager
    {
        private readonly object _downloadedMegabytesLock = new object();
        private readonly GDriveServicePool _gDriveServicePool;
        private readonly GDriveServiceRetrier _gDriveServiceRetrier;
        private readonly Log _log;
        private readonly PathUtils _pathUtils;
        private readonly object _uploadedMegabytesLock = new object();

        public RemoteFileManager(GDriveServicePool gDriveServicePool,
            Log log,
            GDriveServiceRetrier gDriveServiceRetrier,
            PathUtils pathUtils)
        {
            _gDriveServicePool = gDriveServicePool;
            _log = log;
            _gDriveServiceRetrier = gDriveServiceRetrier;
            _pathUtils = pathUtils;
        }

        public double DownloadedMegabytes { get; private set; }

        public double UploadedMegabytes { get; private set; }

        public void CreateRemoteFolderTreeFromLocalFolderRecursively(string rootPath)
        {
            var currentDirectory = new DirectoryInfo(rootPath);
            List<DirectoryInfo> childDirectories = currentDirectory.GetDirectories()
                .Where(directory => IsFileNotFiltered(directory.FullName))
                .ToList();

            Parallel.ForEach(childDirectories, Program.ParallelOptions, childDirectory =>
                _gDriveServiceRetrier.RetryUntilThrowsNoException(
                    () => _gDriveServicePool.ExecuteWithDriveService(driveService =>
                        CreateRemoteFolder(driveService, childDirectory.FullName)),
                    e => _log.Error("Retrying to create: {}", childDirectory)));

            foreach (DirectoryInfo childDirectory in childDirectories)
                CreateRemoteFolderTreeFromLocalFolderRecursively(childDirectory.FullName);
        }

        public void DeleteRemoteFile(string path)
        {
            _gDriveServiceRetrier.RetryUntilThrowsNoException(
                () => _gDriveServicePool.ExecuteWithDriveService(driveService => DeleteRemoteFile(driveService, path)),
                e => _log.Error("Retrying to delete: {}", path));
        }

        public DownloadStatus DownloadServerOrAppFile(string path)
        {
            string choppedPath = path.Split(new[] {Paths.ServerPath + @"\"}, StringSplitOptions.None).Last();
            return _gDriveServicePool.ExecuteWithDriveService(driveService => Download(driveService, choppedPath));
        }

        public void UpdateRemoteFile(string path)
        {
            _gDriveServiceRetrier.RetryUntilThrowsNoException(
                () => _gDriveServicePool.ExecuteWithDriveService(driveService =>
                    UploadAndOverwriteFile(driveService, path, false)),
                e => _log.Error("Retrying to update: {}", path));
        }

        public void UploadAndOverwriteFile(string path, bool isAppFile)
        {
            _gDriveServicePool.ExecuteWithDriveService(driveService =>
            {
                UploadAndOverwriteFile(driveService, path, isAppFile);
            });
        }

        public void UploadFile(RuntimeStatus runtimeStatus, string path)
        {
            _gDriveServiceRetrier.RetryUntilThrowsNoException(
                () => _gDriveServicePool.ExecuteWithDriveService(driveService =>
                {
                    if (runtimeStatus == RuntimeStatus.UploadedCorruptly &&
                        IsFilePresentOnDrive(driveService, path))
                        _log.DriveWarn("Already present: {}", path);
                    else
                        UploadFile(driveService, path, false);
                }),
                e => _log.Error("Retrying to upload: {}", path));
        }

        private string CreateRemoteFolder(DriveService driveService, string path)
        {
            _log.Info("Creating: {}", path);
            const string mimeType = "application/vnd.google-apps.folder";

            bool haveCreated = false;
            string lastParent = string.Empty;
            string[] folders = _pathUtils.GetRelativeFilePath(path, Paths.ServerPath).Split('\\');
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

            string fileId = GetIdOfPathOnDrive(path);

            if (string.IsNullOrEmpty(fileId))
            {
                _log.DriveWarn("Not found: {}", path);
                return;
            }

            driveService.Files.Delete(fileId).Execute();
            _log.Drive("Deleted: {}", path);
        }

        private DownloadStatus Download(DriveService driveService, string path)
        {
            _log.Info("Downloading: {}", path);
            string fileId = GetIdOfPathOnDrive(path);

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

        private string GetIdOfPathOnDrive(string path)
        {
            return _gDriveServicePool.ExecuteWithDriveService(driveService => GetIdOfPathOnDrive(driveService, path));
        }

        private string GetIdOfPathOnDrive(DriveService driveService, string path)
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
            return !string.IsNullOrEmpty(GetIdOfPathOnDrive(driveService, path));
        }

        private void UploadAndOverwriteFile(DriveService driveService, string path, bool isAppFile)
        {
            DeleteRemoteFile(driveService, path);
            UploadFile(driveService, path, isAppFile);

            if (!isAppFile) return;

            switch (path)
            {
                case Paths.Flags:
                    _log.Info("Server status updated on the Drive");
                    break;
                case Paths.Hashes:
                    _log.Info("Hashes of server files saved to the Drive");
                    break;
            }
        }

        private void UploadFile(DriveService driveService, string path, bool isAppFile)
        {
            if (string.IsNullOrEmpty(path))
                return;

            _log.Info("Uploading: {}", path);
            const string mimeType = "application/octet-stream";

            string[] pathSplit = path.Split('\\');
            Array.Resize(ref pathSplit, pathSplit.Length - 1);
            string parentPath = string.Join(@"\", pathSplit);
            string parentId = GetIdOfPathOnDrive(driveService, parentPath) ??
                              CreateRemoteFolder(driveService, parentPath);

            using (var fs = new FileStream(!isAppFile ? $@"{Paths.ServerPath}\{path}" : $@"{Paths.AppPath}\{path}",
                       FileMode.Open))
            {
                ExecuteCreateRequest(driveService, parentId, path.Split('\\').LastOrDefault(), mimeType, fs);
            }

            _log.Drive("Uploaded: {}", path);
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
            // TODO: potential log
        }
    }
}