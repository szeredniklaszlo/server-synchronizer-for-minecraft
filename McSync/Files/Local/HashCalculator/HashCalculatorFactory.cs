using System.Collections.Generic;
using System.IO;
using McSync.Utils;

namespace McSync.Files.Local.HashCalculator
{
    public class HashCalculatorFactory
    {
        private readonly Log _log;

        public HashCalculatorFactory(Log log)
        {
            _log = log;
        }

        public HashCalculator CreateHashCalculator(List<FileInfo> files)
        {
            return new HashCalculator(_log, files);
        }
    }
}