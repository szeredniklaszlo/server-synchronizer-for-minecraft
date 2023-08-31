using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McSync.Exceptions;
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
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly HardwareInfoRetriever _hardwareInfoRetriever;
        private readonly HashCalculatorFactory _hashCalculatorFactory;
        private readonly LocalFileManager _localFileManager;
        private readonly Log _log;
        private readonly ProcessController _processController;
        private readonly RemoteFileManager _remoteFileManager;
        private readonly Retrier _retrier;
        private CalculatedStatus _calculatedStatus;

        private Flags _flags;

        public ServerManager(HardwareInfoRetriever hardwareInfoRetriever,
            LocalFileManager localFileManager, HashCalculatorFactory hashCalculatorFactory,
            RemoteFileManager remoteFileManager, FileSynchronizer fileSynchronizer, Log log,
            ProcessController processController, Retrier retrier)
        {
            _hardwareInfoRetriever = hardwareInfoRetriever;
            _localFileManager = localFileManager;
            _hashCalculatorFactory = hashCalculatorFactory;
            _remoteFileManager = remoteFileManager;
            _fileSynchronizer = fileSynchronizer;
            _log = log;
            _processController = processController;
            _retrier = retrier;
        }

        public void Run()
        {
            DownloadFlags();
            CalculateStatus();
            ManageServerLifecycle();
        }

        private bool AreFlagsCorrupted()
        {
            return string.IsNullOrEmpty(_flags.Owner) || _flags.LifecycleStatus == null;
        }

        private void CalculateStatus()
        {
            if (IsVeryFirstServerStart())
                _calculatedStatus = CalculatedStatus.UpToDate;
            else if (AreFlagsCorrupted())
                throw new ArgumentException("Flags are corrupted");
            else ConvertMapSavedStatusToCalculatedStatus();
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

        private void CheckIfPortableJavaIsPresent()
        {
            DirectoryInfo portableJavaHome = GetPortableJavaHome();
            if (!IsJavaPresentInJavaHome(portableJavaHome)) throw new JreNotFoundException();
        }

        private void ConvertMapSavedStatusToCalculatedStatus()
        {
            string currentPcId = _hardwareInfoRetriever.GetPcId();
            switch (_flags.LifecycleStatus)
            {
                case PersistedStatus.Running when _flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.StoppedCorruptly;
                    break;
                case PersistedStatus.Running:
                    _calculatedStatus = CalculatedStatus.AlreadyRunningElsewhere;
                    break;
                case PersistedStatus.Stopped when _flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.UpToDate;
                    break;
                case PersistedStatus.Stopped:
                    _calculatedStatus = CalculatedStatus.Outdated;
                    break;
                case PersistedStatus.Updating when _flags.Owner == currentPcId:
                    _calculatedStatus = CalculatedStatus.UploadedCorruptly;
                    break;
                case PersistedStatus.Updating:
                    _calculatedStatus = CalculatedStatus.AlreadyUpdatingElsewhere;
                    break;
            }

            throw new ArgumentException("Flags are corrupted");
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

        private void DownloadFlags()
        {
            _flags = _fileSynchronizer.DownloadJsonFile<Flags>(Paths.Flags);
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
            _retrier.RetryUntilThrowsNoException(
                () => DownloadServerFile(remotePath),
                exception => { _log.DriveWarn("Retrying to download: {}", remotePath); });
        }

        private void ExecuteTokenCreation()
        {
            const string createTokenCommand = "ngrok authtoken 22NyU96RrxqrNvxb5Y7eJw08ZKl_5ZVerYF6Ei5XJN5b9E2TX";
            Process tokenCreator = _processController.RunCmdCommand(createTokenCommand);
            tokenCreator.WaitForExit();
        }

        private DirectoryInfo GetPortableJavaHome()
        {
            try
            {
                return new DirectoryInfo(Paths.Java17Home);
            }
            catch (DirectoryNotFoundException)
            {
                _log.Error($@"Java 17 home folder is not found: {Paths.Java17Home}");
                Environment.Exit(1);
            }

            return null;
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

        private bool IsJavaPresentInJavaHome(DirectoryInfo javaHome)
        {
            return javaHome.GetFiles().Any(file => file.Name == "java.exe");
        }

        private bool IsVeryFirstServerStart()
        {
            return _flags.Owner == null && _flags.LifecycleStatus == null;
        }

        private void ManageServerLifecycle()
        {
            HandleCalculatedStatus();
            RunUntilClosed();
            UpdateRemote();
        }

        private void RunUntilClosed()
        {
            List<Process> serverProcesses = StartAllProcesses();
            _processController.WaitProcessesToBeClosed(serverProcesses);
        }

        private List<Process> StartAllProcesses()
        {
            _log.Server(CalculatedStatus.Starting);

            ExecuteTokenCreation();
            List<Process> processes = StartServerProcesses();

            _log.Server(CalculatedStatus.Running);
            UpdateFlags(PersistedStatus.Running);
            return processes;
        }

        private Process StartServerJar()
        {
            CheckIfPortableJavaIsPresent();
            string serverJar = new DirectoryInfo(Paths.ServerPath)
                .GetFiles()
                .FirstOrDefault(file => file.Extension == ".jar")?.Name;

            string arguments = $@"-Xms4G -Xmx4G -XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 
-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1HeapWastePercent=5 
-XX:G1MixedGCCountTarget=4 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 
-XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -XX:G1NewSizePercent=30 
-XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:InitiatingHeapOccupancyPercent=15 
-Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true -jar {serverJar} nogui";
            string java17Path = $@"{Paths.Java17Home}\java";
            return _processController.RunCmdCommand($"{java17Path} {arguments}");
        }

        private List<Process> StartServerProcesses()
        {
            return new List<Process>
            {
                StartServerJar(),
                StartTcpTunnelProvider()
            };
        }

        private Process StartTcpTunnelProvider()
        {
            string openNgrokCommand = "ngrok.exe tcp 25565 --region=eu";
            return _processController.RunCmdCommand(openNgrokCommand);
        }

        private void UpdateFlags(PersistedStatus status)
        {
            UpdateLocalFlags(status);
            _remoteFileManager.UploadAndOverwriteFile(Paths.Flags, true);
        }

        private void UpdateLocalFlags(PersistedStatus status)
        {
            var flags = _localFileManager.LoadObjectFromJsonFile<Flags>(Paths.Flags);

            flags.Owner = _hardwareInfoRetriever.GetPcId();
            flags.LifecycleStatus = status;

            _localFileManager.SaveObjectAsJsonFile(Paths.Flags, flags);
            _log.Info($"Flag 'running' updated to '{status}'");
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

        private void UpdateRemote()
        {
            UpdateFlags(PersistedStatus.Updating);
            UpdateRemoteServer(_calculatedStatus);
            UpdateFlags(PersistedStatus.Stopped);
        }

        private void UpdateRemoteServer(CalculatedStatus calculatedStatus)
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
                toUpload => _remoteFileManager.UploadFile(calculatedStatus, toUpload.Key));

            Parallel.ForEach(filesToUpdate, Program.ParallelOptions,
                toUpdate => _remoteFileManager.UpdateRemoteFile(toUpdate.Key));
            _log.Info("All files synchronized");

            _remoteFileManager.UploadAndOverwriteFile(Paths.Hashes, true);
            _log.Server(CalculatedStatus.Synchronized);
        }
    }
}