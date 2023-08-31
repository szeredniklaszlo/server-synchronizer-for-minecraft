using McSync.Files.Local;
using McSync.Files.Remote;
using McSync.Utils;

namespace McSync.Files
{
    public class FileSynchronizer
    {
        private readonly LocalFileManager _localFileManager;

        // TODO: write logs
        private readonly Log _log;
        private readonly RemoteFileManager _remoteFileManager;

        public FileSynchronizer(LocalFileManager localFileManager, RemoteFileManager remoteFileManager, Log log)
        {
            _localFileManager = localFileManager;
            _remoteFileManager = remoteFileManager;
            _log = log;
        }

        public T DownloadJsonFile<T>(string path)
        {
            _remoteFileManager.DownloadOrCreateAppJsonFile(path);
            return _localFileManager.LoadObjectFromJsonFile<T>(path);
        }
    }
}