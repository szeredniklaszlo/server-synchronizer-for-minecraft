namespace McSync.Server.Info
{
    public enum RuntimeStatus
    {
        Stopped,
        StoppedCorruptly,
        Outdated,
        Updating,
        AlreadyUpdatingElsewhere,
        UpToDate,
        Starting,
        Running,
        AlreadyRunningElsewhere,
        Uploading,
        UploadedCorruptly,
        Synchronized
    }
}