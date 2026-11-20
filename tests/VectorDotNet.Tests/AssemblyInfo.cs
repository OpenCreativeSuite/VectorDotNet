using Xunit;

// The editor, the font catalogue and the pointer modifier snapshot are
// process-wide. Serial execution keeps component tests deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
