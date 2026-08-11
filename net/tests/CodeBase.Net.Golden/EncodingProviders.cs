using System.Runtime.CompilerServices;
using System.Text;

namespace CodeBase.Net.Golden;

/// <summary>
/// Registers the legacy code pages, which is the host application's job and not the library's.
///
/// The library calls [c]Encoding.GetEncoding[/c] and uses whatever the process has registered; it
/// never registers anything itself, because that is a process-wide side effect a library has no
/// business performing on its caller's behalf (ADR-17). A test suite that reads text out of a
/// cp1251 or cp936 table is acting as the host, so it does the host's part here.
///
/// Without this, every table would still open and report its shape, and only reading text would
/// fail. That is the behaviour ADR-17 chose, and this file is what it costs.
/// </summary>
internal static class EncodingProviders
{
    /// <summary>
    /// Registers the code page provider before any test runs.
    /// </summary>
    [ModuleInitializer]
    public static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
