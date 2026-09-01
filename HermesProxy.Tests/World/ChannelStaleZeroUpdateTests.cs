using System;
using HermesProxy;
using HermesProxy.World;
using Xunit;

namespace HermesProxy.Tests.World;

// JimsProxy (fishing recast wedge 2026-09-01): state tests for the stale channel
// zero-update guard in GameSessionData. Recasting fishing while the previous bobber
// still exists server-side (its teardown lags the channel close by ~2s) makes
// mangos-family servers tear the old bobber down on the tick AFTER the new channel
// starts; the teardown's trailing MSG_CHANNEL_UPDATE(0) lands ~100ms behind the new
// MSG_CHANNEL_START and would end the channel just opened on the modern client. The
// guard arms only when a fishing channel starts moments after the previous fishing
// channel closed, drops at most one zero-update, only within the post-start window,
// and stands down on any client-side channel-breaking action.
public class ChannelStaleZeroUpdateTests
{
    private const uint FishingArtisan = 18248;
    private const uint MindFlay = 15407;
    private const uint ChannelDurationMs = 30000;
    private const long T0 = 1_000_000;

    private static GameSessionData NewSession() => GameSessionData.CreateForTesting();

    /// <summary>First channel runs its full course and closes; player recasts inside
    /// the bobber-teardown lag. Returns the recast's start tick.</summary>
    private static long RecastIntoTeardownWindow(GameSessionData session, uint spellId = FishingArtisan)
    {
        session.OnLocalChannelStartAtTick(spellId, ChannelDurationMs, T0);
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(T0 + ChannelDurationMs)); // natural end forwards
        long recastTick = T0 + ChannelDurationMs + 1500; // old bobber still exists ~2s more
        session.OnLocalChannelStartAtTick(spellId, ChannelDurationMs, recastTick);
        return recastTick;
    }

    [Fact]
    public void RecastAfterRecentClose_DropsStaleTail_OnceOnly()
    {
        var session = NewSession();
        long recastTick = RecastIntoTeardownWindow(session);

        // The captured wedge: teardown tail lands ~100ms behind the new CHANNEL_START.
        Assert.True(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
        // The channel we protected stays open (also keeps the #244 emote guard honest).
        Assert.Equal(FishingArtisan, session.LocalChannelSpellId);
        // One-shot: only one stale tail is ever owed — a second zero-update is genuine.
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 200));
        Assert.Equal(0u, session.LocalChannelSpellId);
    }

    [Fact]
    public void FirstCastOfSession_GenuineEarlyInterrupt_Forwards()
    {
        var session = NewSession();
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, T0);

        // No previous fishing channel ⇒ no bobber can be owed a teardown ⇒ a zero-update
        // 100ms in (mob damage, movement interrupt) is genuine and must end the channel.
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(T0 + 100));
        Assert.Equal(0u, session.LocalChannelSpellId);
    }

    [Fact]
    public void RecastLongAfterClose_DoesNotArm()
    {
        var session = NewSession();
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, T0);
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(T0 + ChannelDurationMs));

        // Recast after the old bobber is provably gone — nothing stale can arrive.
        long recastTick = T0 + ChannelDurationMs + GameSessionData.FishingBobberTeardownLagMs;
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, recastTick);
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
    }

    [Fact]
    public void ReplacedWhileStillOpen_ArmsGuard()
    {
        var session = NewSession();
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, T0);

        // Recast mid-channel with no zero-update seen in between: the replaced channel
        // counts as closed now, so its late teardown tail is still owed and droppable.
        long recastTick = T0 + 15000;
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, recastTick);
        Assert.True(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
    }

    [Fact]
    public void BreakActionAfterStart_DisarmsGuard()
    {
        var session = NewSession();
        long recastTick = RecastIntoTeardownWindow(session);

        // Player clicked a GO / cancelled right after recasting — the zero-update that
        // follows is the genuine result of that action and must reach the client.
        session.RecordLocalChannelBreakAction();
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
        Assert.Equal(0u, session.LocalChannelSpellId);
    }

    [Fact]
    public void BreakActionFromPreviousCast_ClearedByStart_StillDrops()
    {
        var session = NewSession();
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, T0);
        session.RecordLocalChannelBreakAction(); // e.g. clicked the old bobber early
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(T0 + 5000));

        // The new CHANNEL_START resets the break flag — an earlier cast's action must
        // not disarm the guard for the channel that follows it.
        long recastTick = T0 + 6500;
        session.OnLocalChannelStartAtTick(FishingArtisan, ChannelDurationMs, recastTick);
        Assert.True(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
    }

    [Fact]
    public void ZeroUpdatePastWindow_Forwards()
    {
        var session = NewSession();
        long recastTick = RecastIntoTeardownWindow(session);

        // Stale tails land within one server update batch of the START; anything at or
        // past the window bound (e.g. the real end of this channel) is genuine.
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(
            recastTick + GameSessionData.StaleChannelZeroUpdateWindowMs));
        Assert.Equal(0u, session.LocalChannelSpellId);
    }

    [Fact]
    public void NonFishingChannel_NeverArms()
    {
        // A genuine early interrupt of a combat channel (damage pushback at <1.5s into
        // Mind Flay) must reach the client, or the cast bar wedges the other way.
        var session = NewSession();
        long recastTick = RecastIntoTeardownWindow(session, MindFlay);
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(recastTick + 100));
    }

    [Fact]
    public void ZeroDurationStart_ClosesChannelWindow()
    {
        // Pre-existing #244 behavior: a CHANNEL_START with no duration means no channel.
        var session = NewSession();
        session.OnLocalChannelStartAtTick(FishingArtisan, 0, T0);
        Assert.Equal(0u, session.LocalChannelSpellId);
        Assert.False(session.ConsumeLocalChannelZeroUpdateAtTick(T0 + 100));
    }

    [Theory]
    [InlineData(7620u, true)]   // Fishing (Apprentice)
    [InlineData(7731u, true)]   // Fishing (Journeyman)
    [InlineData(7732u, true)]   // Fishing (Expert)
    [InlineData(18248u, true)]  // Fishing (Artisan)
    [InlineData(33095u, true)]  // Fishing (Master) — TBC 2.4.3 backends are accepted
    [InlineData(15407u, false)] // Mind Flay
    [InlineData(0u, false)]     // not channeling
    public void IsFishingChannelSpell_CoversAllRanks(uint spellId, bool expected)
    {
        Assert.Equal(expected, GameData.IsFishingChannelSpell(spellId));
    }
}
