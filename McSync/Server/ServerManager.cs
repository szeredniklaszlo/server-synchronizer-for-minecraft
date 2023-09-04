using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McSync.Files;
using McSync.Files.Local;
using McSync.Files.Local.HashCalculator;
using McSync.Files.Remote;
using McSync.Processes;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class ServerManager
    {
        private readonly DriveServiceRetrier _driveServiceRetrier;
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly FlagSynchronizer _flagSynchronizer;
        private readonly HardwareInfoRetriever _hardwareInfoRetriever;
        private readonly HashCalculatorFactory _hashCalculatorFactory;
        private readonly LocalFileManager _localFileManager;
        private readonly Log _log;
        private readonly RemoteFileManager _remoteFileManager;
        private readonly ServerProcessRunner _serverProcessRunner;
        private CalculatedStatus _calculatedStatus;

        public ServerManager(HardwareInfoRetriever hardwareInfoRetriever,
            LocalFileManager localFileManager, HashCalculatorFactory hashCalculatorFactory,
            RemoteFileManager remoteFileManager, FileSynchronizer fileSynchronizer, Log log,
            DriveServiceRetrier driveServiceRetrier, ServerProcessRunner serverProcessRunner,
            FlagSynchronizer flagSynchronizer)
        {
            _hardwareInfoRetriever = hardwareInfoRetriever;
            _localFileManager = localFileManager;
            _hashCalculatorFactory = hashCalculatorFactory;
            _remoteFileManager = remoteFileManager;
            _fileSynchronizer = fileSynchronizer;
            _log = log;
            _driveServiceRetrier = driveServiceRetrier;
            _serverProcessRunner = serverProcessRunner;
            _flagSynchronizer = flagSynchronizer;
        }

        public void Run()
        {
            _flagSynchronizer.DownloadFlags();
            CalculateStatus();
            ManageServerLifecycle();
        }

        private void CalculateStatus()
        {
            if (IsVeryFirstServerStart())
                _calculatedStatus = CalculatedStatus.UpToDate;
            else
            {
                _flagSynchronizer.ValidateFlags();
                MapSavedStatusToCalculatedStatus();
            }
        }

        private static Dictionary<string, string> CalculateWhatToDelete(
            IEnumerable<KeyValuePair<string, string>> hashesForLocalRelativePaths,
            IDictionary<string, string> hashesForRemoteFiles)
        {
            return hashesForLocalRelativePaths.Except(hashesForRemoteFiles)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private static Dictionary<string, string> CalculateWhatToDownload(
            IEnumerable<KeyValuePair<string, string>> hashesForLocalRelativePaths,
            IDictionary<string, string> hashesForRemoteFiles)
        {
            return hashesForRemoteFiles.Except(hashesForLocalRelativePaths)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private void DeleteIfExisting(string localPath)
        {
            try
            {
                File.Delete($@"{Paths.ServerPath}\{localPath}");
                _log.Local("Deleted: {}", localPath);
            }
            catch (Exception)
            {
                _log.Local("Already deleted: {}", localPath);
            }
        }

        private IDictionary<string, string> DownloadRemoteHashes()
        {
            return _fileSynchronizer.DownloadJsonFile<Dictionary<string, string>>(Paths.Hashes);
        }

        private void DownloadServerFile(string remotePath)
        {
            _remoteFileManager.DownloadServerOrAppFile($@"{Paths.ServerPath}\{remotePath}");
        }

        private void DownloadWithUnlimitedRetry(string remotePath)
        {
            _driveServiceRetrier.RetryUntilThrowsNoException(
                () => DownloadServerFile(remotePath),
                exception => { _log.DriveWarn("Retrying to download: {}", remotePath); });
        }

        private void HandleCalculatedStatus()
        {
            _log.Server(_calculatedStatus);
            switch (_calculatedStatus)
            {
                case CalculatedStatus.AlreadyRunningElsewhere:
                case CalculatedStatus.AlreadyUpdatingElsewhere:
                case CalculatedStatus.Running:
                    _log.Info("Exiting...");
                    Environment.Exit(1);
                    return;
                case CalculatedStatus.Outdated:
                    UpdateLocalServer();
                    break;
            }
        }

        private bool IsVeryFirstServerStart()
        {
            return _flagSynchronizer.Flags.Owner == null && _flagSynchronizer.Flags.LifecycleStatus == null;
        }

        private void ManageServerLifecycle()
        {
            HandleCalculatedStatus();
            _serverProcessRunner.RunUntilClosed();
            UpdateRemoteServerAndFlags();
        }

        private void MapSavedStatusToCalculatedStatus()
        {
            string currentPcId = _hardwareInfoRetriever.GetPcId();
            switch (_flagSynchronizer.Flags.LifecycleStatus)
            {
                case PersistedStatus.Running when _flagSynchronizer.Flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.StoppedCorruptly;
                    break;
                case PersistedStatus.Running:
                    _calculatedStatus = CalculatedStatus.AlreadyRunningElsewhere;
                    break;
                case PersistedStatus.Stopped when _flagSynchronizer.Flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.UpToDate;
                    break;
                case PersistedStatus.Stopped:
                    _calculatedStatus = CalculatedStatus.Outdated;
                    break;
                case PersistedStatus.Updating when _flagSynchronizer.Flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.UploadedCorruptly;
                    break;
                case PersistedStatus.Updating:
                    _calculatedStatus = CalculatedStatus.AlreadyUpdatingElsewhere;
                    break;
            }

            throw new ArgumentException("Flags are corrupted");
        }

        private void UpdateLocalServer()
        {
            IDictionary<string, string> remoteHashes = DownloadRemoteHashes();

            // TODO: localFileManager-ben legyen egy field, amivel tároljuk a filtert és a root patht is
            // és csak a szükséges fájlokon dolgozzon
            List<FileInfo> filteredLocal = _localFileManager.FilterFilesInDirectory(Paths.ServerPath);
            HashCalculator hashCalculator = _hashCalculatorFactory.CreateHashCalculator(filteredLocal);

            IDictionary<FileInfo, string> localHashes = hashCalculator.CalculateHashes();
            IEnumerable<KeyValuePair<string, string>> localHashesForRelativePaths = localHashes.Select(pair =>
                new KeyValuePair<string, string>(
                    _localFileManager.GetRelativeFilePath(
                        pair.Key.FullName,
                        Paths.ServerPath),
                    pair.Value));

            Dictionary<string, string> filesToDelete = CalculateWhatToDelete(localHashesForRelativePaths, remoteHashes);
            Dictionary<string, string> filesToDownload =
                CalculateWhatToDownload(localHashesForRelativePaths, remoteHashes);

            _log.Server(CalculatedStatus.Updating);

            Parallel.ForEach(filesToDelete.Keys, Program.ParallelOptions, DeleteIfExisting);
            Parallel.ForEach(filesToDownload.Keys, Program.ParallelOptions, DownloadWithUnlimitedRetry);

            _log.Info("All files synchronized");
            _log.Server(CalculatedStatus.Synchronized);
        }

        private void UpdateRemoteServer()
        {
            IDictionary<string, string> remoteHashes = DownloadRemoteHashes();
            IDictionary<string, string> updatedLocalHashes = _remoteFileManager.UpdateHashes();

            IDictionary<string, string> filesToDelete =
                _remoteFileManager.CalculateFilesToDelete(remoteHashes, updatedLocalHashes);
            IDictionary<string, string> filesToUpload =
                _remoteFileManager.CalculateFilesToUpload(remoteHashes, updatedLocalHashes);
            Dictionary<string, string> filesToUpdate =
                _remoteFileManager.CalculateFilesToUpdate(remoteHashes, updatedLocalHashes, filesToDelete,
                    filesToUpload);

            _log.Server(CalculatedStatus.Uploading);
            Parallel.ForEach(filesToDelete, Program.ParallelOptions,
                toDelete => _remoteFileManager.DeleteRemoteFile(toDelete.Key));

            _remoteFileManager.CreateRemoteFolderTreeFromLocalFolderRecursively(Paths.ServerPath);
            Parallel.ForEach(filesToUpload, Program.ParallelOptions,
                toUpload => _remoteFileManager.UploadFile(_calculatedStatus, toUpload.Key));

            Parallel.ForEach(filesToUpdate, Program.ParallelOptions,
                toUpdate => _remoteFileManager.UpdateRemoteFile(toUpdate.Key));
            _log.Info("All files synchronized");

            _remoteFileManager.UploadAndOverwriteFile(Paths.Hashes, true);
            _log.Server(CalculatedStatus.Synchronized);
        }

        private void UpdateRemoteServerAndFlags()
        {
            _flagSynchronizer.UpdateRemoteFlags(PersistedStatus.Updating);
            UpdateRemoteServer();
            _flagSynchronizer.UpdateRemoteFlags(PersistedStatus.Stopped);
        }
    }
}