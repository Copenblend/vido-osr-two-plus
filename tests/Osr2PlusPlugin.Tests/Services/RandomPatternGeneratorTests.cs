using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class RandomPatternGeneratorTests
{
    private const int Seed = 42; // Deterministic for reproducible tests
    private const double Tolerance = 1e-9;

    // --- Constructor & Defaults ---

    [Fact]
    public void Constructor_DefaultRange_0To100()
    {
        var gen = new RandomPatternGenerator(seed: Seed);
        var pos = gen.GetPosition(0);
        Assert.InRange(pos, 0, 100);
    }

    [Fact]
    public void Constructor_CustomRange_RespectsMinMax()
    {
        var gen = new RandomPatternGenerator(min: 20, max: 80, seed: Seed);
        // Generate many positions to verify all within range
        for (double p = 0; p < 10000; p += 1)
        {
            var pos = gen.GetPosition(p);
            Assert.True(pos >= 20 - Tolerance && pos <= 80 + Tolerance,
                $"Position {pos} at progress={p} outside [20, 80]");
        }
    }

    // --- GetPosition ---

    [Fact]
    public void GetPosition_FirstCall_InitializesAndReturnsWithinRange()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        var pos = gen.GetPosition(0);
        Assert.InRange(pos, 0, 100);
    }

    [Fact]
    public void GetPosition_OutputAlwaysWithinRange()
    {
        var gen = new RandomPatternGenerator(10, 90, Seed);
        for (double p = 0; p < 5000; p += 0.5)
        {
            var pos = gen.GetPosition(p);
            Assert.True(pos >= 10 - Tolerance && pos <= 90 + Tolerance,
                $"Position {pos} at progress={p} outside [10, 90]");
        }
    }

    [Fact]
    public void GetPosition_SmoothTransitions_NoLargeJumps()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        double prevPos = gen.GetPosition(0);
        double maxJump = 0;
        const double step = 0.5;

        for (double p = step; p < 3000; p += step)
        {
            var pos = gen.GetPosition(p);
            var jump = Math.Abs(pos - prevPos);
            if (jump > maxJump) maxJump = jump;
            prevPos = pos;
        }

        // With cosine interpolation and small progress steps,
        // jumps should be much smaller than the full range.
        // At target transitions, start snaps to previous target (no visual jump).
        Assert.True(maxJump < 10,
            $"Maximum jump {maxJump:F3} exceeds threshold. Cosine interpolation should prevent large jumps.");
    }

    [Fact]
    public void GetPosition_CosineInterpolation_StartAndEndAreSmooth()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);

        // Drive far enough to trigger multiple transitions, then check
        // that at the very start of a new transition (t≈0) velocity is near-zero
        // — characteristic of cosine interpolation.
        double pos0 = gen.GetPosition(0);
        double pos1 = gen.GetPosition(0.001);

        // Near the start of a transition, cosine interpolation has near-zero derivative
        var delta = Math.Abs(pos1 - pos0);
        Assert.True(delta < 1.0,
            $"Start of transition should have near-zero velocity (delta={delta:F6})");
    }

    [Fact]
    public void GetPosition_ProgressMoves_PositionChanges()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        var pos0 = gen.GetPosition(0);

        // After sufficient progress, position should differ from initial
        bool changed = false;
        for (double p = 1; p < 1000; p += 1)
        {
            if (Math.Abs(gen.GetPosition(p) - pos0) > 0.01)
            {
                changed = true;
                break;
            }
        }
        Assert.True(changed, "Position should change as progress advances");
    }

    [Fact]
    public void GetPosition_SameProgress_ReturnsSameValue()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        gen.GetPosition(0); // Initialize
        var pos1 = gen.GetPosition(50);
        var pos2 = gen.GetPosition(50);
        Assert.Equal(pos1, pos2, Tolerance);
    }

    // --- SetRange ---

    [Fact]
    public void SetRange_UpdatesOutputRange()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        gen.GetPosition(0); // Initialize

        gen.SetRange(40, 60);
        gen.Reset(); // Reset to pick up new range

        for (double p = 0; p < 5000; p += 1)
        {
            var pos = gen.GetPosition(p);
            Assert.True(pos >= 40 - Tolerance && pos <= 60 + Tolerance,
                $"After SetRange(40,60), position {pos} at progress={p} outside [40, 60]");
        }
    }

    [Fact]
    public void SetRange_NarrowRange_OutputConstrained()
    {
        var gen = new RandomPatternGenerator(50, 50, Seed);
        // With min==max, all output should be exactly 50
        for (double p = 0; p < 500; p += 1)
        {
            var pos = gen.GetPosition(p);
            Assert.Equal(50, pos, Tolerance);
        }
    }

    // --- Reset ---

    [Fact]
    public void Reset_ReinitializesState()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        gen.GetPosition(0);
        gen.GetPosition(500); // Advance state

        gen.Reset();
        // After reset, calling GetPosition reinitializes
        var pos = gen.GetPosition(0);
        Assert.InRange(pos, 0, 100);
    }

    [Fact]
    public void Reset_DeterministicSeed_ProducesDifferentSequence()
    {
        // After reset, the RNG continues from its current state
        // (not re-seeded), so positions differ from the original run.
        var gen = new RandomPatternGenerator(0, 100, Seed);
        var firstRunPositions = new List<double>();
        for (double p = 0; p < 500; p += 50)
            firstRunPositions.Add(gen.GetPosition(p));

        gen.Reset();
        var secondRunPositions = new List<double>();
        for (double p = 0; p < 500; p += 50)
            secondRunPositions.Add(gen.GetPosition(p));

        // At least some positions should differ (RNG state is different)
        bool anyDifferent = false;
        for (int i = 0; i < firstRunPositions.Count; i++)
        {
            if (Math.Abs(firstRunPositions[i] - secondRunPositions[i]) > 0.01)
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent, "After reset, sequence should differ from first run");
    }

    // --- Transition Duration Scales with Distance ---

    [Fact]
    public void TransitionDuration_ScalesWithDistance()
    {
        // With a large range, some transitions will cover large distances
        // and should take longer. We verify by checking that the generator
        // doesn't complete all transitions instantly.
        var gen = new RandomPatternGenerator(0, 100, Seed);
        int transitionCount = 0;
        double prevPos = gen.GetPosition(0);

        // Count how many direction changes occur over fixed progress
        double lastDirection = 0;
        for (double p = 0.5; p < 2000; p += 0.5)
        {
            var pos = gen.GetPosition(p);
            var direction = pos - prevPos;
            if (lastDirection != 0 && direction != 0 &&
                Math.Sign(direction) != Math.Sign(lastDirection))
            {
                transitionCount++;
            }
            if (direction != 0) lastDirection = direction;
            prevPos = pos;
        }

        // With progress spanning 2000 units and transitions of 50-300+ duration,
        // we expect a moderate number of transitions, not thousands
        Assert.True(transitionCount < 200,
            $"Expected fewer transitions for scaled duration, got {transitionCount}");
        Assert.True(transitionCount > 2,
            $"Expected some transitions over 2000 progress units, got {transitionCount}");
    }

    // --- No Sudden Jumps on Target Change ---

    [Fact]
    public void TargetChange_NoContinuityBreak()
    {
        var gen = new RandomPatternGenerator(0, 100, Seed);
        double prevPos = gen.GetPosition(0);

        // Step through many transitions with fine granularity
        const double step = 0.1;
        for (double p = step; p < 5000; p += step)
        {
            var pos = gen.GetPosition(p);
            var jump = Math.Abs(pos - prevPos);

            // At transition boundaries, start snaps to previous target.
            // With step=0.1 and cosine interpolation, max delta per step
            // should be very small relative to the 0-100 range.
            Assert.True(jump < 5.0,
                $"Discontinuity at progress={p}: jump={jump:F3} (prev={prevPos:F3}, cur={pos:F3})");
            prevPos = pos;
        }
    }

    // --- Deterministic with Same Seed ---

    [Fact]
    public void DeterministicSeed_SameSequence()
    {
        var gen1 = new RandomPatternGenerator(0, 100, Seed);
        var gen2 = new RandomPatternGenerator(0, 100, Seed);

        for (double p = 0; p < 1000; p += 10)
        {
            Assert.Equal(gen1.GetPosition(p), gen2.GetPosition(p), Tolerance);
        }
    }
}
