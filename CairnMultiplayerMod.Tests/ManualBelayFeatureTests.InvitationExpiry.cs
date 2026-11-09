using System.Linq;
using CairnMultiplayerMod.Features;
using Xunit;

namespace CairnMultiplayerMod.Tests;

public sealed partial class ManualBelayFeatureTests
{
    [Fact]
    public void LocallyExpiredConsentCannotBeRestoredByOldOfferAndStartEvents()
    {
        using var world = new World();
        world.Nodes[1].Game.ChatImpl.Run("offer 2");
        var offered = world.Events.Last(e => Decode(e).Phase == ManualBelayPhase.Offered);
        world.DeferredEventRecipient = 2;
        world.DeferredPhase = ManualBelayPhase.Started;
        world.Nodes[2].Game.ChatImpl.Run("accept");
        var started = world.Events.Last(e => Decode(e).Phase == ManualBelayPhase.Started);
        world.DroppedEventRecipient = 2;
        world.Time += 26;
        world.Tick();
        var begins = world.Nodes[2].Game.BelayImpl.BeginCalls;
        world.Nodes[2].Runtime.ApplyEvent(offered);
        world.Nodes[2].Runtime.ApplyEvent(started);
        world.Tick();
        Assert.False(world.Nodes[2].Game.BelayImpl.Active);
        Assert.Equal(begins, world.Nodes[2].Game.BelayImpl.BeginCalls);
    }
}
