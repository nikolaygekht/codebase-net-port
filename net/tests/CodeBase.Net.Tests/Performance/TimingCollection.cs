using Xunit;

namespace CodeBase.Net.Tests.Performance;

/// <summary>
/// The tests that read a clock, run by themselves.
///
/// xUnit runs a collection that disables parallelization after every parallel collection has finished,
/// alone. A timed assertion measured while the rest of the suite competes for the processor measures
/// the competition: the block cache's Go-then-Skip gate fell to 1.91 times in a Release full run that
/// clears it four times over on its own (ADR-57).
/// </summary>
[CollectionDefinition(nameof(TimingCollection), DisableParallelization = true)]
public sealed class TimingCollection;
