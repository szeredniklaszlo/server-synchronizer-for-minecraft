using System.Collections.Generic;

namespace McSync.Files.Local.HashCalculator
{
    public class FiltersForDirectory
    {
        public FiltersForDirectory(string directoryPath, List<string> filters = null)
        {
            DirectoryPath = directoryPath;
            Filters = filters;
        }

        public string DirectoryPath { get; }
        public List<string> Filters { get; }
    }
}