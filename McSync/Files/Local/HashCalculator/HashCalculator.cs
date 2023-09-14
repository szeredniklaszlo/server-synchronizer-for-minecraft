using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McSync.Utils;
using Standart.Hash.xxHash;

namespace McSync.Files.Local.HashCalculator
{
    public class HashCalculator
    {
        private readonly List<FileInfo> _files;
        private readonly Log _log;
        private readonly PathUtils _pathUtils;

        private ConcurrentDictionary<FileInfo, string> _hashesCalculated;

        public HashCalculator(Log log, PathUtils pathUtils, List<FileInfo> files)
        {
            _log = log;
            _pathUtils = pathUtils;
            _files = files;
        }

        public IDictionary<string, string> CalculateHashesForFilePaths()
        {
            IDictionary<FileInfo, string> hashesForFiles = CalculateHashesForFiles();
            return hashesForFiles.ToDictionary(
                ExtractKeyFromFileToRelativePath,
                pair => pair.Value);
        }

        public IDictionary<FileInfo, string> CalculateHashesForFiles()
        {
            _log.Info("Calculating hashes");
            _hashesCalculated = new ConcurrentDictionary<FileInfo, string>();
            Parallel.ForEach(_files, Program.ParallelOptions, AddHashOfFile);

            _log.Info("Hashes calculated");
            return _hashesCalculated;
        }

        private void AddHashOfFile(FileInfo fileInfo)
        {
            string hash = CalculateHashOfFile(fileInfo);
            _hashesCalculated.TryAdd(fileInfo, hash);
        }

        private string CalculateHashOfFile(FileInfo fileInfo)
        {
            using (FileStream stream = fileInfo.OpenRead())
            {
                stream.Position = 0;
                return xxHash64.ComputeHash(stream).ToString();
            }
        }

        private string ExtractKeyFromFileToRelativePath(KeyValuePair<FileInfo, string> pair)
        {
            return _pathUtils.GetRelativeFilePath(pair.Key.FullName, Paths.ServerPath);
        }
    }
}