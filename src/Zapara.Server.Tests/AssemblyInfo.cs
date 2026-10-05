using Xunit;

// Independent hosts share the process-wide eight-permit password budget and one
// local PostgreSQL instance. Explicit concurrency inside each test stays intact.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
