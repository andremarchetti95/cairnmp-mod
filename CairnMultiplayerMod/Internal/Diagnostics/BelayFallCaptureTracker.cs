using System;
using CairnMultiplayerMod.GameApi;

namespace CairnMultiplayerMod.Internal.Diagnostics;

internal enum BelayFallCaptureStage
{
    Setup, Ready, Armed, Falling, Observing, Captured, Failed, Invalid
}

/// <summary>Measures one native catch without controlling rope, health or damage.</summary>
internal sealed class BelayFallCaptureTracker
{
    internal const double HoldSeconds = 1;
    internal const double MaximumSampleGapSeconds = .5;
    internal const double FallTimeoutSeconds = 15;
    internal const float MinimumDrop = .25f;

    internal BelayFallCaptureTracker() => Reset();

    internal BelayFallCaptureStage Stage { get; private set; }
    internal bool HasOutcome => Stage is BelayFallCaptureStage.Captured or BelayFallCaptureStage.Failed
        or BelayFallCaptureStage.Invalid;
    internal bool Active => Stage is BelayFallCaptureStage.Armed or BelayFallCaptureStage.Falling
        or BelayFallCaptureStage.Observing;
    internal bool Ready { get; private set; }
    internal int Attempt { get; private set; }
    internal string Readiness { get; private set; } = "Wait for a stable partner protection route.";
    internal string Detail { get; private set; } = "Wait for a stable partner protection route.";
    internal bool HasMeasurement { get; private set; }
    internal double CapturedAt => HasMeasurement ? _loadedAt : double.NaN;
    internal float Drop { get; private set; }
    internal float RopeLength { get; private set; }
    internal float DepartureRopeLength { get; private set; }
    internal float PreLoadRopeLength { get; private set; }
    internal float DepartureBudgetLength { get; private set; }
    internal float DepartureFixedPathLength { get; private set; }
    internal float PreLoadReleasedFinalLength { get; private set; }
    internal float Factor { get; private set; }
    internal float HpBefore { get; private set; }
    internal float HpAfter { get; private set; }
    internal float HpLoss => HpBefore - HpAfter;
    // Native hook totals since the last stable wall sample before the fall.
    // Neither establishes exclusive attribution to the catch.
    internal uint NativeDamageCalls { get; private set; }
    internal float RequestedDamage { get; private set; }
    internal float ObservedFallHpLoss { get; private set; }
    internal double RemainingSeconds { get; private set; }
    internal BelayFallObservationSample LastSample { get; private set; }

    private BelayFallObservationSample _baseline;
    private double _lastObservation, _fallAt, _loadedAt;
    private float _lastStableHeight, _startHeight;
    private bool _fallStarted, _awaitingStableRoute, _awaitingUnanchoredWall, _awaitingDepartureUnload;

    internal bool Arm(BelayFallObservationSample sample, double now, out string reason)
    {
        Reset(); // Only a new capture epoch replaces a retained result.
        if (Attempt < int.MaxValue) Attempt++;
        LastSample = sample;
        reason = SetupReason(sample, now);
        if (reason != null)
        {
            Readiness = Detail = reason;
            return false;
        }

        RefreshBaseline(sample);
        _awaitingUnanchoredWall = sample.PersonallyAnchored;
        _lastObservation = now;
        RemainingSeconds = 0;
        Stage = BelayFallCaptureStage.Armed;
        Readiness = "Ready for an observed fall.";
        Detail = "Capture armed; rope payout and protection can change before the fall.";
        reason = null;
        return true;
    }

