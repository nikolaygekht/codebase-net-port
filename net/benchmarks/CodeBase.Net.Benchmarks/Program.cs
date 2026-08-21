using BenchmarkDotNet.Running;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// Entry point.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs every benchmark, or the ones named by the arguments.
    /// </summary>
    /// <param name="args">
    /// BenchmarkDotNet arguments, for example [c]--filter *SeekCharacter*[/c]. With none, the whole
    /// read-path set runs, so that the obvious command prints the whole baseline.
    /// </param>
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            BenchmarkRunner.Run<ReadPathBenchmarks>();
            BenchmarkRunner.Run<DescentBenchmarks>();
            BenchmarkRunner.Run<RamDescentBenchmarks>();
            BenchmarkRunner.Run<KeyComparisonBenchmarks>();
        }
        else
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
