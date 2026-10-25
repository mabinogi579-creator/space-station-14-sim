using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server._Colony.Mood;
using Content.Shared._Colony.Mood;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Colony.Ai;

internal static class ColonyRest
{
    public static readonly ProtoId<SatiationTypePrototype> Rest = "Rest";
    public static readonly ProtoId<SatiationTypePrototype> Recreation = "Recreation";

    public static readonly ProtoId<ThoughtPrototype> SleptInBed = "ThoughtSleptInBed";
    public static readonly ProtoId<ThoughtPrototype> SleptOnFloor = "ThoughtSleptOnFloor";
    public static readonly ProtoId<ThoughtPrototype> HadFun = "ThoughtHadFun";
    public static readonly ProtoId<ThoughtPrototype> Chatted = "ThoughtChatted";

    public static void Thought(IEntityManager ent, EntityUid who, ProtoId<ThoughtPrototype> thought)
    {
        ent.System<MoodSystem>().AddThought(new Entity<ColonyMoodComponent?>(who, null), thought);
    }

    /// <summary>
    /// Adds a need's value gradually. Called every frame, but only touches the need about twice a second.
    /// </summary>
    public static void Fill(IEntityManager ent, EntityUid who, ProtoId<SatiationTypePrototype> need, ref float pending,
        float perSecond, float frameTime)
    {
        pending += perSecond * frameTime;
        if (pending < 0.5f)
            return;

        if (ent.TryGetComponent(who, out SatiationComponent? sat))
            ent.System<SatiationSystem>().ModifyValue(new Entity<SatiationComponent>(who, sat), need, pending);

        pending = 0f;
    }

    public static float? Value(IEntityManager ent, EntityUid who, ProtoId<SatiationTypePrototype> need)
    {
        if (!ent.TryGetComponent(who, out SatiationComponent? sat))
            return null;

        return ent.System<SatiationSystem>().GetValueOrNull(new Entity<SatiationComponent>(who, sat), need);
    }
}

/// <summary>
/// Finds somewhere to sleep: a free bed if there is one, otherwise the colonist just drops where they are.
/// Sets "TargetCoordinates", "TargetPathfind" and "Seat" (the bed, or invalid).
/// </summary>
public sealed partial class PickRestSpotOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;

    private static readonly string[] BedWords = { "Bed", "Cot", "Mattress", "Bunk" };

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coords, _ent))
            return (false, null);

        _buckle.TryUnbuckle(owner, owner, popup: false);

        var straps = new HashSet<Entity<StrapComponent>>();
        _lookup.GetEntitiesInRange(coords, 80f, straps);

        var beds = new List<Entity<StrapComponent>>();
        foreach (var strap in straps)
        {
            if (!strap.Comp.Enabled || strap.Comp.BuckledEntities.Count > 0 || ColonyAiLog.ClaimedByOther(strap.Owner, owner, 180_000))
                continue;

            var id = _ent.GetComponent<MetaDataComponent>(strap.Owner).EntityPrototype?.ID ?? string.Empty;
            if (BedWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase)))
                beds.Add(strap);
        }

        var kinds = string.Join(", ", straps.Select(s => _ent.GetComponent<MetaDataComponent>(s.Owner).EntityPrototype?.ID ?? "?").Distinct().Take(12));
        ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} sees {straps.Count} straps nearby, {beds.Count} free beds. Kinds: {kinds}");
        _random.Shuffle(beds);

        var tried = 0;
        foreach (var bed in beds)
        {
            if (tried++ >= 4)
                break;

            var spot = _ent.GetComponent<TransformComponent>(bed.Owner).Coordinates;
            var path = await _pathfinding.GetPath(owner, coords, spot, 1f, cancelToken, _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            ColonyAiLog.Claim(bed.Owner, owner);
            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} heading to a bed to sleep");
            return (true, new Dictionary<string, object>
            {
                { "TargetCoordinates", spot },
                { "TargetPathfind", path },
                { "Seat", bed.Owner },
            });
        }

        ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} found no free bed, sleeping on the spot");
        return (true, new Dictionary<string, object>
        {
            { "TargetCoordinates", coords },
            { "Seat", EntityUid.Invalid },
        });
    }
}