    internal void Observe(BelayFallObservationSample sample, double now)
    {
        if (HasOutcome)
        {
            // Current preparation can ready the next capture without replacing this result.
            var reason = SetupReason(sample, now);
            Ready = reason == null;
            Readiness = reason ?? "Ready for the next catch.";
            return;
        }
        if (!Active)
        {
            LastSample = sample;
            var reason = SetupReason(sample, now);
            Ready = reason == null;
            Stage = Ready ? BelayFallCaptureStage.Ready : BelayFallCaptureStage.Setup;
            Readiness = Detail = reason ?? "Ready for the next catch.";
            return;
        }

        // Health observations remain useful when native teardown has already made
        // rope geometry unavailable. They cannot authorize an H/L measurement.
        if (sample.Rope == _baseline.Rope && HealthNumbersValid(sample) && CountersValid(sample))
            UpdateSummary(sample);

        // A known dead pawn takes priority over the context loss caused by death.
        // An empty/ended sample has no pawn identity or max HP and cannot imply death.
        if (_fallStarted && sample.Rope == _baseline.Rope && float.IsFinite(sample.MaxHp)
            && sample.MaxHp > 0 && float.IsFinite(sample.Hp) && sample.Hp <= 0)
        {
            HpAfter = sample.Hp;
            Finish(BelayFallCaptureStage.Failed, "The pawn died during the fall observation.");
            return;
        }

        if (!double.IsFinite(now) || now < 0 || now < _lastObservation)
        { Invalid("The observation clock was invalid or reversed."); return; }
        if (now - _lastObservation > MaximumSampleGapSeconds)
        { Invalid("An observation gap exceeded 0.5 seconds."); return; }
        _lastObservation = now;
        if (!sample.ContextValid)
        { Invalid("Partner gameplay, focus or pause context was lost."); return; }
        if (!_fallStarted && !sample.Falling && !sample.Suspended
            && (!sample.HasStation || !sample.RouteReady))
        {
            _awaitingStableRoute = true;
            Detail = "Catch capture is waiting for a ready native route on the wall.";
            return;
        }
        if (!sample.HasStation || !sample.RouteReady)
        { Invalid("The native station or rope route was no longer ready."); return; }
        if (!NumbersValid(sample))
        { Invalid("A native measurement was invalid or non-finite."); return; }
        if (!HasMeasurement && !ManualLengthNumbersValid(sample))
        { Invalid("The recorded-station rope-budget components were invalid."); return; }
        if (!_fallStarted && !sample.Falling && !sample.Suspended)
        {
            if (sample.Rope == 0 || sample.Station == 0 || sample.LastPiton == 0)
            { Invalid("The native route had no stable rope, station or protection identity."); return; }
            if (sample.Hp <= 0)
            { Finish(BelayFallCaptureStage.Failed, "The pawn died during the fall observation."); return; }
            // Preparation can include new protections, healing or a new native damage-counter epoch.
            // Keep the last valid wall sample as the baseline so onset-frame damage is included.
            RefreshBaseline(sample);
            _awaitingStableRoute = false;
            _awaitingUnanchoredWall = sample.PersonallyAnchored;
            Detail = sample.PersonallyAnchored ? "Catch capture is waiting for personal-anchor release and a stable wall position."
                : "Catch capture armed: slack, brake and protection can change before the fall.";
            return;
        }
        if (_awaitingStableRoute)
        { Invalid("The fall began before a stable native route was observed again."); return; }
        if (_awaitingUnanchoredWall)
        { Invalid("The fall began before an unanchored stable wall position was observed."); return; }
        if (!CountersValid(sample))
        { Invalid("Native fall-damage counters reset or became invalid."); return; }
        if (sample.Rope != _baseline.Rope || sample.Station != _baseline.Station
            || sample.LastPiton != _baseline.LastPiton
            || (!HasMeasurement && sample.Revision != _baseline.Revision))
        { Invalid("The measured rope, station, protection or pre-load revision changed."); return; }

        LastSample = sample;
        UpdateSummary(sample);
        if (sample.Hp <= 0)
        { Finish(BelayFallCaptureStage.Failed, "The pawn died during the fall observation."); return; }

        if (HasMeasurement)
        {
            RemainingSeconds = Math.Max(0, HoldSeconds - (now - _loadedAt));
            if (RemainingSeconds <= 0)
                Finish(BelayFallCaptureStage.Captured, "Native catch measurement complete.");
            return; // Swing replays and brake changes cannot capture a second H or L.
        }

        if (sample.PersonallyAnchored)
        { Invalid("A personal anchor prevented an isolated rope-fall measurement."); return; }
        if (!_fallStarted)
        {
            RemainingSeconds = 0;
            if (!sample.Falling && !sample.Suspended)
            {
                _lastStableHeight = sample.Height;
                DepartureRopeLength = sample.RopeLength;
                DepartureBudgetLength = sample.ReleasedRopeLength;
                DepartureFixedPathLength = sample.FixedPathLength;
                ObserveUnloadedLength(sample);
                return;
            }
            _fallStarted = true;
            _fallAt = now;
            _startHeight = _lastStableHeight;
            Stage = BelayFallCaptureStage.Falling;
            Detail = "Waiting for the first suspended, taut native rope load.";
        }

        RemainingSeconds = Math.Max(0, FallTimeoutSeconds - (now - _fallAt));
        if (RemainingSeconds <= 0)
        { Invalid("No native rope load was observed within 15 seconds of the fall."); return; }
        if (!sample.RopeTaut || !sample.Suspended)
        {
            _awaitingDepartureUnload = false;
            ObserveUnloadedLength(sample);
        }
        if (!sample.Suspended || !sample.RopeTaut) return;

        var drop = _startHeight - sample.Height;
        if (!float.IsFinite(drop))
        { Invalid("The first-load H/L calculation was non-finite."); return; }
        // A newly created native pivot can be taut at departure, then unload as
        // the body descends. That transient must not finish the actual fall.
        if (drop < MinimumDrop) { _awaitingDepartureUnload = true; return; }
        if (_awaitingDepartureUnload) return;
        var length = DepartureFixedPathLength + PreLoadReleasedFinalLength;
        var factor = drop / length;
        if (!float.IsFinite(length) || length <= 0 || length > 200 || !float.IsFinite(factor))
        { Invalid("The first-load H/L calculation was non-finite."); return; }
        Drop = drop;
        RopeLength = length;
        Factor = factor;
        HasMeasurement = true;

        _loadedAt = now;
        RemainingSeconds = HoldSeconds;
        Stage = BelayFallCaptureStage.Observing;
        Detail = "H and L recorded: observe native health for 1 second. Damage attribution is not established.";
    }

