using System;
using HermesProxy;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using Xunit;

namespace HermesProxy.Tests.World;

// JimsProxy (observed-bow retract): quiescence-sweep that lowers an observed shooter's latched
// ranged-aim (Auto Shot 75 / wand Shoot 5019) by firing SMSG_CANCEL_AUTO_REPEAT once they stop.
// The sweep clock is injected so the 3s quiet window is exercised deterministically with no waits.
public class ObservedAutoRepeatTests
{
    static ObservedAutoRepeatTests()
    {
        if (global::Framework.Settings.ClientBuild == ClientVersionBuild.Zero)
            global::Framework.Settings.ClientBuild = ClientVersionBuild.V1_14_2_42597;
    }

    private const long QuietMs = 4000;
    private static GameSessionData NewSession() => GameSessionData.CreateForTesting();
    private static WowGuid128 Shooter(ulong counter) =>
        WowGuid128.Create(HighGuidType703.Creature, 0, 12345, counter);

    [Fact]
    public void Sweep_FreshSession_ReturnsNothing()
    {
        var session = NewSession();
        Assert.Empty(session.SweepObservedAutoRepeat(0));
    }

    [Fact]
    public void Sweep_WithinQuietWindow_DoesNotExpire()
    {
        var session = NewSession();
        var hunter = Shooter(1);
        session.NoteObservedAutoRepeatActivityForTest(hunter, nowMs: 10_000);

        // Only 2.5s have passed — still actively shooting, keep the aim up.
        Assert.Empty(session.SweepObservedAutoRepeat(nowMs: 10_000 + 2_500));
    }

    [Fact]
    public void Sweep_PastQuietWindow_ExpiresAndReturnsShooter()
    {
        var session = NewSession();
        var hunter = Shooter(1);
        session.NoteObservedAutoRepeatActivityForTest(hunter, nowMs: 10_000);

        var expired = session.SweepObservedAutoRepeat(nowMs: 10_000 + QuietMs);

        Assert.Single(expired);
        Assert.Equal(hunter, expired[0]);
    }

    [Fact]
    public void Sweep_AfterExpiry_IsIdempotent()
    {
        var session = NewSession();
        var hunter = Shooter(1);
        session.NoteObservedAutoRepeatActivityForTest(hunter, nowMs: 10_000);

        Assert.Single(session.SweepObservedAutoRepeat(nowMs: 100_000));
        // Already removed — a second sweep must not re-fire a cancel for the same shooter.
        Assert.Empty(session.SweepObservedAutoRepeat(nowMs: 200_000));
    }

    [Fact]
    public void Sweep_PerShooter_OnlyExpiresTheQuietOne()
    {
        var session = NewSession();
        var stopped = Shooter(1);
        var stillShooting = Shooter(2);
        session.NoteObservedAutoRepeatActivityForTest(stopped, nowMs: 10_000);
        session.NoteObservedAutoRepeatActivityForTest(stillShooting, nowMs: 12_500);

        // At t=14_100: 'stopped' has been quiet 4.1s (expire); 'stillShooting' only 1.6s (keep).
        var expired = session.SweepObservedAutoRepeat(nowMs: 14_100);

        Assert.Single(expired);
        Assert.Equal(stopped, expired[0]);
        // The active shooter is still tracked, so a later quiet sweep still catches it.
        Assert.Contains(stillShooting, session.SweepObservedAutoRepeat(nowMs: 20_000));
    }

    [Fact]
    public void Note_RefreshesTimestamp_KeepsAimAliveAcrossSlowCadence()
    {
        var session = NewSession();
        var hunter = Shooter(1);
        session.NoteObservedAutoRepeatActivityForTest(hunter, nowMs: 10_000);
        // A second shot lands at t=13_500 (3.5s later) — still one series, refresh the clock.
        session.NoteObservedAutoRepeatActivityForTest(hunter, nowMs: 13_500);

        // t=14_000 is a full quiet-window after the FIRST shot (would expire without the refresh)
        // but only 0.5s after the second — the refresh keeps the aim up.
        Assert.Empty(session.SweepObservedAutoRepeat(nowMs: 14_000));
        // A full quiet-window after the SECOND shot — now the series is genuinely over.
        Assert.Single(session.SweepObservedAutoRepeat(nowMs: 13_500 + QuietMs));
    }

    [Fact]
    public void ClearAll_DropsTrackingAndUnbindsCallback()
    {
        var session = NewSession();
        session.OnObservedAutoRepeatExpire = _ => { };
        session.NoteObservedAutoRepeatActivityForTest(Shooter(1), nowMs: 10_000);

        session.ClearAllObservedAutoRepeat();

        Assert.Empty(session.SweepObservedAutoRepeat(nowMs: 100_000));
        Assert.Null(session.OnObservedAutoRepeatExpire);
    }

    [Fact]
    public void ResetInFlightCastState_ClearsObservedAutoRepeat()
    {
        var session = NewSession();
        session.NoteObservedAutoRepeatActivityForTest(Shooter(1), nowMs: 10_000);

        session.ResetInFlightCastState();

        Assert.Empty(session.SweepObservedAutoRepeat(nowMs: 100_000));
    }

    [Fact]
    public void Note_RealClockPath_RecordsAndExpires()
    {
        var session = NewSession();
        var hunter = Shooter(1);
        // Exercise the production entry point (stamps Environment.TickCount64, arms the timer).
        session.NoteObservedAutoRepeatActivity(hunter);

        // A sweep far in the future relative to "now" must expire the just-recorded shot.
        var expired = session.SweepObservedAutoRepeat(Environment.TickCount64 + 100_000);

        Assert.Contains(hunter, expired);
        session.ClearAllObservedAutoRepeat(); // dispose the real timer this test armed
    }
}