/// <summary>
/// Sleeps: climbs into the bed (if any) and slowly refills Rest. A bed restores it much faster than the floor.
/// Keeps going until rested or five minutes have passed.
/// </summary>
public sealed partial class ColonistSleepOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private sealed class SleepState
    {
        public float Slept;
        public float Pending;
        public float NextSnore = 6f;
        public bool Bed;
        public long Last;
    }

    private static readonly Dictionary<EntityUid, SleepState> States = new();

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var now = Environment.TickCount64;
        var who = ColonyAiLog.NameOf(_ent, owner);

        if (!States.TryGetValue(owner, out var s) || now - s.Last > 2000)
        {
            s = new SleepState();
            States[owner] = s;

            if (blackboard.TryGetValue<EntityUid>("Seat", out var seat, _ent) && seat.IsValid() && _ent.EntityExists(seat))
                s.Bed = _buckle.TryBuckle(owner, owner, seat, popup: false);

            // No bed: lie down flat using the game's own lying-down state.
            if (!s.Bed)
                _standing.Down(owner, playSound: false, dropHeldItems: false);

            ColonyAiLog.Action(_ent, owner, s.Bed ? $"{who} lies down in a bed to sleep" : $"{who} dozes off on the floor");
        }

        s.Last = now;
        s.Slept += frameTime;
        ColonyRest.Fill(_ent, owner, ColonyRest.Rest, ref s.Pending, s.Bed ? 0.6f : 0.25f, frameTime);

        s.NextSnore -= frameTime;
        if (s.NextSnore <= 0f)
        {
            s.NextSnore = 12f;
            ColonyAiLog.Sound(_ent, owner, "*zzz*");
        }

        var value = ColonyRest.Value(_ent, owner, ColonyRest.Rest);
        if (value == null)
        {
            States.Remove(owner);
            return HTNOperatorStatus.Failed;
        }

        if (value.Value >= 95f || s.Slept > 300f)
        {
            ColonyRest.Thought(_ent, owner, s.Bed ? ColonyRest.SleptInBed : ColonyRest.SleptOnFloor);
            ColonyAiLog.Action(_ent, owner, $"{who} wakes up");
            _standing.Stand(owner);
            States.Remove(owner);
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Continuing;
    }
}

/// <summary>
/// Finds something fun to do: an arcade machine, jukebox or similar if one is free, otherwise another colonist to
/// chat with. Sets "PlayTarget", "TargetCoordinates" and "TargetPathfind".
/// </summary>
public sealed partial class PickRecreationOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly string[] FunWords =
    {
        "Arcade", "Jukebox", "Television", "Piano", "Dartboard", "Gramophone", "Guitar", "Violin", "Saxophone", "Trumpet",
    };

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coords, _ent))
            return (false, null);

        var nearby = new HashSet<Entity<TransformComponent>>();
        _lookup.GetEntitiesInRange(coords, 25f, nearby);

        var fun = new List<EntityUid>();
        foreach (var e in nearby)
        {
            if (!e.Comp.Anchored || ColonyAiLog.ClaimedByOther(e.Owner, owner, 60_000))
                continue;

            var id = _ent.GetComponent<MetaDataComponent>(e.Owner).EntityPrototype?.ID ?? string.Empty;
            if (FunWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase)))
                fun.Add(e.Owner);
        }

        _random.Shuffle(fun);
        foreach (var target in fun.Take(3))
        {
            var spot = _ent.GetComponent<TransformComponent>(target).Coordinates;
            var path = await _pathfinding.GetPath(owner, coords, spot, 1.5f, cancelToken, _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            ColonyAiLog.Claim(target, owner);
            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} heading to {_ent.ToPrettyString(target)} for fun");
            return (true, Effects(target, spot, path));
        }

        // Nothing to play with: find a friend.
        var friends = new HashSet<Entity<ColonyMoodComponent>>();
        _lookup.GetEntitiesInRange(coords, 25f, friends);
        var others = friends.Where(f => f.Owner != owner).ToList();
        _random.Shuffle(others);
        foreach (var friend in others.Take(3))
        {
            var spot = _ent.GetComponent<TransformComponent>(friend.Owner).Coordinates;
            var path = await _pathfinding.GetPath(owner, coords, spot, 1.5f, cancelToken, _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} going to chat with {_ent.ToPrettyString(friend.Owner)}");
            return (true, Effects(friend.Owner, spot, path));
        }

        return (false, null);
    }

    private static Dictionary<string, object> Effects(EntityUid target, EntityCoordinates spot, PathResultEvent path)
    {
        return new Dictionary<string, object>
        {
            { "PlayTarget", target },
            { "TargetCoordinates", spot },
            { "TargetPathfind", path },
        };
    }
}

