using System.IO;

namespace McSync.Files.Local
{
    public static class Paths
    {
        public const string Flags = "flags.json";
        public const string Hashes = "hashes.json";
        public const string TokenFolder = "token";
        public const string Credentials = @"Key\credentials.json";

        public static readonly string AppPath = Directory.GetCurrentDirectory();
        public static readonly string Java17Home = $@"{AppPath}\OpenJDK64\bin";

        public static readonly string ServerPath =
            //@"E:\Desktop\Folders\Jatek\Minecraft_Server\WildUpdateServer";
            Directory.GetParent(AppPath)?.FullName;
    }
}