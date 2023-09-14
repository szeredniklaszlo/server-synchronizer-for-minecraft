using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using McSync.Files;
using McSync.Files.Local;
using McSync.Files.Local.HashCalculator;
using McSync.Files.Remote;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Server
{
    public class RemoteServerUpdater
    {
        private readonly EnumerableUtils _enumerableUtils;
        private readonly FileSynchronizer _fileSynchronizer;
        private readonly FlagSynchronizer _flagSynchronizer;
        private readonly GDriveServicePool _gDriveServicePool;
        private readonly HashCalculatorFactory _hashCalculatorFactory;
        private readonly Log _log;
        private readonly RuntimeStatusUpdater _runtimeStatusUpdater;

        public RemoteServerUpdater(Log log,
            FileSynchronizer fileSynchronizer,
            FlagSynchronizer flagSynchronizer,
            RuntimeStatusUpdater runtimeStatusUpdater,
            EnumerableUtils enumerableUtils,
            GDriveServicePool gDriveServicePool,
            HashCalculatorFactory hashCalculatorFactory)
        {
            _log = log;
            _fileSynchronizer = fileSynchronizer;
            _flagSynchronizer = flagSynchronizer;
            _runtimeStatusUpdater = runtimeStatusUpdater;
            _enumerableUtils = enumerableUtils;
            _gDriveServicePool = gDriveServicePool;
            _hashCalculatorFactory = hashCalculatorFactory;
        }

        public void UpdateServerAndFlags()
        {
            _flagSynchronizer.UpdateFlags(PersistedStatus.Updating);
            UpdateServer();
            _flagSynchronizer.UpdateFlags(PersistedStatus.Stopped);
        }

        private IDictionary<string, string> CalculateFilesToDelete(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes)
        {
            return _enumerableUtils.ToDictionary(remoteHashes.Except(localHashes));
        }

        private Dictionary<string, string> CalculateFilesToUpdate(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes,
            IDictionary<string, string> filesToDelete,
            IDictionary<string, string> filesToUpload)
        {
            IDictionary<string, string> remoteHashesFiltered =
                _enumerableUtils.ToDictionary(remoteHashes.Except(filesToDelete));
            IDictionary<string, string> calculatedHashesFiltered =
                _enumerableUtils.ToDictionary(localHashes.Except(filesToUpload));

            return remoteHashesFiltered
                .Where(remoteHash => calculatedHashesFiltered[remoteHash.Key] != remoteHash.Value)
                .ToDictionary(remoteHash => remoteHash.Key, remoteHash => remoteHash.Value);
        }

        private IDictionary<string, string> CalculateFilesToUpload(IDictionary<string, string> remoteHashes,
            IDictionary<string, string> localHashes)
        {
            return _enumerableUtils.ToDictionary(localHashes.Except(remoteHashes));
        }

        private IDictionary<string, string> CalculateHashesForLocalServerDirWithRelativePaths()
        {
            HashCalculator localServerDirHashCalculator = _hashCalculatorFactory.CreateHashCalculator(Paths.ServerPath);
            return localServerDirHashCalculator.CalculateHashesForFilePaths();
        }

        private void ListDriveFiles()
        {
            // Define parameters of request.
            FilesResource.ListRequest listRequest =
                _gDriveServicePool.ExecuteWithDriveService(driveService => driveService.Files.List());
            listRequest.PageSize = 10;
            listRequest.Fields = "nextPageToken, files(id, name, mimeType)";

            // List files.
            IList<File> files = listRequest.Execute()
                .Files;
            Console.WriteLine("Files:");
            if (files != null && files.Count > 0)
                foreach (File file in files)
                    Console.WriteLine("{0} ({1}) {2}", file.Name, file.Id, file.MimeType);
            else Console.WriteLine("No files found.");
        }

        private IDictionary<string, string> RecalculateAndSaveLocalHashes()
        {
            IDictionary<string, string> hashesForRelativePaths = CalculateHashesForLocalServerDirWithRelativePaths();
            _fileSynchronizer.LocalFileManager.SaveObjectAsJsonFile(Paths.Hashes, hashesForRelativePaths);
            return hashesForRelativePaths;
        }

        private void UpdateServer()
        {
            IDictionary<string, string> remoteHashes = _fileSynchronizer.DownloadHashes();
            IDictionary<string, string> updatedLocalHashes = RecalculateAndSaveLocalHashes();

            IDictionary<string, string> filesToDelete = CalculateFilesToDelete(remoteHashes, updatedLocalHashes);
            IDictionary<string, string> filesToUpload = CalculateFilesToUpload(remoteHashes, updatedLocalHashes);
            Dictionary<string, string> filesToUpdate =
                CalculateFilesToUpdate(remoteHashes, updatedLocalHashes, filesToDelete, filesToUpload);

            _log.Server(RuntimeStatus.Uploading);
            Parallel.ForEach(filesToDelete, Program.ParallelOptions,
                toDelete => _fileSynchronizer.RemoteFileManager.DeleteRemoteFile(toDelete.Key));

            _fileSynchronizer.RemoteFileManager.CreateRemoteFolderTreeFromLocalFolderRecursively(Paths.ServerPath);
            Parallel.ForEach(filesToUpload, Program.ParallelOptions,
                toUpload => _fileSynchronizer.RemoteFileManager.UploadFile(
                    _runtimeStatusUpdater.RuntimeStatus,
                    toUpload.Key));

            Parallel.ForEach(filesToUpdate, Program.ParallelOptions,
                toUpdate => _fileSynchronizer.RemoteFileManager.UpdateRemoteFile(toUpdate.Key));
            _log.Info("All files synchronized");

            _fileSynchronizer.RemoteFileManager.UploadAndOverwriteFile(Paths.Hashes, true);
            _log.Server(RuntimeStatus.Synchronized);
        }
    }
}