/// <summary>
/// Has fun at the target for a while: plays with a machine, or chats with another colonist (who enjoys it too).
/// </summary>
public sealed partial class ColonistPlayOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;

    private sealed class PlayState
    {
        public float Played;
        public float Pending;
        public float OtherPending;
        public float NextNoise = 3f;
        public bool Social;
        public long Last;
    }

    private static readonly Dictionary<EntityUid, PlayState> States = new();

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>("PlayTarget", out var target, _ent) || !_ent.EntityExists(target))
        {
            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} lost its play target");
            States.Remove(owner);
            return HTNOperatorStatus.Failed;
        }

        var now = Environment.TickCount64;
        var who = ColonyAiLog.NameOf(_ent, owner);
        var what = ColonyAiLog.NameOf(_ent, target);

        if (!States.TryGetValue(owner, out var s) || now - s.Last > 2000)
        {
            s = new PlayState { Social = _ent.HasComponent<ColonyMoodComponent>(target) };
            States[owner] = s;
            ColonyAiLog.Action(_ent, owner, s.Social ? $"{who} stops to chat with {what}" : $"{who} plays with the {what}");
        }

        s.Last = now;
        s.Played += frameTime;
        ColonyRest.Fill(_ent, owner, ColonyRest.Recreation, ref s.Pending, s.Social ? 0.5f : 0.9f, frameTime);

        if (s.Social)
            ColonyRest.Fill(_ent, target, ColonyRest.Recreation, ref s.OtherPending, 0.5f, frameTime);

        s.NextNoise -= frameTime;
        if (s.NextNoise <= 0f && !s.Social)
        {
            s.NextNoise = 8f;
            var id = _ent.GetComponent<MetaDataComponent>(target).EntityPrototype?.ID ?? string.Empty;
            var noise = id.Contains("Arcade", StringComparison.OrdinalIgnoreCase) ? "*bleep bloop*"
                : id.Contains("Jukebox", StringComparison.OrdinalIgnoreCase) ? "*jingle jangle*"
                : "*fun noises*";
            ColonyAiLog.Sound(_ent, owner, noise);
        }

        var value = ColonyRest.Value(_ent, owner, ColonyRest.Recreation);
        if (value == null)
        {
            States.Remove(owner);
            return HTNOperatorStatus.Failed;
        }

        if (value.Value >= 95f || s.Played > 120f)
        {
            ColonyRest.Thought(_ent, owner, s.Social ? ColonyRest.Chatted : ColonyRest.HadFun);
            if (s.Social)
                ColonyRest.Thought(_ent, target, ColonyRest.Chatted);

            ColonyAiLog.Action(_ent, owner, s.Social ? $"{who} says goodbye to {what}" : $"{who} is done with the {what}");
            States.Remove(owner);
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Continuing;
    }
}