    internal void Cancel(string reason)
    {
        if (Active) Invalid(string.IsNullOrWhiteSpace(reason) ? "The fall observation was cancelled." : reason);
    }

    private string SetupReason(BelayFallObservationSample sample, double now)
    {
        if (!double.IsFinite(now) || now < 0) return "The observation clock is invalid.";
        if (!sample.ContextValid) return "Wait for focused, unpaused partner gameplay.";
        if (!sample.HasStation || sample.Station == 0) return "Record a valid native station.";
        if (!sample.RouteReady || sample.Rope == 0 || sample.LastPiton == 0)
            return "Wait for the native station-to-harness route to be ready.";
        if (!NumbersValid(sample)) return "Wait for finite native rope and health measurements.";
        if (!ManualLengthNumbersValid(sample))
            return "Wait for valid recorded-station rope-budget components.";
        if (sample.Hp <= 0) return "A living pawn is required.";
        if (sample.Falling || sample.Suspended) return "Return to a stable wall position before arming.";
        return null;
    }

    private static bool NumbersValid(BelayFallObservationSample sample) =>
        float.IsFinite(sample.Height) && float.IsFinite(sample.RopeLength)
        && sample.RopeLength > 0 && sample.RopeLength <= 200
        && float.IsFinite(sample.Slack) && sample.Slack >= 0
        && HealthNumbersValid(sample);

