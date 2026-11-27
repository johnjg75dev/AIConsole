using Xunit;

// WPF/SQLite tests share process-wide culture and temp files; keep them deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
