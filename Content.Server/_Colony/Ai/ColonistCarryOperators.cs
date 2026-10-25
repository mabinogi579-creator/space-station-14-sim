using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Colony.Ai;

internal static class ColonyCarry
{
    /// <summary>The first thing in the colonist's hands that they can and want to eat or drink.</summary>
    public static EntityUid? HeldFood(IEntityManager ent, IPrototypeManager proto, EntityUid who, ProtoId<EdiblePrototype> type)
    {
        var hands = ent.System<SharedHandsSystem>();
        var ingestion = ent.System<IngestionSystem>();
        var diet = ColonyAiLog.DietOf(ent, proto, who);

        foreach (var held in hands.EnumerateHeld(who))
        {
            if (!ent.TryGetComponent(held, out EdibleComponent? edible))
                continue;

            if (ingestion.GetEdibleType((held, edible)) != type)
                continue;

            if (ingestion.IsEmpty((held, edible)))
                continue;

            var protoId = ent.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID;
            if (!ColonyAiLog.Allows(diet, protoId, false))
                continue;

            var value = type == IngestionSystem.Drink
                ? ingestion.TotalHydration((held, edible))
                : ingestion.TotalNutrition((held, edible));
            if (value <= 0f)
                continue;

            return held;
        }

        return null;
    }
}

/// <summary>True when the colonist is already carrying something they could eat (or drink).</summary>
public sealed partial class HoldingFoodPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    [DataField(required: true)]
    public ProtoId<EdiblePrototype> EdibleType;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return ColonyCarry.HeldFood(_ent, _proto, owner, EdibleType) != null;
    }
}

/// <summary>
/// Picks the food in "Target" up into the colonist's hands, to eat somewhere quieter.
/// If their hands are full or it can't be carried, they eat it where it lies.
/// </summary>
public sealed partial class ColonistGrabOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IngestionSystem _ingestion = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, new Dictionary<string, object> { { "IdleTime", 0.5f } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var target = blackboard.GetValue<EntityUid>("Target");

        if (!_ent.EntityExists(target))
            return HTNOperatorStatus.Failed;

        var who = ColonyAiLog.NameOf(_ent, owner);
        var what = ColonyAiLog.NameOf(_ent, target);

        if (_hands.TryPickupAnyHand(owner, target))
        {
            ColonyAiLog.Action(_ent, owner, $"{who} picks up the {what}");
            blackboard.SetValue("IdleTime", 0.5f);
            return HTNOperatorStatus.Finished;
        }

        // Can't carry it: eat it right here.
        var started = _ingestion.TryIngest(owner, target);
        if (started)
        {
            ColonyAiLog.Action(_ent, owner, $"{who} starts on the {what}");
            blackboard.SetValue("IdleTime", 4f);
            return HTNOperatorStatus.Finished;
        }

        ColonyAiLog.Ignore(owner, target);
        ColonyAiLog.Action(_ent, owner, $"{who} can't eat the {what}");
        return HTNOperatorStatus.Failed;
    }
}

