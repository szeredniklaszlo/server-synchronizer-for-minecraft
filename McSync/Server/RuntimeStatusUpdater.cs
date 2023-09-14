using System;
using McSync.Files;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class RuntimeStatusUpdater
    {
        private readonly FlagSynchronizer _flagSynchronizer;
        private readonly HardwareInfoRetriever _hardwareInfoRetriever;

        public RuntimeStatusUpdater(FlagSynchronizer flagSynchronizer, HardwareInfoRetriever hardwareInfoRetriever)
        {
            _flagSynchronizer = flagSynchronizer;
            _hardwareInfoRetriever = hardwareInfoRetriever;
        }

        public RuntimeStatus RuntimeStatus { get; private set; }

        public void CalculateInitialRuntimeStatus()
        {
            if (_flagSynchronizer.IsVeryFirstServerStart())
            {
                RuntimeStatus = RuntimeStatus.UpToDate;
            }
            else
            {
                _flagSynchronizer.ValidateFlags();
                MapPersistedStatusToRuntimeStatus();
            }
        }

        private void MapPersistedStatusToRuntimeStatus()
        {
            string currentPcId = _hardwareInfoRetriever.GetPcId();
            switch (_flagSynchronizer.Flags.PersistedStatus)
            {
                case PersistedStatus.Running when _flagSynchronizer.Flags.Owner == currentPcId:
                    RuntimeStatus = RuntimeStatus.StoppedCorruptly;
                    break;
                case PersistedStatus.Running:
                    RuntimeStatus = RuntimeStatus.AlreadyRunningElsewhere;
                    break;
                case PersistedStatus.Stopped when _flagSynchronizer.Flags.Owner == currentPcId:
                    RuntimeStatus = RuntimeStatus.UpToDate;
                    break;
                case PersistedStatus.Stopped:
                    RuntimeStatus = RuntimeStatus.Outdated;
                    break;
                case PersistedStatus.Updating when _flagSynchronizer.Flags.Owner == currentPcId:
                    RuntimeStatus = RuntimeStatus.UploadedCorruptly;
                    break;
                case PersistedStatus.Updating:
                    RuntimeStatus = RuntimeStatus.AlreadyUpdatingElsewhere;
                    break;
            }

            throw new ArgumentException("Flags are corrupted");
        }
    }
}