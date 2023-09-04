using System;
using McSync.Files.Local;
using McSync.Files.Remote;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Files
{
    public class FlagSynchronizer
    {
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly HardwareInfoRetriever _hardwareInfoRetriever;
        private readonly LocalFileManager _localFileManager;
        private readonly Log _log;
        private readonly RemoteFileManager _remoteFileManager;

        public FlagSynchronizer(Log log,
            RemoteFileManager remoteFileManager,
            LocalFileManager localFileManager,
            HardwareInfoRetriever hardwareInfoRetriever, FileSynchronizer fileSynchronizer)
        {
            _log = log;
            _remoteFileManager = remoteFileManager;
            _localFileManager = localFileManager;
            _hardwareInfoRetriever = hardwareInfoRetriever;
            _fileSynchronizer = fileSynchronizer;
        }

        public Flags Flags { get; private set; }

        public void DownloadFlags()
        {
            Flags = _fileSynchronizer.DownloadJsonFile<Flags>(Paths.Flags);
        }

        public void UpdateRemoteFlags(PersistedStatus status)
        {
            UpdateLocalFlags(status);
            _remoteFileManager.UploadAndOverwriteFile(Paths.Flags, true);
        }

        public void ValidateFlags()
        {
            if (string.IsNullOrEmpty(Flags.Owner) || Flags.LifecycleStatus == null)
                throw new ArgumentException("Flags are corrupted");
        }

        private void UpdateLocalFlags(PersistedStatus status)
        {
            var flags = _localFileManager.LoadObjectFromJsonFile<Flags>(Paths.Flags);

            flags.Owner = _hardwareInfoRetriever.GetPcId();
            flags.LifecycleStatus = status;

            _localFileManager.SaveObjectAsJsonFile(Paths.Flags, flags);
            _log.Info($"Flag 'running' updated to '{status}'");
        }
    }
}