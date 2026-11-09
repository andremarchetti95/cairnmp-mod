using System;
using CairnMultiplayerMod.GameApi;
using CairnMultiplayerMod.Internal.Diagnostics;
using Xunit;

namespace CairnMultiplayerMod.Tests;

public sealed class BelayFallCaptureTrackerTests
{
    private static BelayFallObservationSample Wall => new(true, true, true,
        false, false, false, false, 11, 22, 33, 4, 30, 10, 100, 100, 7, 12, 8, 1.5f, 10, 2, 8);
    private static BelayFallObservationSample ManualWall => Wall;

    [Fact]
    public void AShortFallPassesOnlyAfterOneContinuouslyObservedSecond()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        trial.Observe(Loaded(), .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
        Assert.Equal(10, trial.RopeLength);
        Assert.Equal(.2f, trial.Factor);
        Assert.Equal(1, trial.RemainingSeconds);
        Assert.Equal(.5, trial.CapturedAt);
        trial.Observe(Loaded(), 1);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(.5, trial.RemainingSeconds);
        trial.Observe(Loaded(), 1.499999);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.False(trial.HasOutcome);
        Assert.True(trial.RemainingSeconds > 0);
        trial.Observe(Loaded(), 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(0, trial.RemainingSeconds);
    }


    [Fact]
    public void FallHeightUsesTheLastWallSampleBeforeTheOnsetSample()
    {
        var trial = Armed();
        trial.Observe(Wall with { Height = 31 }, .1);
        trial.Observe(Falling() with { Height = 30.6f }, .2);
        trial.Observe(Loaded() with { Height = 28.5f }, .3);
        Assert.Equal(2.5f, trial.Drop);
        Assert.Equal(.25f, trial.Factor);
    }


    [Fact]
    public void ARecordedManualStationUsesTheLastUnloadedPaidOutBudget()
    {
        var trial = new BelayFallCaptureTracker();
        var departure = ManualWall with { RopeLength = 4 };
        Assert.True(trial.Arm(departure, 0, out _));
        trial.Observe(departure with { Falling = true, Height = 29, RopeLength = 3, ReleasedRopeLength = 12, ReleasedFinalLength = 10 }, .25);
        var caught = departure with { Falling = true, Suspended = true, RopeTaut = true,
            Height = 28, RopeLength = 2, ReleasedRopeLength = 20, ReleasedFinalLength = 18 };
        trial.Observe(caught, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);

        Assert.Equal(4, trial.DepartureRopeLength);
        Assert.Equal(3, trial.PreLoadRopeLength);
        Assert.Equal(10, trial.DepartureBudgetLength);
        Assert.Equal(2, trial.DepartureFixedPathLength);
        Assert.Equal(10, trial.PreLoadReleasedFinalLength);
        Assert.Equal(12, trial.RopeLength);
        Assert.Equal(2f / 12, trial.Factor);
        ObserveThrough(trial, caught with { ReleasedRopeLength = 30, ReleasedFinalLength = 28, RopeLength = 1 }, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(12, trial.RopeLength);
        Assert.Equal(4, trial.DepartureRopeLength);
        Assert.Equal(3, trial.PreLoadRopeLength);
    }


    [Fact]
    public void ManualReferenceFreezesTheLastWallFixedPathInsteadOfItsShrinkingFallReading()
    {
        var trial = new BelayFallCaptureTracker();
        var initial = ManualWall with { FixedPathLength = 4, ReleasedFinalLength = 2, ReleasedRopeLength = 6 };
        Assert.True(trial.Arm(initial, 0, out _));
        var lastWall = initial with { FixedPathLength = 5, ReleasedRopeLength = 7 };
        trial.Observe(lastWall, .1);
        trial.Observe(lastWall with { Falling = true, Height = 29, FixedPathLength = 3,
            ReleasedRopeLength = 5, RopeLength = 6 }, .25);
        trial.Observe(lastWall with { Falling = true, Suspended = true, RopeTaut = true,
            Height = 28, FixedPathLength = 2, ReleasedRopeLength = 4, RopeLength = 5 }, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(5, trial.DepartureFixedPathLength);
        Assert.Equal(2, trial.PreLoadReleasedFinalLength);
        Assert.Equal(7, trial.DepartureBudgetLength);
        Assert.Equal(7, trial.RopeLength);
        Assert.Equal(2f / 7, trial.Factor);
    }


    [Theory]
    [InlineData(11f)]
    [InlineData(6f)]
    public void PayoutOrTakeInUpdatesOnlyTheFinalBudgetBeforeLoad(float lastReleasedFinal)
    {
        var trial = new BelayFallCaptureTracker();
        var departure = ManualWall;
        Assert.True(trial.Arm(departure, 0, out _));
        trial.Observe(departure with { Falling = true, Height = 29, FixedPathLength = 1,
            ReleasedFinalLength = 11, ReleasedRopeLength = 12 }, .1);
        trial.Observe(departure with { Falling = true, Height = 28.5f, FixedPathLength = .5f,
            ReleasedFinalLength = lastReleasedFinal, ReleasedRopeLength = .5f + lastReleasedFinal }, .3);
        var caught = departure with { Falling = true, Suspended = true, RopeTaut = true, Height = 28,
            FixedPathLength = 0, ReleasedFinalLength = 20, ReleasedRopeLength = 20 };
        trial.Observe(caught, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.DepartureFixedPathLength);
        Assert.Equal(lastReleasedFinal, trial.PreLoadReleasedFinalLength);
        Assert.Equal(2 + lastReleasedFinal, trial.RopeLength);
        Assert.Equal(2 / (2 + lastReleasedFinal), trial.Factor);
        ObserveThrough(trial, caught with { FixedPathLength = 20, ReleasedFinalLength = 30,
            ReleasedRopeLength = 50 }, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(2, trial.DepartureFixedPathLength);
        Assert.Equal(lastReleasedFinal, trial.PreLoadReleasedFinalLength);
        Assert.Equal(2 + lastReleasedFinal, trial.RopeLength);
    }


    [Theory]
    [InlineData(float.NaN, 8f, 10f)]
    [InlineData(float.PositiveInfinity, 8f, 10f)]
    [InlineData(-1f, 8f, 7f)]
    [InlineData(2f, float.NaN, 10f)]
    [InlineData(2f, 0f, 2f)]
    [InlineData(2f, -1f, 1f)]
    [InlineData(199f, 2f, 200f)]
    [InlineData(201f, 1f, 200f)]
    [InlineData(2f, 8f, float.NaN)]
    [InlineData(2f, 8f, 0f)]
    [InlineData(2f, 8f, 201f)]
    public void InvalidManualBudgetComponentsCannotSilentlyFallBackToANativeEstimate(
        float fixedPath, float releasedFinal, float rawTotal)
    {
        var trial = new BelayFallCaptureTracker();
        var invalid = ManualWall with { FixedPathLength = fixedPath,
            ReleasedFinalLength = releasedFinal, ReleasedRopeLength = rawTotal };
        Assert.False(trial.Arm(invalid, 0, out var reason));
        Assert.Contains("rope-budget components", reason);
        Assert.False(trial.Active);
        Assert.True(trial.Arm(ManualWall, 1, out _));
        trial.Observe(ManualWall with { Falling = true, Height = 29 }, 1.25);
        trial.Observe(invalid with { Falling = true, Suspended = true, RopeTaut = true,
            Height = 28, Hp = 90, DamageCalls = 8, RequestedFallDamage = 22, ObservedFallHpLoss = 18 }, 1.5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(90, trial.HpAfter);
    }


    [Fact]
    public void AnOverlongFrozenFixedPlusFinalReferenceCannotProduceAMeasurement()
    {
        var trial = new BelayFallCaptureTracker();
        var departure = ManualWall with { FixedPathLength = 5, ReleasedFinalLength = 3, ReleasedRopeLength = 8 };
        Assert.True(trial.Arm(departure, 0, out _));
        trial.Observe(departure with { Falling = true, Height = 29, FixedPathLength = 1,
            ReleasedFinalLength = 198, ReleasedRopeLength = 199 }, .25);
        trial.Observe(departure with { Suspended = true, RopeTaut = true, Height = 28,
            FixedPathLength = 1, ReleasedFinalLength = 198, ReleasedRopeLength = 199 }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(5, trial.DepartureFixedPathLength);
        Assert.Equal(198, trial.PreLoadReleasedFinalLength);
    }


    [Fact]
    public void UnloadedSwingAndAnUnsuspendedTautRopeCannotCaptureTheLoad()
    {
        var trial = Armed();
        trial.Observe(Loaded() with { RopeTaut = false }, .2);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        Assert.False(trial.HasMeasurement);
        trial.Observe(Falling() with { RopeTaut = true }, .4);
        Assert.False(trial.HasMeasurement);
        trial.Observe(Loaded() with { RopeTaut = false }, .6);
        Assert.False(trial.HasMeasurement);
        trial.Observe(Loaded(), .8);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
    }


    [Fact]
    public void SuspensionCanRevealAFallWhenTheNativeFallingFlagWasNotSampled()
    {
        var trial = Armed();
        trial.Observe(Loaded() with { Falling = false }, .25);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.Drop);
    }


    [Fact]
    public void BrakeAndSwingReplayCannotReplaceTheFirstMeasurementOrRestartTheHold()
    {
        var trial = Armed();
        trial.Observe(Loaded(), .5);
        trial.Observe(Wall with { Height = 18, RopeLength = 25, Revision = 5 }, .75);
        trial.Observe(Loaded() with { Height = 14, RopeLength = 30, Revision = 6 }, 1);
        trial.Observe(Wall with { Height = 30, Revision = 7 }, 1.25);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        trial.Observe(Loaded() with { Height = 10, RopeLength = 40, Revision = 8 }, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(2, trial.Drop);
        Assert.Equal(10, trial.RopeLength);
        Assert.Equal(.2f, trial.Factor);
    }


    [Fact]
    public void HealthDifferenceRequestedDamageAndHookObservedLossRemainDistinct()
    {
        var trial = Armed();
        var caught = Loaded() with { Hp = 92, DamageCalls = 9, RequestedFallDamage = 18,
            ObservedFallHpLoss = 11 };
        trial.Observe(caught, .5);
        ObserveThrough(trial, caught with { Hp = 94 }, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(100, trial.HpBefore);
        Assert.Equal(94, trial.HpAfter);
        Assert.Equal(6, trial.HpLoss);
        Assert.Equal(2u, trial.NativeDamageCalls);
        Assert.Equal(6, trial.RequestedDamage);
        Assert.Equal(3, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void NativeDamageCountersUseTheLastWallBaselineBeforeTheCatch()
    {
        var trial = Armed();
        var beforeFall = Wall with { DamageCalls = 8, RequestedFallDamage = 14, ObservedFallHpLoss = 9 };
        trial.Observe(beforeFall, .2);
        trial.Observe(beforeFall with { Falling = true, Height = 29 }, .4);
        trial.Observe(beforeFall with { Suspended = true, RopeTaut = true, Height = 28,
            DamageCalls = 9, RequestedFallDamage = 17, ObservedFallHpLoss = 10 }, .6);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(3, trial.RequestedDamage);
        Assert.Equal(1, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void AMeasuredCatchCanPassWithoutDamageAndDoesNotValidateADamageCurve()
    {
        var trial = Passed();
        Assert.Equal(0u, trial.NativeDamageCalls);
        Assert.Equal(0, trial.HpLoss);
        Assert.Equal(0, trial.RequestedDamage);
        Assert.Equal(0, trial.ObservedFallHpLoss);
    }


    [Theory]
    [InlineData(0f)]
    [InlineData(.2f)]
    [InlineData(-1f)]
    public void AnInsufficientOrUpwardFirstTensionWaitsForTheFallRatherThanEndingIt(float drop)
    {
        var trial = Armed();
        var small = Loaded() with { Height = Wall.Height - drop };
        trial.Observe(small, .25);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        Assert.False(trial.HasOutcome);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(0, trial.RopeLength);
        ObserveThrough(trial, small, .75, 14.75);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        trial.Observe(small, 15.25);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(0, trial.Drop);
        Assert.Contains("15 seconds", trial.Detail);
    }


    [Theory]
    [InlineData(.002f, 3.55f, 2.092f, 1.688f)]
    [InlineData(-.001f, 4.285f, 3.373f, 2.936f)]
    public void ADepartureTensionCanUnloadThenRecordTheLaterMeasurableNativeCatch(
        float initialDrop, float actualDrop, float initialLength, float unloadedLength)
    {
        var trial = new BelayFallCaptureTracker();
        var start = Wall with { RopeLength = initialLength, FixedPathLength = 0,
            ReleasedFinalLength = initialLength, ReleasedRopeLength = initialLength };
        Assert.True(trial.Arm(start, 0, out _));
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - initialDrop }, .1);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        Assert.False(trial.HasMeasurement);
        trial.Observe(start with { Falling = true, Height = Wall.Height - 1, RopeLength = unloadedLength }, .3);
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - actualDrop, RopeLength = unloadedLength + 1 }, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(Wall.Height - (Wall.Height - actualDrop), trial.Drop);
        Assert.Equal(initialLength, trial.RopeLength);
        Assert.Equal(unloadedLength, trial.PreLoadRopeLength);
        Assert.Equal(trial.Drop / initialLength, trial.Factor);
        ObserveThrough(trial, Loaded() with { RopeLength = unloadedLength + 1 }, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
    }


    [Fact]
    public void NativeDamageAfterATinyDepartureLoadIsNotLostBeforeTheLaterCatch()
    {
        var trial = new BelayFallCaptureTracker();
        var before = Wall with { Hp = 94.328f, DamageCalls = 0, RequestedFallDamage = 0,
            ObservedFallHpLoss = 0, RopeLength = 3.891f, FixedPathLength = 0,
            ReleasedFinalLength = 3.891f, ReleasedRopeLength = 3.891f };
        Assert.True(trial.Arm(before, 0, out _));
        trial.Observe(before with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - .014f }, .1);
        Assert.False(trial.HasOutcome);
        trial.Observe(before with { Falling = true, Suspended = true, Height = Wall.Height - 3 }, .3);
        var caught = before with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - 6.724f, Hp = 76.744f, DamageCalls = 1,
            RequestedFallDamage = 17.584f, ObservedFallHpLoss = 17.584f };
        trial.Observe(caught, .5);
        ObserveThrough(trial, caught, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(17.584f, trial.RequestedDamage);
        Assert.Equal(17.584f, trial.ObservedFallHpLoss);
        Assert.Equal(94.328f - 76.744f, trial.HpLoss);
        Assert.Equal(3.891f, trial.RopeLength);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEarlyTautPivotMustUnloadBeforeTheLaterMeasurableCatchCanBeRecorded(bool loseSuspension)
    {
        var trial = new BelayFallCaptureTracker();
        var start = Wall with { RopeLength = 3.373f, FixedPathLength = 0,
            ReleasedFinalLength = 3.373f, ReleasedRopeLength = 3.373f };
        Assert.True(trial.Arm(start, 0, out _));
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height + .001f }, .1);
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - .246f, RopeLength = 3.315f }, .2);
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - .4f, RopeLength = 3.2f }, .3);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.False(trial.HasOutcome);
        trial.Observe(start with { Falling = true, Height = Wall.Height - 2, RopeLength = 2.936f,
            Suspended = !loseSuspension, RopeTaut = loseSuspension }, .4);
        trial.Observe(start with { Falling = true, Suspended = true, RopeTaut = true,
            Height = Wall.Height - 4.285f, RopeLength = 3.048f }, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(Wall.Height - (Wall.Height - 4.285f), trial.Drop);
        Assert.Equal(3.373f, trial.RopeLength);
        Assert.Equal(2.936f, trial.PreLoadRopeLength);
        Assert.Equal(trial.Drop / 3.373f, trial.Factor);
    }


    [Fact]
    public void AnUninterruptedDepartureTensionCannotBecomeAMeasurementByCrossingTheMinimumDrop()
    {
        var trial = Armed();
        trial.Observe(Loaded() with { Height = Wall.Height - .002f }, .25);
        var stillTaut = Loaded() with { Hp = 90, DamageCalls = 8, RequestedFallDamage = 22,
            ObservedFallHpLoss = 18 };
        ObserveThrough(trial, stillTaut, .75, 14.75);
        Assert.Equal(BelayFallCaptureStage.Falling, trial.Stage);
        Assert.False(trial.HasMeasurement);
        trial.Observe(stillTaut, 15.25);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(0, trial.Drop);
        Assert.Equal(90, trial.HpAfter);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(10, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void ANewAttemptDoesNotRetainThePreviousDepartureUnloadRequirement()
    {
        var trial = Armed();
        trial.Observe(Loaded() with { Height = Wall.Height - .002f }, .25);
        trial.Cancel("Start a fresh fall");
        Assert.True(trial.Arm(Wall, 1, out _));
        trial.Observe(Loaded(), 1.25);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.Drop);
    }


    [Fact]
    public void CancellationIsAnOutcomeOnlyForAnActiveCaptureAndNeverErasesAnOutcome()
    {
        var trial = new BelayFallCaptureTracker();
        trial.Cancel("Nothing running");
        Assert.False(trial.HasOutcome);
        Assert.True(trial.Arm(Wall, 0, out _));
        trial.Cancel("User cancelled");
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.Equal("User cancelled", trial.Detail);
        trial.Cancel("Teardown");
        Assert.Equal("User cancelled", trial.Detail);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ACorruptNativeMeasurementCannotPassAnAlreadyCapturedCapture(int field)
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        var invalid = field switch
        {
            0 => Loaded() with { Height = float.NaN },
            1 => Loaded() with { RopeLength = float.NaN },
            2 => Loaded() with { Hp = float.NaN },
            3 => Loaded() with { MaxHp = float.PositiveInfinity },
            4 => Loaded() with { RequestedFallDamage = float.NaN },
            _ => Loaded() with { ObservedFallHpLoss = float.PositiveInfinity }
        };
        trial.Observe(invalid, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
    }


    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void InvalidArmingTimeDoesNotCreateAnActiveAttempt(double now)
    {
        var trial = new BelayFallCaptureTracker();
        Assert.False(trial.Arm(Wall, now, out _));
        Assert.False(trial.Active);
    }


    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(.1d)]
    [InlineData(.750001d)]
    public void AClockErrorOrGapCannotCountTowardTheHealthObservation(double now)
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        trial.Observe(Loaded(), now);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
    }


    [Fact]
    public void DeathDuringTheHealthWindowRetainsTheMeasurementEvenIfContextIsLost()
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        trial.Observe(Loaded() with { Hp = 0, ContextValid = false, DamageCalls = 8, RequestedFallDamage = 112, ObservedFallHpLoss = 108 }, .5);
        Assert.Equal(BelayFallCaptureStage.Failed, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
        Assert.Equal(0, trial.HpAfter);
        Assert.Equal(100, trial.HpLoss);
        Assert.Equal(1u, trial.NativeDamageCalls);
        trial.Observe(default, 100);
        Assert.Equal(BelayFallCaptureStage.Failed, trial.Stage);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FatalHealthMetadataSurvivesUnavailableGeometryWithOrWithoutAPreviousCatch(bool captured)
    {
        var trial = new BelayFallCaptureTracker();
        var before = Wall with { Hp = 9.845f, DamageCalls = 3, RequestedFallDamage = 76.326f,
            ObservedFallHpLoss = 76.326f };
        Assert.True(trial.Arm(before, 0, out _));
        trial.Observe(before with { Falling = true, Suspended = captured, RopeTaut = captured, Height = 28 }, .25);
        trial.Observe(before with { ContextValid = false, RouteReady = false,
            Height = float.NaN, RopeLength = float.NaN, Slack = float.NaN, Hp = 0, DamageCalls = 4,
            RequestedFallDamage = 76.326f + 20.961f, ObservedFallHpLoss = 76.326f + 9.845f }, .5);
        Assert.Equal(BelayFallCaptureStage.Failed, trial.Stage);
        Assert.Equal(captured, trial.HasMeasurement);
        Assert.Equal(captured ? 2 : 0, trial.Drop);
        Assert.Equal(9.845f, trial.HpBefore);
        Assert.Equal(0, trial.HpAfter);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal((76.326f + 20.961f) - 76.326f, trial.RequestedDamage);
        Assert.Equal((76.326f + 9.845f) - 76.326f, trial.ObservedFallHpLoss);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidHealthMetadataIsRetainedWhenContextOrRouteLossMakesTheFallInconclusive(bool loseContext)
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Falling() with { ContextValid = !loseContext, RouteReady = false,
            Height = float.NaN, RopeLength = float.NaN, Hp = 90, DamageCalls = 8,
            RequestedFallDamage = 22, ObservedFallHpLoss = 18 }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.Equal(90, trial.HpAfter);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(10, trial.RequestedDamage);
        Assert.Equal(10, trial.ObservedFallHpLoss);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FatalInvalidCountersRetainTheirLastValidSummaryInsteadOfInventingDeltas(bool nonFinite)
    {
        var trial = Armed();
        var valid = Falling() with { Hp = 90, DamageCalls = 8, RequestedFallDamage = 22, ObservedFallHpLoss = 18 };
        trial.Observe(valid, .25);
        trial.Observe(valid with { ContextValid = false, Height = float.NaN, RopeLength = float.NaN, Hp = 0,
            DamageCalls = nonFinite ? 9u : 0u,
            RequestedFallDamage = nonFinite ? float.NaN : 22 }, .5);
        Assert.Equal(BelayFallCaptureStage.Failed, trial.Stage);
        Assert.Equal(0, trial.HpAfter);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(10, trial.RequestedDamage);
        Assert.Equal(10, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void AForeignRopeCannotSupplyHealthMetadataToTheFallBeingObserved()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Falling() with { Rope = 999, ContextValid = false, Height = float.NaN,
            RopeLength = float.NaN, Hp = 0, DamageCalls = 10, RequestedFallDamage = 100,
            ObservedFallHpLoss = 100 }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.Equal(100, trial.HpAfter);
        Assert.Equal(0u, trial.NativeDamageCalls);
        Assert.Equal(0, trial.RequestedDamage);
        Assert.Equal(0, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void DeathBeforeTheFirstLoadCannotInventAMeasurement()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Falling() with { Hp = 0, ContextValid = false }, .5);
        Assert.Equal(BelayFallCaptureStage.Failed, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void AnEmptyEndSampleIsInconclusiveRatherThanAFalseDeath()
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        trial.Observe(default, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.Equal(100, trial.HpAfter);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void APreLoadRouteOrIdentityChangeInvalidatesTheCapture(int identity)
    {
        var trial = Armed();
        trial.Observe(Falling(), .2);
        var changed = identity switch
        {
            0 => Loaded() with { Rope = 12 },
            1 => Loaded() with { Station = 23 },
            2 => Loaded() with { LastPiton = 34 },
            _ => Loaded() with { Revision = 5 }
        };
        trial.Observe(changed, .4);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ANewRopeStationOrProtectionCannotFinishThePreviousHealthObservation(int identity)
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        var changed = identity switch
        {
            0 => Loaded() with { Rope = 12 },
            1 => Loaded() with { Station = 23 },
            _ => Loaded() with { LastPiton = 34 }
        };
        trial.Observe(changed, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeCounterResetsDuringTheHealthWindowCannotProduceAPass(int counter)
    {
        var trial = Armed();
        var increased = Loaded() with { DamageCalls = 9, RequestedFallDamage = 16, ObservedFallHpLoss = 10 };
        trial.Observe(increased, .25);
        var reset = counter switch
        {
            0 => increased with { DamageCalls = 8 },
            1 => increased with { RequestedFallDamage = 15 },
            _ => increased with { ObservedFallHpLoss = 9 }
        };
        trial.Observe(reset, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.Equal(2u, trial.NativeDamageCalls);
        Assert.Equal(4, trial.RequestedDamage);
        Assert.Equal(2, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void ANewExplicitAttemptUsesFreshCounterAndHealthBaselines()
    {
        var trial = Passed();
        var next = Wall with { Rope = 12, Station = 23, LastPiton = 34, Revision = 1,
            Hp = 80, DamageCalls = 0, RequestedFallDamage = 0, ObservedFallHpLoss = 0 };
        Assert.True(trial.Arm(next, 20, out _));
        trial.Observe(next with { Suspended = true, RopeTaut = true, Height = 29,
            Hp = 78, DamageCalls = 1, RequestedFallDamage = 3, ObservedFallHpLoss = 2 }, 20.5);
        Assert.Equal(2, trial.Attempt);
        Assert.Equal(1, trial.Drop);
        Assert.Equal(80, trial.HpBefore);
        Assert.Equal(2, trial.HpLoss);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(3, trial.RequestedDamage);
        Assert.Equal(2, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void ACounterWrapIsInconclusiveRatherThanAnEnormousDamageDelta()
    {
        var trial = new BelayFallCaptureTracker();
        Assert.True(trial.Arm(Wall with { DamageCalls = uint.MaxValue }, 0, out _));
        trial.Observe(Loaded() with { DamageCalls = 0 }, .25);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.Equal(0u, trial.NativeDamageCalls);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void FiniteInputsWhoseHeightDifferenceOverflowsCannotProduceAnInfiniteFactor()
    {
        var trial = new BelayFallCaptureTracker();
        Assert.True(trial.Arm(Wall with { Height = float.MaxValue }, 0, out _));
        trial.Observe(Loaded() with { Height = -float.MaxValue }, .25);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
        Assert.True(float.IsFinite(trial.Drop));
        Assert.True(float.IsFinite(trial.Factor));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostStationOrRouteDuringTheHoldRetainsAnInconclusiveMeasurement(bool station)
    {
        var trial = Armed();
        trial.Observe(Loaded(), .25);
        trial.Observe(station ? Loaded() with { HasStation = false }
            : Loaded() with { RouteReady = false }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.True(trial.HasMeasurement);
        Assert.Equal(2, trial.Drop);
    }


    [Fact]
    public void AOpenBrakeNativeCatchIncludingAnEndKnotCanRecordTheMeasurement()
    {
        var trial = Armed();
        var caught = Loaded() with { Slack = 0 };
        trial.Observe(caught, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.Drop);
        ObserveThrough(trial, caught, 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
    }


    [Fact]
    public void PayoutAndBrakeChangesWaitForActualNativeTensionThenFreezeTheFirstCatch()
    {
        var trial = Armed();
        trial.Observe(Falling() with { RopeLength = 11, Slack = 2 }, .25);
        trial.Observe(Loaded() with { RopeTaut = false, RopeLength = 12 }, .5);
        Assert.False(trial.HasMeasurement);
        trial.Observe(Loaded() with { Height = 27, RopeLength = 15 }, .75);
        Assert.Equal(3, trial.Drop);
        Assert.Equal(10, trial.RopeLength);
        Assert.Equal(12, trial.PreLoadRopeLength);
        Assert.Equal(.3f, trial.Factor);
        trial.Observe(Loaded() with { Height = 20, RopeLength = 25 }, 1);
        Assert.Equal(3, trial.Drop);
        Assert.Equal(10, trial.RopeLength);
        Assert.Equal(12, trial.PreLoadRopeLength);
    }


    [Fact]
    public void WallPreparationCanChangeTheWholeRouteAndStartsHealthCountersAtTheLastWallSample()
    {
        var trial = Armed();
        var next = Wall with { Rope = 41, Station = 42, LastPiton = 43, Revision = 20, Height = 42,
            RopeLength = 12, Hp = 80, DamageCalls = 0, RequestedFallDamage = 0, ObservedFallHpLoss = 0,
            Slack = 0, FixedPathLength = 4, ReleasedRopeLength = 12 };
        trial.Observe(next, .25);
        Assert.Equal(BelayFallCaptureStage.Armed, trial.Stage);
        Assert.Equal(80, trial.HpBefore);
        Assert.Equal(0u, trial.NativeDamageCalls);
        var caught = next with { Falling = true, Suspended = true, RopeTaut = true, Height = 40,
            Hp = 78, DamageCalls = 1, RequestedFallDamage = 3, ObservedFallHpLoss = 2 };
        trial.Observe(caught, .5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.Drop);
        Assert.Equal(12, trial.RopeLength);
        Assert.Equal(80, trial.HpBefore);
        Assert.Equal(78, trial.HpAfter);
        Assert.Equal(1u, trial.NativeDamageCalls);
        Assert.Equal(3, trial.RequestedDamage);
        Assert.Equal(2, trial.ObservedFallHpLoss);
    }


    [Fact]
    public void CaptureWaitsForAProtectionUpdateAndUsesItsNewStableRoute()
    {
        var trial = Armed();
        trial.Observe(Wall with { RouteReady = false, RopeLength = float.NaN }, .25);
        Assert.Equal(BelayFallCaptureStage.Armed, trial.Stage);
        Assert.False(trial.HasMeasurement);
        var next = Wall with { LastPiton = 44, Revision = 8, Height = 34, RopeLength = 14,
            FixedPathLength = 6, ReleasedRopeLength = 14 };
        trial.Observe(next, .5);
        trial.Observe(next with { Falling = true, Suspended = true, RopeTaut = true, Height = 31 }, .75);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(3, trial.Drop);
        Assert.Equal(14, trial.RopeLength);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFallCannotReuseTheOldBaselineAfterAnUnobservedProtectionUpdate(bool routeReady)
    {
        var trial = Armed();
        trial.Observe(Wall with { RouteReady = false, RopeLength = float.NaN }, .25);
        trial.Observe(Loaded() with { RouteReady = routeReady }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void CaptureArmingWhileAnchoredWaitsForReleaseAndAStableWallBeforeMeasuring()
    {
        var trial = new BelayFallCaptureTracker();
        Assert.True(trial.Arm(Wall with { PersonallyAnchored = true }, 0, out _));
        trial.Observe(Wall with { PersonallyAnchored = true, Height = 32 }, .25);
        Assert.Equal(BelayFallCaptureStage.Armed, trial.Stage);
        trial.Observe(Wall with { Height = 33 }, .5);
        trial.Observe(Loaded() with { Height = 30 }, .75);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(3, trial.Drop);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureCannotCaptureAnAnchoredCatchOrAnImmediateReleaseWithoutAWallSample(bool stillAnchored)
    {
        var trial = new BelayFallCaptureTracker();
        Assert.True(trial.Arm(Wall with { PersonallyAnchored = true }, 0, out _));
        trial.Observe(Loaded() with { PersonallyAnchored = stillAnchored }, .25);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void ANewPersonalAnchorDuringAFallCannotBecomeACatchMeasurement()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Loaded() with { PersonallyAnchored = true }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void WallPreparationHasNoFortyFiveSecondDeadline()
    {
        var trial = Armed();
        ObserveThrough(trial, Wall, .5, 60);
        Assert.Equal(BelayFallCaptureStage.Armed, trial.Stage);
        Assert.False(trial.HasOutcome);
        trial.Observe(Loaded(), 60.5);
        Assert.Equal(BelayFallCaptureStage.Observing, trial.Stage);
        Assert.Equal(2, trial.Drop);
    }


    [Fact]
    public void AProtectionChangeAfterAFallBeginsCannotSilentlyRebaselineIt()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Loaded() with { LastPiton = 99, Revision = 8 }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }


    [Fact]
    public void ADamageCounterResetAfterTheFallStartsIsStillInvalid()
    {
        var trial = Armed();
        trial.Observe(Falling(), .25);
        trial.Observe(Loaded() with { DamageCalls = 0 }, .5);
        Assert.Equal(BelayFallCaptureStage.Invalid, trial.Stage);
        Assert.False(trial.HasMeasurement);
    }



    private static BelayFallCaptureTracker Armed()
    {
        var trial = new BelayFallCaptureTracker();
        Assert.True(trial.Arm(Wall, 0, out var reason), reason);
        return trial;
    }

    private static BelayFallCaptureTracker Passed()
    {
        var trial = Armed();
        trial.Observe(Loaded(), .5);
        ObserveThrough(trial, Loaded(), 1, 1.5);
        Assert.Equal(BelayFallCaptureStage.Captured, trial.Stage);
        return trial;
    }

    private static BelayFallObservationSample Falling() => Wall with { Falling = true, Height = 29.75f };

    private static BelayFallObservationSample Loaded() => Wall with
        { Falling = true, Suspended = true, RopeTaut = true, Height = 28 };

    private static void ObserveThrough(BelayFallCaptureTracker trial, BelayFallObservationSample sample,
        double first, double last)
    {
        for (var now = first; now <= last; now += .5) trial.Observe(sample, now);
    }
}
