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

        public List<FileInfo> FilterFilesInDirectory(string directoryPath, List<string> filters = null)
        {
            filters = filters ?? new List<string>();
            var directoryInfo = new DirectoryInfo(directoryPath);
            List<FileInfo> filteredFileInfos = directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => filters.TrueForAll(
                        filterFragment => !file.FullName.ToLower().Contains(filterFragment.ToLower())
                    )
                )
                .ToList();
            return filteredFileInfos;
        }

        public string GetRelativeFilePath(string fullPath, string relativeToPath)
        {
            if (!IsAbsolutePathValid(fullPath))
                throw new ArgumentException($"Invalid path: {fullPath}", nameof(fullPath));

            if (!IsAbsolutePathValid(relativeToPath))
                throw new ArgumentException($"Invalid path: {relativeToPath}", nameof(relativeToPath));

            relativeToPath = AttachSlashIfNotPresent(relativeToPath);
            if (!fullPath.StartsWith(relativeToPath))
                throw new ArgumentException(
                    $"Invalid paths: relativeToPath |{relativeToPath}| is not contained by fullPath |{fullPath}|",
                    nameof(relativeToPath));

            return SplitFullPathWithRelativePath(fullPath, relativeToPath);
        }

        public T LoadObjectFromJsonFile<T>(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found", path);

            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public void SaveObjectAsJsonFile(string filePath, object obj)
        {
            string json = JsonConvert.SerializeObject(obj);
            File.WriteAllText(filePath, json);
            _log.Local("Saved: {}", filePath);
        }

        private string AttachSlashIfNotPresent(string relativeToPath)
        {
            if (!relativeToPath.EndsWith("\\"))
                relativeToPath += "\\";

            return relativeToPath;
        }

        private bool IsAbsolutePathValid(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            bool isValidUri = Uri.TryCreate(path, UriKind.Absolute, out Uri pathUri);
            return isValidUri && pathUri != null && pathUri.IsLoopback;
        }

        private string SplitFullPathWithRelativePath(string fullPath, string relativeToPath)
        {
            string[] relativePath = fullPath.Split(new[] {relativeToPath}, StringSplitOptions.RemoveEmptyEntries);
            return relativePath.First();
        }
    }
}