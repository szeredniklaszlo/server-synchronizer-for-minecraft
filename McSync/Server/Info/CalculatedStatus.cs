namespace McSync.Server.Info
{
    public enum CalculatedStatus
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