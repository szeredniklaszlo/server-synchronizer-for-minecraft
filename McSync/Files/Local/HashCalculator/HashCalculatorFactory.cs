using System.Collections.Generic;
using System.IO;
using McSync.Utils;

namespace McSync.Files.Local.HashCalculator
{
    public class HashCalculatorFactory
    {
        private readonly LocalFileManager _localFileManager;
        private readonly Log _log;
        private readonly PathUtils _pathUtils;

        public HashCalculatorFactory(Log log, PathUtils pathUtils, LocalFileManager localFileManager = null)
        {
            _log = log;
            _pathUtils = pathUtils;
            _localFileManager = localFileManager;
        }

        public HashCalculator CreateHashCalculator(List<FileInfo> files)
        {
            return new HashCalculator(_log, _pathUtils, files);
        }

        public HashCalculator CreateHashCalculator(string folderPath)
        {
            List<FileInfo> filteredFilesInDirectory = _localFileManager.FilterFilesInDirectory(folderPath);
            return CreateHashCalculator(filteredFilesInDirectory);
        }
    }
}