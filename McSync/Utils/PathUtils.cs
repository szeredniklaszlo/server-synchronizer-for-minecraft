using System;
using System.Linq;

namespace McSync.Utils
{
    public class PathUtils
    {
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