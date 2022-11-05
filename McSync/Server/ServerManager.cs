using System;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class ServerManager
    {
        private readonly HardwareInfoRepository _hardwareInfoRepository;

        public ServerManager(HardwareInfoRepository hardwareInfoRepository)
        {
            _hardwareInfoRepository = hardwareInfoRepository;
        }

        public CalculatedStatus CalculateStatusByFlags(Flags flags)
        {
            if (flags.Owner == null && flags.SavedStatus == null)
                return CalculatedStatus.UpToDate;

            if (string.IsNullOrEmpty(flags.Owner) || flags.SavedStatus == null)
            {
                throw new ArgumentException("Flags are corrupted");
            }

            var currentPcId = _hardwareInfoRepository.GetPcId();
            switch (flags.SavedStatus)
            {
                case SavedStatus.RUNNING when flags.Owner == currentPcId:
                    return CalculatedStatus.StoppedCorruptly;
                case SavedStatus.RUNNING:
                    return CalculatedStatus.AlreadyRunningElsewhere;
                case SavedStatus.STOPPED when flags.Owner == currentPcId:
                    return CalculatedStatus.UpToDate;
                case SavedStatus.STOPPED:
                    return CalculatedStatus.Outdated;
                case SavedStatus.UPDATING when flags.Owner == currentPcId:
                    return CalculatedStatus.UploadedCorruptly;
                case SavedStatus.UPDATING:
                    return CalculatedStatus.AlreadyUpdatingElsewhere;
            }

            throw new ArgumentException("Flags are corrupted");
        }
    }
}