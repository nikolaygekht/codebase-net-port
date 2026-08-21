using AwesomeAssertions;
using CodeBase.Net.IO;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The pool's promises: one buffer per slot, no allocation while serving, and a claimed block that
/// cannot be pulled out from under its reader.
///
/// Layer one. Nothing here reads a file, and nothing needs to: the pool's whole job is which bytes
/// belong to whom, and the interesting failures — a claim that is retired anyway, a buffer handed to
/// two readers, a claim that never comes back — are all reachable with no I/O at all.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class BlockPoolTests
{
    [Fact]
    public void APoolHandsOutBlocksOfItsOwnSize()
    {
        BlockPool pool = new(blockSize: 512, capacity: 4);

        pool.BlockSize.Should().Be(512);
        pool.Capacity.Should().Be(4);
        pool.Held.Should().Be(0);

        pool.TryClaim(BlockPriority.IndexLow, out int slot, out int retired).Should().BeTrue();

        retired.Should().Be(-1, "there was a free slot, so nothing was given up");
        pool.At(slot).Length.Should().Be(512);
        pool.Held.Should().Be(1);
    }

    [Fact]
    public void EverySlotHasItsOwnBuffer()
    {
        // Two readers on two slots must not be looking at the same bytes, which is the one way a pool
        // can corrupt a read without any eviction being involved.
        BlockPool pool = new(blockSize: 8, capacity: 3);
        List<int> slots = [];

        for (int i = 0; i < 3; i++)
        {
            pool.TryClaim(BlockPriority.DataLow, out int slot, out _).Should().BeTrue();
            pool.At(slot).Writable.Fill((byte)(i + 1));
            slots.Add(slot);
        }

        for (int i = 0; i < 3; i++)
            pool.At(slots[i]).Span.ToArray().Should().AllSatisfy(b => b.Should().Be((byte)(i + 1)));
    }

    [Fact]
    public void ABlockIsTheSameBytesEveryTimeItIsAskedFor()
    {
        BlockPool pool = new(blockSize: 4, capacity: 1);
        pool.TryClaim(BlockPriority.Other, out int slot, out _).Should().BeTrue();

        pool.At(slot).Writable.Fill(0xAB);

        pool.At(slot).Span.ToArray().Should().Equal((byte)0xAB, 0xAB, 0xAB, 0xAB);
    }

    [Fact]
    public void ClaimingWhenFullRetiresSomethingAndSaysWhat()
    {
        BlockPool pool = new(blockSize: 8, capacity: 2);

        pool.TryClaim(BlockPriority.DataLow, out int first, out _).Should().BeTrue();
        pool.TryClaim(BlockPriority.DataLow, out int second, out _).Should().BeTrue();
        pool.Release(first);
        pool.Release(second);

        pool.TryClaim(BlockPriority.DataLow, out int third, out int retired).Should().BeTrue();

        retired.Should().Be(first, "the least recently used unclaimed block");
        third.Should().Be(first, "and its slot is the one reused");
        pool.Held.Should().Be(2, "still two blocks, one of them new");
    }

    [Fact]
    public void AClaimedBlockIsNotRetiredToMakeRoom()
    {
        // The lifetime rule, at the level a caller sees it.
        BlockPool pool = new(blockSize: 8, capacity: 2);

        pool.TryClaim(BlockPriority.DataLow, out int held, out _).Should().BeTrue();
        pool.TryClaim(BlockPriority.DataLow, out int spare, out _).Should().BeTrue();
        pool.Release(spare);

        pool.TryClaim(BlockPriority.DataLow, out _, out int retired).Should().BeTrue();

        retired.Should().Be(spare, "the released one");
        retired.Should().NotBe(held, "a claimed block must never be given up");
    }

    [Fact]
    public void WithEverythingClaimedTheClaimIsRefusedRatherThanServedWrong()
    {
        BlockPool pool = new(blockSize: 8, capacity: 2);

        pool.TryClaim(BlockPriority.DataLow, out _, out _).Should().BeTrue();
        pool.TryClaim(BlockPriority.DataLow, out _, out _).Should().BeTrue();

        pool.TryClaim(BlockPriority.DataLow, out _, out _).Should().BeFalse(
            "the caller reads uncached rather than overwriting a block being read");
    }

    [Fact]
    public void AnIndexBlockSurvivesAScanOverDataBlocks()
    {
        // The five-list split as a pool user sees it: a scan cannot cost the index tree its place.
        BlockPool pool = new(blockSize: 8, capacity: 4);

        pool.TryClaim(BlockPriority.IndexHigh, out int root, out _).Should().BeTrue();
        pool.Release(root);

        for (int read = 0; read < 20; read++)
        {
            pool.TryClaim(BlockPriority.DataLow, out int slot, out int retired).Should().BeTrue();
            retired.Should().NotBe(root, $"read {read} retired the index block");
            pool.Release(slot);
        }

        pool.Held.Should().Be(4);
    }

    [Fact]
    public void ATouchedBlockOutlivesAnUntouchedOne()
    {
        BlockPool pool = new(blockSize: 8, capacity: 2);

        pool.TryClaim(BlockPriority.DataLow, out int older, out _).Should().BeTrue();
        pool.TryClaim(BlockPriority.DataLow, out int newer, out _).Should().BeTrue();
        pool.Release(older);
        pool.Release(newer);

        pool.Touch(older);

        pool.TryClaim(BlockPriority.DataLow, out _, out int retired).Should().BeTrue();
        retired.Should().Be(newer, "older was just used, so newer is now the oldest");
    }

    [Fact]
    public void ASecondClaimOnAHeldBlockHasToBeReleasedTwice()
    {
        BlockPool pool = new(blockSize: 8, capacity: 1);

        pool.TryClaim(BlockPriority.IndexLow, out int slot, out _).Should().BeTrue();
        pool.Claim(slot);
        pool.Release(slot);

        pool.TryClaim(BlockPriority.IndexLow, out _, out _).Should().BeFalse("one claim is still out");

        pool.Release(slot);
        pool.TryClaim(BlockPriority.IndexLow, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void ReleasingWhatNobodyClaimedIsRefused()
    {
        BlockPool pool = new(blockSize: 8, capacity: 1);
        pool.TryClaim(BlockPriority.Other, out int slot, out _).Should().BeTrue();
        pool.Release(slot);

        Action act = () => pool.Release(slot);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DroppingAHeldBlockFreesItsSlot()
    {
        BlockPool pool = new(blockSize: 8, capacity: 1);
        pool.TryClaim(BlockPriority.Other, out int slot, out _).Should().BeTrue();
        pool.Release(slot);

        pool.Drop(slot);

        pool.Held.Should().Be(0);
        pool.TryClaim(BlockPriority.Other, out _, out int retired).Should().BeTrue();
        retired.Should().Be(-1, "the slot was free, not retired");
    }

    [Fact]
    public void DroppingAClaimedBlockIsRefused()
    {
        BlockPool pool = new(blockSize: 8, capacity: 1);
        pool.TryClaim(BlockPriority.Other, out int slot, out _).Should().BeTrue();

        Action act = () => pool.Drop(slot);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ServingBlocksAllocatesNothing()
    {
        // What the pool exists for. Everything is allocated when the pool is built; claiming, reading,
        // touching and releasing must cost no bytes at all, or the descent goes back to where it was.
        BlockPool pool = new(blockSize: 512, capacity: 8);

        for (int warm = 0; warm < 100; warm++)
            Cycle(pool);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
            Cycle(pool);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(512, 0)]
    [InlineData(-1, 4)]
    public void ANonsensicalPoolIsRefused(int blockSize, int capacity)
    {
        Action act = () => new BlockPool(blockSize, capacity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AResidencyOfADifferentSizeIsRefused()
    {
        Action act = () => new BlockPool(512, 4, new BlockResidency(8));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheResidencyRuleIsReplaceable()
    {
        // The seam the faithful policy will arrive through: the pool asks, it does not decide.
        AlwaysSlotZero rule = new(2);
        BlockPool pool = new(blockSize: 8, capacity: 2, rule);

        pool.TryClaim(BlockPriority.DataLow, out int first, out _).Should().BeTrue();
        pool.TryClaim(BlockPriority.DataLow, out _, out _).Should().BeTrue();
        pool.Release(first);

        pool.TryClaim(BlockPriority.DataLow, out int third, out int retired).Should().BeTrue();

        retired.Should().Be(0, "the rule was asked and it said zero");
        third.Should().Be(0);
    }

    private static void Cycle(BlockPool pool)
    {
        if (!pool.TryClaim(BlockPriority.IndexLow, out int slot, out _))
            return;

        pool.At(slot).Writable[0] = 1;
        pool.Touch(slot);
        pool.Release(slot);
    }

    /// <summary>
    /// A residency that always names slot zero, to prove the pool takes its answer rather than its own.
    /// </summary>
    private sealed class AlwaysSlotZero(int capacity) : IBlockResidency
    {
        private readonly BlockResidency real = new(capacity);

        public int Capacity => real.Capacity;

        public int Resident => real.Resident;

        public void Admit(int slot, BlockPriority priority) => real.Admit(slot, priority);

        public void Touch(int slot) => real.Touch(slot);

        public void Pin(int slot) => real.Pin(slot);

        public void Unpin(int slot) => real.Unpin(slot);

        public void Forget(int slot) => real.Forget(slot);

        public bool TryFindFree(out int slot) => real.TryFindFree(out slot);

        public bool TryChooseVictim(out int slot)
        {
            slot = 0;
            return real.Resident > 0;
        }
    }
}
