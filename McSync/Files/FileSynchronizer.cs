using System.Collections.Generic;
using Google.Apis.Download;
using McSync.Files.Local;
using McSync.Files.Remote;
using McSync.Utils;

namespace McSync.Files
{
    public class FileSynchronizer
    {
        // TODO: write logs
        private readonly Log _log;

        public FileSynchronizer(LocalFileManager localLocalFileManager, RemoteFileManager remoteFileManager, Log log)
        {
            LocalFileManager = localLocalFileManager;
            RemoteFileManager = remoteFileManager;
            _log = log;
        }

        public LocalFileManager LocalFileManager { get; }
        public RemoteFileManager RemoteFileManager { get; }

        public IDictionary<string, string> DownloadHashes()
        {
            return DownloadJsonFile<Dictionary<string, string>>(Paths.Hashes);
        }

        public T DownloadJsonFile<T>(string path)
        {
            DownloadStatus downloadStatus = RemoteFileManager.DownloadServerOrAppFile(path);
            if (downloadStatus == DownloadStatus.Failed) LocalFileManager.SaveObjectAsJsonFile(path, new object());

            return LocalFileManager.LoadObjectFromJsonFile<T>(path);
        }
    }
}