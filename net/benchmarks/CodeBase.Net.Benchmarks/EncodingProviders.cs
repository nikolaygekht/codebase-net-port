using System.Runtime.CompilerServices;
using System.Text;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// Registers the legacy code pages, which is the host application's job and not the library's.
///
/// The library calls [c]Encoding.GetEncoding[/c] and uses whatever the process has registered; it
/// never registers anything itself, because that is a process-wide side effect a library has no
/// business performing on its caller's behalf (ADR-17). PERF10K names no code page and an unmarked
/// table is read as cp437, which no runtime supplies unregistered, so a benchmark that reads a
/// character field out of one is acting as the host and does the host's part here.
///
/// A module initializer and not a line in [c]Main[/c], deliberately: BenchmarkDotNet runs each
/// benchmark in a child process whose entry point is its own generated code, so this assembly's
/// [c]Main[/c] never executes there. A module initializer runs in any process that loads the
/// assembly, which is the only hook both processes share.
/// </summary>
internal static class EncodingProviders
{
    /// <summary>
    /// Registers the code page provider before any benchmark runs.
    /// </summary>
    [ModuleInitializer]
    public static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
