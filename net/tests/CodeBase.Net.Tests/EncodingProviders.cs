using System.Runtime.CompilerServices;
using System.Text;

namespace CodeBase.Net.Tests;

/// <summary>
/// Registers the legacy code pages, which is the host application's job and not the library's.
///
/// The case maps are built from each table's encoding, and most of the code pages a table can be
/// marked with are not in .NET until a provider is registered. The library never registers one
/// itself (ADR-17), so the test assembly does the host's part, as the golden one does.
/// </summary>
internal static class EncodingProviders
{
    /// <summary>
    /// Registers the code page provider before any test runs.
    /// </summary>
    [ModuleInitializer]
    public static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