    private static bool HealthNumbersValid(BelayFallObservationSample sample) =>
        float.IsFinite(sample.Hp) && sample.Hp >= 0
        && float.IsFinite(sample.MaxHp) && sample.MaxHp > 0 && sample.Hp <= sample.MaxHp
        && float.IsFinite(sample.RequestedFallDamage) && sample.RequestedFallDamage >= 0
        && float.IsFinite(sample.ObservedFallHpLoss) && sample.ObservedFallHpLoss >= 0;

    private bool CountersValid(BelayFallObservationSample sample) =>
        sample.DamageCalls >= LastSample.DamageCalls
        && sample.RequestedFallDamage >= LastSample.RequestedFallDamage
        && sample.ObservedFallHpLoss >= LastSample.ObservedFallHpLoss
        && float.IsFinite(sample.RequestedFallDamage - _baseline.RequestedFallDamage)
        && float.IsFinite(sample.ObservedFallHpLoss - _baseline.ObservedFallHpLoss);

    private void UpdateSummary(BelayFallObservationSample sample)
    {
        HpAfter = sample.Hp;
        NativeDamageCalls = sample.DamageCalls - _baseline.DamageCalls;
        RequestedDamage = sample.RequestedFallDamage - _baseline.RequestedFallDamage;
        ObservedFallHpLoss = sample.ObservedFallHpLoss - _baseline.ObservedFallHpLoss;
    }

    private void RefreshBaseline(BelayFallObservationSample sample)
    {
        _baseline = LastSample = sample;
        _lastStableHeight = sample.Height;
        DepartureRopeLength = PreLoadRopeLength = sample.RopeLength;
        DepartureBudgetLength = sample.ReleasedRopeLength;
        DepartureFixedPathLength = sample.FixedPathLength;
        PreLoadReleasedFinalLength = sample.ReleasedFinalLength;
        HpBefore = HpAfter = sample.Hp;
        NativeDamageCalls = 0;
        RequestedDamage = ObservedFallHpLoss = 0;
    }

    private static bool ManualLengthNumbersValid(BelayFallObservationSample sample)
        => float.IsFinite(sample.ReleasedRopeLength) && sample.ReleasedRopeLength > 0 && sample.ReleasedRopeLength <= 200
            && float.IsFinite(sample.FixedPathLength) && sample.FixedPathLength >= 0 && sample.FixedPathLength <= 200
            && float.IsFinite(sample.ReleasedFinalLength) && sample.ReleasedFinalLength > 0 && sample.ReleasedFinalLength <= 200
            && float.IsFinite(sample.FixedPathLength + sample.ReleasedFinalLength)
            && sample.FixedPathLength + sample.ReleasedFinalLength <= 200;

    private void ObserveUnloadedLength(BelayFallObservationSample sample)
    {
        PreLoadRopeLength = sample.RopeLength;
        PreLoadReleasedFinalLength = sample.ReleasedFinalLength;
    }

    private void Reset()
    {
        Stage = BelayFallCaptureStage.Setup;
        Ready = HasMeasurement = _fallStarted = _awaitingStableRoute = _awaitingUnanchoredWall = _awaitingDepartureUnload = false;
        Readiness = Detail = "Wait for a stable partner protection route.";
        Drop = RopeLength = Factor = HpBefore = HpAfter = RequestedDamage = ObservedFallHpLoss = 0;
        DepartureRopeLength = PreLoadRopeLength = DepartureBudgetLength = DepartureFixedPathLength = PreLoadReleasedFinalLength = float.NaN;
        NativeDamageCalls = 0;
        RemainingSeconds = 0;
        _baseline = LastSample = default;
        _lastObservation = _fallAt = _loadedAt = 0;
        _lastStableHeight = _startHeight = 0;
    }

    private void Invalid(string detail) => Finish(BelayFallCaptureStage.Invalid, detail);

    private void Finish(BelayFallCaptureStage stage, string detail)
    {
        Stage = stage;
        Ready = false;
        RemainingSeconds = 0;
        Detail = detail;
    }
}
