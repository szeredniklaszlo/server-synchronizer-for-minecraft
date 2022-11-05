using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using McSync.Utils;
using Newtonsoft.Json;
using Standart.Hash.xxHash;

namespace McSync.Files
{
    public static class LocalFileManager
    {
        private static readonly Logger Log = Logger.Instance;

        public static T LoadFromFile<T>(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static string CalculateHashOfStream(Stream stream)
        {
            stream.Position = 0;
            return xxHash64.ComputeHash(stream).ToString();
        }
        
        public static IDictionary<string, string> CalculateHashOfFilesInDirectory(string directoryPath, List<string> filterPathFragments = null)
        {
            Log.Info("Calculating hashes");

            filterPathFragments = filterPathFragments ?? new List<string>();
            var directoryInfo = new DirectoryInfo(directoryPath);
            var fileInfos = directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => filterPathFragments.All(
                        filterFragment => !file.FullName.ToLower().Contains(filterFragment.ToLower())
                    )
                )
                .ToList();
            var hashes = new ConcurrentDictionary<string, string>();

            Parallel.ForEach(fileInfos, fileInfo =>
            {
                string hash;
                using (FileStream file = fileInfo.OpenRead())
                    hash = LocalFileManager.CalculateHashOfStream(file);

                Log.Info($"Hash of {fileInfo.Name} calculated");

                var relativeFilePath = LocalFileManager.GetRelativeFilePath(fileInfo.FullName, directoryPath);
                hashes.TryAdd(relativeFilePath, hash);
            });

            Log.Info("Hashes calculated");
            return hashes;
        }
        
        public static void SaveObjectIntoFile(string filePath, object obj)
        {
            string json = JsonConvert.SerializeObject(obj);

            File.WriteAllText(filePath, json);
            Log.Local("Saved: {}", filePath);
        }
        
        public static string GetRelativeFilePath(string fullPath, string relativeToPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !IsPathValid(fullPath, UriKind.Absolute))
                throw new ArgumentException($"Invalid path: {fullPath}", nameof(fullPath));
            
            if (string.IsNullOrEmpty(relativeToPath) || !IsPathValid(relativeToPath, UriKind.Absolute))
                throw new ArgumentException($"Invalid path: {relativeToPath}", nameof(relativeToPath));

            string separator = relativeToPath;
            if (!relativeToPath.EndsWith("\\"))
            {
                separator += "\\";
            }

            if (!fullPath.StartsWith(separator))
            {
                throw new ArgumentException($"Invalid paths: relativeToPath |{relativeToPath}| is not contained by fullPath |{fullPath}|", nameof(relativeToPath));
            }
            
            var relativePath = fullPath.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            return relativePath.First();
        }
        
        private static bool IsPathValid(string path, UriKind uriKind) {
            var isValidUri = Uri.TryCreate(path, uriKind, out var pathUri);
            return (pathUri != null && isValidUri && uriKind == UriKind.Relative) ||
                   (pathUri != null && isValidUri && uriKind == UriKind.Absolute && pathUri.IsLoopback);
        }
    }
}