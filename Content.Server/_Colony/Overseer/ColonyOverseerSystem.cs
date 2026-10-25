using Content.Shared._Colony.Mood;
using Content.Shared._Colony.Overseer;
using Content.Shared.Follower;
using Content.Shared.Ghost.Components;

namespace Content.Server._Colony.Overseer;

/// <summary>Overseer powers. For now: jump to a colonist and follow them around.</summary>
public sealed class ColonyOverseerSystem : EntitySystem
{
    [Dependency] private FollowerSystem _follower = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ColonyFollowRequest>(OnFollow);
    }

    private void OnFollow(ColonyFollowRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } overseer || !HasComp<GhostComponent>(overseer))
            return;

        var target = GetEntity(msg.Target);
        if (!Exists(target) || !HasComp<ColonyMoodComponent>(target))
            return;

        // Moving the overseer with the keys stops the following again.
        _follower.StartFollowingEntity(overseer, target);
    }
}
