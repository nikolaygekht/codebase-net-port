using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using CodeBase.Net.Tests;
using Xunit.v3;

[assembly: RunTimeWatchdog]

namespace CodeBase.Net.Tests;

/// <summary>
/// Ends the test process, naming the test, when a test runs longer than it is allowed to.
///
/// A test that loops forever is the likeliest way for a cursor to fail, and nothing else stops one: a
/// test timeout reports the test but leaves its thread spinning and the process alive, and a runner
/// that gives up on the process does not reliably reach it -- a mutation run on Windows left one
/// orphaned test process per looping mutant, each spinning a core (ADR-70). Ending the process from
/// inside is the one exit that always works. It is killed outright rather than failed fast: the
/// message is the same, and a mutation run does not leave an error report behind per looping mutant.
///
/// Run directly, the process ends a second in. Under [c]dotnet test[/c] the xUnit adapter waits a
/// further 60 seconds before reporting the crash; that wait is the adapter's and cannot be configured.
///
/// The limit is a second unless the test carries [clink=CodeBase.Net.Tests.MaxRunTimeAttribute]MaxRunTime[/clink];
/// the slowest test without one takes about a third of that. Only the test method is timed, not its
/// class's constructor. The watchdog stands down while a debugger is attached, so a breakpoint does
/// not end the session.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class RunTimeWatchdogAttribute : BeforeAfterTestAttribute
{
    /// <summary>How long a test without a limit of its own may run.</summary>
    private static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(1);

    /// <summary>The armed timer of each test that is running; tests run in parallel.</summary>
    private readonly ConcurrentDictionary<IXunitTest, Timer> running = new();

    /// <summary>
    /// Arms the timer for a test that is about to run.
    /// </summary>
    /// <param name="methodUnderTest">The test method.</param>
    /// <param name="test">The test.</param>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        TimeSpan limit = methodUnderTest.GetCustomAttribute<MaxRunTimeAttribute>()?.Limit ?? DefaultLimit;
        string name = test.TestDisplayName;

        running[test] = new Timer(_ => Expire(name, limit), null, limit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Disarms the timer of a test that has finished.
    /// </summary>
    /// <param name="methodUnderTest">The test method.</param>
    /// <param name="test">The test.</param>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (running.TryRemove(test, out Timer? timer))
            timer.Dispose();
    }

    /// <summary>
    /// Ends the process because a test has run out of time.
    /// </summary>
    /// <param name="name">The test's display name.</param>
    /// <param name="limit">How long it was allowed.</param>
    private static void Expire(string name, TimeSpan limit)
    {
        if (Debugger.IsAttached)
            return;

        string message = $"Test ran longer than its limit of {limit.TotalSeconds:0.###} s and the process is ended: {name}";
        Console.Error.WriteLine(message);
        Console.Error.Flush();
        Process.GetCurrentProcess().Kill();
    }
}

/// <summary>
/// Allows a test longer than the watchdog's default second.
///
/// For a test that is slow by design -- one that times something, and has to run long enough for the
/// timing to mean anything. A test that is merely slow is better made quicker.
/// </summary>
/// <param name="seconds">How long the test may run.</param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class MaxRunTimeAttribute(int seconds) : Attribute
{
    /// <summary>
    /// Gets how long the test may run.
    /// </summary>
    public TimeSpan Limit { get; } = TimeSpan.FromSeconds(seconds);
}
