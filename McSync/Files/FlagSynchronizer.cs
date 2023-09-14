using System;
using McSync.Files.Local;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Files
{
    public class FlagSynchronizer
    {
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly HardwareInfoRetriever _hardwareInfoRetriever;
        private readonly Log _log;

        public FlagSynchronizer(Log log, HardwareInfoRetriever hardwareInfoRetriever, FileSynchronizer fileSynchronizer)
        {
            _log = log;
            _hardwareInfoRetriever = hardwareInfoRetriever;
            _fileSynchronizer = fileSynchronizer;
        }

        public Flags Flags { get; private set; }

        public void DownloadFlags()
        {
            Flags = _fileSynchronizer.DownloadJsonFile<Flags>(Paths.Flags);
        }

        public bool IsVeryFirstServerStart()
        {
            return Flags.Owner == null && Flags.PersistedStatus == null;
        }

        public void UpdateFlags(PersistedStatus status)
        {
            UpdateLocalFlags(status);
            _fileSynchronizer.RemoteFileManager.UploadAndOverwriteFile(Paths.Flags, true);
        }

        public void ValidateFlags()
        {
            if (string.IsNullOrEmpty(Flags.Owner) || Flags.PersistedStatus == null)
                throw new ArgumentException("Flags are corrupted");
        }

        private void UpdateLocalFlags(PersistedStatus status)
        {
            Flags.Owner = _hardwareInfoRetriever.GetPcId();
            Flags.PersistedStatus = status;

            _fileSynchronizer.LocalFileManager.SaveObjectAsJsonFile(Paths.Flags, Flags);
            _log.Info($"Flag 'PersistedStatus' updated to '{status}'");
        }
    }
}