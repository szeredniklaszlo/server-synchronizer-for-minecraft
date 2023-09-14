namespace McSync.Server.Info
{
    public abstract class Flags
    {
        public PersistedStatus? PersistedStatus { get; set; }
        public string Owner { get; set; }
    }
}