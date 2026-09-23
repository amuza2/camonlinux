using Xunit;

// Some tests mutate process-wide state (the XDG_DATA_HOME environment variable used
// by TrashService) or write to a shared temporary directory, so the suite runs
// sequentially. It is small enough that this costs nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
