using Content.Shared._Colony.Mood;
using Content.Shared._Colony.Overseer;
using Content.Shared.Buckle.Components;
using Content.Shared.Follower;
using Content.Shared.Ghost.Components;

namespace Content.Server._Colony.Overseer;

/// <summary>
/// Overseer powers (jump to a colonist and follow them), plus a stuck detector that writes to server.log
/// when a colonist has not moved for a while, so we can find out what they are stuck on.
/// </summary>
public sealed class ColonyOverseerSystem : EntitySystem
{
    [Dependency] private FollowerSystem _follower = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private const float CheckEvery = 5f;
    private const float StuckAfter = 25f;

    private float _timer;
    private readonly Dictionary<EntityUid, (System.Numerics.Vector2 Pos, float Since)> _seen = new();
    private readonly Dictionary<EntityUid, float> _reported = new();
    private float _clock;

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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _clock += frameTime;
        _timer += frameTime;
        if (_timer < CheckEvery)
            return;

        _timer = 0f;
        var query = EntityQueryEnumerator<ColonyMoodComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var pos = _xform.GetWorldPosition(xform);
            if (!_seen.TryGetValue(uid, out var last) || (pos - last.Pos).Length() > 0.4f)
            {
                _seen[uid] = (pos, _clock);
                continue;
            }

            // Sitting in a chair eating is fine.
            if (TryComp(uid, out BuckleComponent? buckle) && buckle.Buckled)
                continue;

            if (_clock - last.Since < StuckAfter)
                continue;

            if (_reported.TryGetValue(uid, out var when) && _clock - when < 60f)
                continue;

            _reported[uid] = _clock;
            var near = new List<string>();
            foreach (var other in _lookup.GetEntitiesInRange(xform.Coordinates, 1.3f))
            {
                if (other == uid)
                    continue;

                near.Add(MetaData(other).EntityPrototype?.ID ?? Name(other));
                if (near.Count >= 8)
                    break;
            }

            Log.Info($"{ToPrettyString(uid)} has not moved for {(int) (_clock - last.Since)}s at {pos.X:0.0},{pos.Y:0.0}. Nearby: {string.Join(", ", near)}");
        }
    }
}
