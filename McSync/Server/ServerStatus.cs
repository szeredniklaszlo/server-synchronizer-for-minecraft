namespace MinecraftSynchronizer
{
    internal enum ServerStatus
    {
        StoppedCorruptly,
        UploadedCorruptly,
        Outdated,
        UpToDate,
        AlreadyRunningElsewhere,
        AlreadyUpdatingElsewhere,
        Updating,
        Starting,
        Running,
        Stopped,
        Uploading,
        Synchronized
    }
}