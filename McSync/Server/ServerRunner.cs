using McSync.Files;
using McSync.Processes;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class ServerRunner
    {
        private readonly FlagSynchronizer _flagSynchronizer;

        private readonly LocalServerUpdater _localServerUpdater;
        private readonly Log _log;
        private readonly RemoteServerUpdater _remoteServerUpdater;
        private readonly RuntimeStatusUpdater _runtimeStatusUpdater;
        private readonly ServerProcessRunner _serverProcessRunner;

        public ServerRunner(Log log,
            ServerProcessRunner serverProcessRunner,
            FlagSynchronizer flagSynchronizer,
            LocalServerUpdater localServerUpdater,
            RemoteServerUpdater remoteServerUpdater,
            RuntimeStatusUpdater runtimeStatusUpdater)
        {
            _log = log;
            _serverProcessRunner = serverProcessRunner;
            _flagSynchronizer = flagSynchronizer;
            _localServerUpdater = localServerUpdater;
            _remoteServerUpdater = remoteServerUpdater;
            _runtimeStatusUpdater = runtimeStatusUpdater;
        }

        public void Execute()
        {
            _flagSynchronizer.DownloadFlags();
            _runtimeStatusUpdater.CalculateInitialRuntimeStatus();
            var initializationSuccess = HandleInitialStatus();
            if (!initializationSuccess) return;
            _serverProcessRunner.RunUntilClosed();
            _remoteServerUpdater.UpdateServerAndFlags();
        }

        private bool HandleInitialStatus()
        {
            _log.Server(_runtimeStatusUpdater.RuntimeStatus);
            switch (_runtimeStatusUpdater.RuntimeStatus)
            {
                case RuntimeStatus.AlreadyRunningElsewhere:
                case RuntimeStatus.AlreadyUpdatingElsewhere:
                case RuntimeStatus.Running:
                    _log.Info("Already running or updating elsewhere. Exiting...");
                    return false;
                case RuntimeStatus.Outdated:
                    _localServerUpdater.UpdateServer();
                    break;
            }

            return true;
        }
    }
}