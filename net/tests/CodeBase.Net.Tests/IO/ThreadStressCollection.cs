using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The tests that start threads of their own to stress shared state, run by themselves.
///
/// xUnit runs a collection that disables parallelization after every parallel collection has finished,
/// alone, as it does the timed tests (ADR-57). A stress test's own eight threads competing with the rest
/// of the suite for the processor ran past the watchdog's second (ADR-70) in a full run that it clears
/// in a third of that on its own.
/// </summary>
[CollectionDefinition(nameof(ThreadStressCollection), DisableParallelization = true)]
public sealed class ThreadStressCollection;
