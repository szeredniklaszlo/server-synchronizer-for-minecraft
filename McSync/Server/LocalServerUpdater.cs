using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McSync.Files;
using McSync.Files.Local;
using McSync.Files.Local.HashCalculator;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class LocalServerUpdater
    {
        private readonly EnumerableUtils _enumerableUtils;
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly GDriveServiceRetrier _gDriveServiceRetrier;
        private readonly HashCalculatorFactory _hashCalculatorFactory;
        private readonly Log _log;

        public LocalServerUpdater(FileSynchronizer fileSynchronizer,
            GDriveServiceRetrier gDriveServiceRetrier,
            Log log,
            HashCalculatorFactory hashCalculatorFactory,
            EnumerableUtils enumerableUtils)
        {
            _fileSynchronizer = fileSynchronizer;
            _gDriveServiceRetrier = gDriveServiceRetrier;
            _log = log;
            _hashCalculatorFactory = hashCalculatorFactory;
            _enumerableUtils = enumerableUtils;
        }

        public void UpdateServer()
        {
            IDictionary<string, string> remoteHashes = _fileSynchronizer.DownloadHashes();

            // TODO: localFileManager-ben legyen egy field, amivel tároljuk a filtert és a root patht is
            // és csak a szükséges fájlokon dolgozzon
            HashCalculator localServerDirHashCalculator = _hashCalculatorFactory.CreateHashCalculator(Paths.ServerPath);
            IEnumerable<KeyValuePair<string, string>> localHashesForRelativePaths =
                localServerDirHashCalculator.CalculateHashesForFilePaths();

            IDictionary<string, string>
                filesToDelete = CalculateWhatToDelete(localHashesForRelativePaths, remoteHashes);
            IDictionary<string, string> filesToDownload =
                CalculateWhatToDownload(localHashesForRelativePaths, remoteHashes);

            _log.Server(RuntimeStatus.Updating);

            Parallel.ForEach(filesToDelete.Keys, Program.ParallelOptions,
                _fileSynchronizer.LocalFileManager.DeleteIfExisting);
            Parallel.ForEach(filesToDownload.Keys, Program.ParallelOptions, DownloadWithUnlimitedRetry);

            _log.Info("All files synchronized");
            _log.Server(RuntimeStatus.Synchronized);
        }

        private IDictionary<string, string> CalculateWhatToDelete(
            IEnumerable<KeyValuePair<string, string>> hashesForLocalRelativePaths,
            IDictionary<string, string> hashesForRemoteFiles)
        {
            return _enumerableUtils.ToDictionary(hashesForLocalRelativePaths.Except(hashesForRemoteFiles));
        }

        private IDictionary<string, string> CalculateWhatToDownload(
            IEnumerable<KeyValuePair<string, string>> hashesForLocalRelativePaths,
            IDictionary<string, string> hashesForRemoteFiles)
        {
            return _enumerableUtils.ToDictionary(hashesForRemoteFiles.Except(hashesForLocalRelativePaths));
        }

        private void DownloadServerFile(string remotePath)
        {
            _fileSynchronizer.RemoteFileManager.DownloadServerOrAppFile($@"{Paths.ServerPath}\{remotePath}");
        }

        private void DownloadWithUnlimitedRetry(string remotePath)
        {
            _gDriveServiceRetrier.RetryUntilThrowsNoException(
                () => DownloadServerFile(remotePath),
                (e, retries) => { _log.DriveWarn(retries + "th try to download: {} Retrying...", remotePath); });
        }
    }
}