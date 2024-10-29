using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using McSync.Utils;
using Newtonsoft.Json;

namespace McSync.Files.Local
{
    public class LocalFileManager
    {
        private readonly Log _log;

        public LocalFileManager(Log log)
        {
            _log = log;
        }

        public List<string> DefaultFilters { get; } = new List<string>()
        {
            "sync", "libraries", "openjdk", "net5.0", "ngrok.cmd", "run.cmd"
        };

        public void DeleteIfExisting(string localPath)
        {
            try
            {
                File.Delete($@"{Paths.ServerPath}\{localPath}");
                //_log.Local("Deleted: {}", localPath);
            }
            catch (Exception)
            {
                //_log.Local("Already deleted: {}", localPath);
            }
        }

        public List<FileInfo> FilterFilesInDirectory(string directoryPath, List<string> filters = null)
        {
            filters = filters ?? DefaultFilters;
            var directoryInfo = new DirectoryInfo(directoryPath);
            List<FileInfo> filteredFileInfos = directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => filters.TrueForAll(
                        filterFragment => !file.FullName.ToLower().Contains(filterFragment.ToLower())
                    )
                )
                .ToList();
            return filteredFileInfos;
        }

        public T LoadObjectFromJsonFile<T>(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found", path);

            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public void SaveObjectAsJsonFile(string path, object obj)
        {
            string json = JsonConvert.SerializeObject(obj);
            File.WriteAllText(path, json);
            //_log.Local("Local file created: {}", path);
        }
    }
}