/// <summary>
/// Finds a place to eat what the colonist is carrying: a free chair or bench some of the time,
/// otherwise a spot beside a wall to lean on. Sets "TargetCoordinates", "TargetPathfind" and "Seat".
/// </summary>
public sealed partial class PickEatingSpotOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;

    private static readonly string[] SeatWords = { "Chair", "Stool", "Bench", "Sofa", "Couch", "Seat" };
    private static readonly Dictionary<EntityUid, (EntityUid Seat, EntityCoordinates Spot, long When)> Recent = new();

    private static readonly Vector2[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coords, _ent))
            return (false, null);

        // Standing up from the last seat.
        _buckle.TryUnbuckle(owner, owner, popup: false);

        // Keep using the same spot for a while, so a long meal doesn't turn into a wander.
        if (Recent.TryGetValue(owner, out var last) && Environment.TickCount64 - last.When < 90_000)
        {
            var path = await _pathfinding.GetPath(owner, coords, last.Spot, 1f, cancelToken, _pathfinding.GetFlags(blackboard));
            if (path.Result == PathResult.Path)
                return (true, Effects(last.Spot, path, last.Seat));
        }

        var attempts = new List<(EntityCoordinates Spot, EntityUid Seat)>();

        // Free chairs and benches nearby.
        var seats = new HashSet<Entity<StrapComponent>>();
        _lookup.GetEntitiesInRange(coords, 12f, seats);
        var seatList = new List<Entity<StrapComponent>>();
        foreach (var seat in seats)
        {
            if (!seat.Comp.Enabled || seat.Comp.BuckledEntities.Count > 0 || ColonyAiLog.ClaimedByOther(seat.Owner, owner, 90_000))
                continue;

            var id = _ent.GetComponent<MetaDataComponent>(seat.Owner).EntityPrototype?.ID ?? string.Empty;
            if (SeatWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase)))
                seatList.Add(seat);
        }

        // Walls to lean on.
        var occluders = new HashSet<Entity<OccluderComponent>>();
        _lookup.GetEntitiesInRange(coords, 10f, occluders);
        var wallSpots = new List<EntityCoordinates>();
        foreach (var wall in occluders)
        {
            var xform = _ent.GetComponent<TransformComponent>(wall.Owner);
            if (!xform.Anchored)
                continue;

            wallSpots.Add(xform.Coordinates.Offset(_random.Pick(Sides)));
        }

        _random.Shuffle(seatList);
        _random.Shuffle(wallSpots);

        // Sit about half the time when there is somewhere to sit.
        var preferSeat = seatList.Count > 0 && _random.Prob(0.5f);
        foreach (var seat in seatList.Take(preferSeat ? 3 : 1))
            attempts.Add((_ent.GetComponent<TransformComponent>(seat.Owner).Coordinates, seat.Owner));

        foreach (var spot in wallSpots.Take(6))
            attempts.Add((spot, EntityUid.Invalid));

        if (!preferSeat)
            attempts.Reverse();

        var tried = 0;
        foreach (var (spot, seat) in attempts)
        {
            if (tried++ >= 5)
                break;

            var path = await _pathfinding.GetPath(owner, coords, spot, 1f, cancelToken, _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            if (seat.IsValid())
                ColonyAiLog.Claim(seat, owner);

            Recent[owner] = (seat, spot, Environment.TickCount64);
            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} heading to a {(seat.IsValid() ? "seat" : "wall")} to eat");
            return (true, Effects(spot, path, seat));
        }

        // Nowhere better: eat where they stand.
        ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} found no eating spot, eating on the spot");
        return (true, Effects(coords, null, EntityUid.Invalid));
    }

    private static Dictionary<string, object> Effects(EntityCoordinates spot, PathResultEvent? path, EntityUid seat)
    {
        var effects = new Dictionary<string, object>
        {
            { "TargetCoordinates", spot },
            { "Seat", seat },
        };

        if (path != null)
            effects["TargetPathfind"] = path;

        return effects;
    }
}

/// <summary>Sits on the chosen seat (if any) and eats or drinks what the colonist is carrying.</summary>
public sealed partial class ColonistEatHeldOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;

    [DataField(required: true)]
    public ProtoId<EdiblePrototype> EdibleType;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, new Dictionary<string, object> { { "IdleTime", 1f } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var held = ColonyCarry.HeldFood(_ent, _proto, owner, EdibleType);
        if (held == null)
            return HTNOperatorStatus.Failed;

        var who = ColonyAiLog.NameOf(_ent, owner);
        var what = ColonyAiLog.NameOf(_ent, held.Value);

        var sat = false;
        if (blackboard.TryGetValue<EntityUid>("Seat", out var seat, _ent) && seat.IsValid() && _ent.EntityExists(seat))
            sat = _buckle.TryBuckle(owner, owner, seat, popup: false);

        var started = _ingestion.TryIngest(owner, held.Value);
        if (!started)
        {
            ColonyAiLog.Action(_ent, owner, $"{who} can't eat the {what}");
            blackboard.SetValue("IdleTime", 1f);
            return HTNOperatorStatus.Failed;
        }

        ColonyAiLog.Action(_ent, owner, sat ? $"{who} sits down with the {what}" : $"{who} settles in to eat the {what}");
        blackboard.SetValue("IdleTime", 6f);
        return HTNOperatorStatus.Finished;
    }
}

/// <summary>Gets up from a seat once the meal is over.</summary>
public sealed partial class ColonistStandUpOperator : HTNOperator
{
    [Dependency] private SharedBuckleSystem _buckle = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _buckle.TryUnbuckle(owner, owner, popup: false);
        return HTNOperatorStatus.Finished;
    }
}
