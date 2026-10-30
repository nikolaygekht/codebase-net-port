using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// Tables of one engine opened and closed from several threads at once.
///
/// The engine keeps the list of tables it owns, so that closing it closes them. Opening and closing
/// different tables of one engine concurrently is allowed by the threading contract (ADR-41), and the
/// list is engine-shared state the library must keep whole: a torn list loses a table, which then
/// leaks its file handle, or hands the engine's own close a null.
///
/// A stress test, like the shared-cache one beside it: it cannot prove the absence of a race, and it
/// was checked by removing the lock and watching it fail.
/// </summary>
[Trait("Layer", "Component")]
public sealed class EngineTableListConcurrencyTests
{
    private const int Threads = 8;

    private const int Passes = 1000;

    [Fact]
    public void OpeningAndClosingFromManyThreads_LeavesTheListEmptyAndTheCloseClean()
    {
        CodeBaseEngine engine = new();
        string path = Corpus.PathOf("DB3TYPE.DBF");

        List<Exception> failures = [];
        Thread[] threads = [.. Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
        {
            try
            {
                for (int pass = 0; pass < Passes; pass++)
                    engine.OpenTable(path).Dispose();
            }
            catch (Exception failed)
            {
                lock (failures)
                {
                    failures.Add(failed);
                }
            }
        }))];

        Array.ForEach(threads, t => t.Start());
        Array.ForEach(threads, t => t.Join());

        failures.Should().BeEmpty();
        engine.OpenTables.Should().BeEmpty("every table opened was closed again");
        engine.Invoking(e => e.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void ClosingTheEngineWhileOthersOpen_LeavesNoTableOpen()
    {
        // Every table a thread got back must be closed once the engine is: either the engine closed
        // it, or the open was refused after the engine had already taken its list.
        CodeBaseEngine engine = new();
        string path = Corpus.PathOf("DB3TYPE.DBF");

        List<Table> received = [];
        List<Exception> failures = [];
        using Barrier start = new(Threads + 1);

        Thread[] threads = [.. Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            try
            {
                for (int pass = 0; pass < Passes; pass++)
                {
                    Table table = engine.OpenTable(path);
                    lock (received)
                    {
                        received.Add(table);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected once the engine is closed.
            }
            catch (Exception failed)
            {
                lock (failures)
                {
                    failures.Add(failed);
                }
            }
        }))];

        Array.ForEach(threads, t => t.Start());
        start.SignalAndWait(TestContext.Current.CancellationToken);
        Thread.Sleep(5);
        engine.Dispose();
        Array.ForEach(threads, t => t.Join());

        failures.Should().BeEmpty();
        received.Should().NotBeEmpty("the engine should have been closed mid-run, not before it");
        received.Should().OnlyContain(
            table => IsClosed(table), "a table the engine handed out must not outlive the engine");
    }

    private static bool IsClosed(Table table)
    {
        try
        {
            table.Top();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
