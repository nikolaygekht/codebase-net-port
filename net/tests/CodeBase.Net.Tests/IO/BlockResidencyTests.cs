using AwesomeAssertions;
using CodeBase.Net.IO;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The eviction rule, driven exhaustively and then attacked.
///
/// Layer one, and it is where the cache's only correctness risk lives: **a wrong victim is a wrong
/// record set.** Everything above this can be right and still corrupt a read if a block is retired
/// while somebody is reading out of it, and no corpus file can express that — so it is proved here,
/// with no file, no disk and no clock.
///
/// The last test is a randomised run rather than a case: fixed seed, thousands of operations, and the
/// class's invariants checked after every one. Cases prove the rules that were thought of; the random
/// run is what looks for the ones that were not.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class BlockResidencyTests
{
    [Fact]
    public void ANewResidencyIsAllFreeAndHoldsNothing()
    {
        BlockResidency residency = new(4);

        residency.Capacity.Should().Be(4);
        residency.Resident.Should().Be(0);
        residency.TryFindFree(out int slot).Should().BeTrue();
        slot.Should().Be(0);
        residency.TryChooseVictim(out _).Should().BeFalse("nothing is resident to retire");
    }

    [Fact]
    public void FindingAFreeSlotDoesNotTakeIt()
    {
        BlockResidency residency = new(2);

        residency.TryFindFree(out int first).Should().BeTrue();
        residency.TryFindFree(out int again).Should().BeTrue();

        again.Should().Be(first, "finding is a look, not a claim");
    }

    [Fact]
    public void AdmittingFillsTheSlotsAndThenThereAreNoneFree()
    {
        BlockResidency residency = new(3);

        for (int i = 0; i < 3; i++)
        {
            residency.TryFindFree(out int slot).Should().BeTrue();
            residency.Admit(slot, BlockPriority.DataLow);
        }

        residency.Resident.Should().Be(3);
        residency.TryFindFree(out _).Should().BeFalse();
    }

    [Fact]
    public void ForgettingGivesTheSlotBack()
    {
        BlockResidency residency = new(1);

        residency.Admit(0, BlockPriority.IndexLow);
        residency.TryFindFree(out _).Should().BeFalse();

        residency.Forget(0);

        residency.Resident.Should().Be(0);
        residency.TryFindFree(out int slot).Should().BeTrue();
        slot.Should().Be(0);
    }

    [Fact]
    public void APoolOfOneStillMakesProgress()
    {
        // The degenerate size, and the one where an off-by-one in the free chain shows up as a
        // deadlock rather than as a wrong answer.
        BlockResidency residency = new(1);

        for (int round = 0; round < 5; round++)
        {
            residency.Admit(0, BlockPriority.IndexLow);
            residency.TryChooseVictim(out int victim).Should().BeTrue();
            victim.Should().Be(0);
            residency.Forget(victim);
        }

        residency.Resident.Should().Be(0);
    }

    [Fact]
    public void VictimsComeOutInTheReferencesPriorityOrder()
    {
        // D4OPT.C:324-328 -- prio[0] to prio[4] are other, dbfLo, indexLo, dbfHi, indexHi, and
        // chooseBlock walks them in that order.
        BlockResidency residency = new(5);

        residency.Admit(0, BlockPriority.IndexHigh);
        residency.Admit(1, BlockPriority.DataHigh);
        residency.Admit(2, BlockPriority.IndexLow);
        residency.Admit(3, BlockPriority.DataLow);
        residency.Admit(4, BlockPriority.Other);

        List<int> order = [];
        while (residency.TryChooseVictim(out int victim))
        {
            order.Add(victim);
            residency.Forget(victim);
        }

        order.Should().Equal(4, 3, 2, 1, 0);
    }

    [Fact]
    public void ATableScanCannotEvictTheIndexTree()
    {
        // The property the whole five-list split exists for. One index block, resident and long
        // untouched; then a scan's worth of table blocks. Every table block must go first, however old
        // the index block is.
        BlockResidency residency = new(8);

        residency.Admit(0, BlockPriority.IndexHigh);
        residency.Admit(1, BlockPriority.IndexLow);

        for (int slot = 2; slot < 8; slot++)
            residency.Admit(slot, BlockPriority.DataLow);

        for (int retired = 0; retired < 6; retired++)
        {
            residency.TryChooseVictim(out int victim).Should().BeTrue();
            victim.Should().BeGreaterThanOrEqualTo(2, "an index block was retired while data was available");
            residency.Forget(victim);
        }

        residency.Resident.Should().Be(2, "both index blocks are still here");
    }

    [Fact]
    public void WithinOnePriorityTheLeastRecentlyUsedGoesFirst()
    {
        BlockResidency residency = new(3);

        residency.Admit(0, BlockPriority.IndexLow);
        residency.Admit(1, BlockPriority.IndexLow);
        residency.Admit(2, BlockPriority.IndexLow);

        residency.TryChooseVictim(out int first).Should().BeTrue();
        first.Should().Be(0, "admitted earliest and never touched");
    }

    [Fact]
    public void TouchingABlockSendsItToTheBackOfItsOwnQueue()
    {
        BlockResidency residency = new(3);

        residency.Admit(0, BlockPriority.IndexLow);
        residency.Admit(1, BlockPriority.IndexLow);
        residency.Admit(2, BlockPriority.IndexLow);

        residency.Touch(0);

        residency.TryChooseVictim(out int victim).Should().BeTrue();
        victim.Should().Be(1, "0 was just used, so 1 is now the oldest");
    }

    [Fact]
    public void TouchingTheNewestBlockChangesNothing()
    {
        BlockResidency residency = new(2);

        residency.Admit(0, BlockPriority.DataLow);
        residency.Admit(1, BlockPriority.DataLow);
        residency.Touch(1);
        residency.Touch(1);

        residency.TryChooseVictim(out int victim).Should().BeTrue();
        victim.Should().Be(0);
    }

    [Fact]
    public void APinnedBlockIsNeverTheVictimEvenWhenItIsTheOldest()
    {
        BlockResidency residency = new(2);

        residency.Admit(0, BlockPriority.DataLow);
        residency.Admit(1, BlockPriority.DataLow);
        residency.Pin(0);

        residency.TryChooseVictim(out int victim).Should().BeTrue();
        victim.Should().Be(1, "0 is pinned, so it is stepped over however old it is");
    }

    [Fact]
    public void EverythingPinnedMeansNoVictimRatherThanAWrongOne()
    {
        BlockResidency residency = new(2);

        residency.Admit(0, BlockPriority.DataLow);
        residency.Admit(1, BlockPriority.IndexHigh);
        residency.Pin(0);
        residency.Pin(1);

        residency.TryChooseVictim(out _).Should().BeFalse(
            "refusing is right; the caller reads uncached rather than overwriting a live block");
    }

    [Fact]
    public void PinsAreCountedSoTwoReadersBothHaveToLetGo()
    {
        // Two cursors on one tag can sit on the same block, and each holds it in its own right.
        BlockResidency residency = new(1);

        residency.Admit(0, BlockPriority.IndexLow);
        residency.Pin(0);
        residency.Pin(0);

        residency.Unpin(0);
        residency.TryChooseVictim(out _).Should().BeFalse("one reader is still in there");

        residency.Unpin(0);
        residency.TryChooseVictim(out int victim).Should().BeTrue();
        victim.Should().Be(0);
    }

    [Fact]
    public void APinnedBlockCannotBeForgotten()
    {
        BlockResidency residency = new(1);

        residency.Admit(0, BlockPriority.IndexLow);
        residency.Pin(0);

        Action act = () => residency.Forget(0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*pinned*");
    }

    [Fact]
    public void ReturningABlockNobodyHoldsIsRefused()
    {
        BlockResidency residency = new(1);
        residency.Admit(0, BlockPriority.IndexLow);

        Action act = () => residency.Unpin(0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not pinned*");
    }

    [Fact]
    public void AdmittingAnOccupiedSlotIsRefused()
    {
        BlockResidency residency = new(2);
        residency.Admit(0, BlockPriority.Other);

        Action act = () => residency.Admit(0, BlockPriority.Other);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already holds*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ASlotOutsideThePoolIsRefused(int slot)
    {
        BlockResidency residency = new(4);

        Action admit = () => residency.Admit(slot, BlockPriority.Other);
        Action touch = () => residency.Touch(slot);
        Action pin = () => residency.Pin(slot);

        admit.Should().Throw<ArgumentOutOfRangeException>();
        touch.Should().Throw<ArgumentOutOfRangeException>();
        pin.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TouchingOrForgettingAnEmptySlotIsRefused()
    {
        BlockResidency residency = new(2);

        Action touch = () => residency.Touch(1);
        Action forget = () => residency.Forget(1);

        touch.Should().Throw<InvalidOperationException>().WithMessage("*holds no block*");
        forget.Should().Throw<InvalidOperationException>().WithMessage("*holds no block*");
    }

    [Fact]
    public void ZeroCapacityIsRefused()
    {
        Action act = () => new BlockResidency(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheInvariantsSurviveThousandsOfRandomOperations()
    {
        // A fixed seed, so a failure is reproducible and the test is not a lottery. What this looks for
        // is not a rule anybody thought of — the cases above cover those — but an interleaving that
        // leaves the lists inconsistent: a victim that is pinned, a victim that is not resident, or a
        // slot lost from both the free chain and the queues.
        const int capacity = 16;
        Random random = new(20260821);
        BlockResidency residency = new(capacity);
        Dictionary<int, int> pinsHeld = [];
        HashSet<int> live = [];

        for (int step = 0; step < 20000; step++)
        {
            switch (random.Next(5))
            {
                case 0 when residency.TryFindFree(out int free):
                    residency.Admit(free, (BlockPriority)random.Next(5));
                    live.Add(free);
                    break;

                case 1 when live.Count > 0:
                    residency.Touch(Any(live, random));
                    break;

                case 2 when live.Count > 0:
                    int toPin = Any(live, random);
                    residency.Pin(toPin);
                    pinsHeld[toPin] = pinsHeld.GetValueOrDefault(toPin) + 1;
                    break;

                case 3 when pinsHeld.Count > 0:
                    int toUnpin = Any(pinsHeld.Keys, random);
                    residency.Unpin(toUnpin);
                    if (--pinsHeld[toUnpin] == 0)
                        pinsHeld.Remove(toUnpin);
                    break;

                case 4 when residency.TryChooseVictim(out int victim):
                    pinsHeld.Should().NotContainKey(victim, $"step {step} chose a pinned block");
                    live.Should().Contain(victim, $"step {step} chose a slot holding nothing");
                    residency.Forget(victim);
                    live.Remove(victim);
                    break;
            }

            residency.Resident.Should().Be(live.Count, $"step {step}");

            // A victim must always exist while something unpinned is resident, or a slot has been lost
            // from the queues and the pool would starve without saying so.
            bool anyUnpinned = live.Any(slot => !pinsHeld.ContainsKey(slot));
            residency.TryChooseVictim(out _).Should().Be(anyUnpinned, $"step {step}");
        }
    }

    private static int Any(IEnumerable<int> slots, Random random)
    {
        List<int> all = [.. slots];
        return all[random.Next(all.Count)];
    }
}
