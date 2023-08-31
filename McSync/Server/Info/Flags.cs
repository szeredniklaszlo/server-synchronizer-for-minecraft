namespace McSync.Server.Info
{
    public abstract class Flags
    {
        public PersistedStatus? LifecycleStatus { get; set; }
        public string Owner { get; set; }
    }
}