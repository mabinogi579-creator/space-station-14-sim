using Content.Server.NPC;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Content.Shared.Power.EntitySystems;
using Content.Shared.VendingMachines;
using Content.Shared.VendingMachines.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Colony.Ai;

/// <summary>
/// Finds loose food (or a drink) lying out in the open and walks toward it.
/// Fails if there is none, so the next branch of the brain gets a turn.
/// </summary>
public sealed partial class PickLooseEdibleOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    [DataField(required: true)]
    public ProtoId<EdiblePrototype> EdibleType;

    [DataField]
    public float Range = 40f;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coords, _ent))
            return (false, null);

        var found = new HashSet<Entity<EdibleComponent>>();
        _lookup.GetEntitiesInRange(coords, Range, found);

        var candidates = new List<(float Distance, EntityUid Uid)>();
        foreach (var edible in found)
        {
            if (_ingestion.GetEdibleType((edible.Owner, edible.Comp)) != EdibleType)
                continue;

            // Skip anything inside a fridge, a hand, a bag, etc. Only things lying out in the open count.
            if (_container.IsEntityOrParentInContainer(edible.Owner))
                continue;

            if (_ingestion.IsEmpty(edible))
                continue;

            // Paper, crayons and other junk count as "edible" in the game, but give nothing. Skip them.
            var value = EdibleType == IngestionSystem.Drink
                ? _ingestion.TotalHydration((edible.Owner, edible.Comp))
                : _ingestion.TotalNutrition((edible.Owner, edible.Comp));
            if (value <= 0f)
                continue;

            var pos = _ent.GetComponent<TransformComponent>(edible.Owner).Coordinates;
            if (!coords.TryDistance(_ent, pos, out var distance))
                continue;

            candidates.Add((distance, edible.Owner));
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        // Only bother pathfinding to the closest few.
        var tried = 0;
        foreach (var (_, target) in candidates)
        {
            if (tried++ >= 4)
                break;

            var path = await _pathfinding.GetPath(owner, target, 1f, cancelToken, flags: _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            return (true, new Dictionary<string, object>
            {
                { "Target", target },
                { "TargetCoordinates", _ent.GetComponent<TransformComponent>(target).Coordinates },
                { "TargetPathfind", path },
            });
        }

        return (false, null);
    }
}

/// <summary>
/// Finds a working vending machine that sells something to eat (or drink) and walks toward it.
/// Remembers which product to buy under the key "VendItem".
/// </summary>
public sealed partial class PickVendingMachineOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;

    [DataField(required: true)]
    public ProtoId<EdiblePrototype> EdibleType;

    [DataField]
    public float Range = 40f;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coords, _ent))
            return (false, null);

        var found = new HashSet<Entity<VendingMachineComponent>>();
        _lookup.GetEntitiesInRange(coords, Range, found);

        var candidates = new List<(float Distance, EntityUid Uid, string Item)>();
        foreach (var machine in found)
        {
            if (machine.Comp.Broken || !_power.IsPowered(machine.Owner))
                continue;

            var item = FindEdibleProduct(machine.Comp);
            if (item == null)
                continue;

            var pos = _ent.GetComponent<TransformComponent>(machine.Owner).Coordinates;
            if (!coords.TryDistance(_ent, pos, out var distance))
                continue;

            candidates.Add((distance, machine.Owner, item));
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var tried = 0;
        foreach (var (_, target, item) in candidates)
        {
            if (tried++ >= 4)
                break;

            var path = await _pathfinding.GetPath(owner, target, 1f, cancelToken, flags: _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path)
                continue;

            return (true, new Dictionary<string, object>
            {
                { "Target", target },
                { "TargetCoordinates", _ent.GetComponent<TransformComponent>(target).Coordinates },
                { "TargetPathfind", path },
                { "VendItem", item },
            });
        }

        return (false, null);
    }

    private string? FindEdibleProduct(VendingMachineComponent machine)
    {
        foreach (var (_, entry) in machine.Inventory)
        {
            if (entry.Amount == 0)
                continue;

            if (!_proto.TryIndex<EntityPrototype>(entry.ID.Id, out var proto))
                continue;

            if (!proto.Components.TryGetValue("Edible", out var registration))
                continue;

            if (registration.Component is EdibleComponent edible && edible.Edible == EdibleType)
                return entry.ID.Id;
        }

        return null;
    }
}

/// <summary>
/// Buys the product chosen by <see cref="PickVendingMachineOperator"/> from the machine in "Target".
/// The item drops out next to the machine, and the brain then picks it up like any other loose food.
/// </summary>
public sealed partial class UseVendingMachineOperator : HTNOperator
{
    [Dependency] private SharedVendingMachineSystem _vending = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        // Leave a short pause afterwards while the machine does its thing.
        return (true, new Dictionary<string, object> { { "IdleTime", 3f } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var machine = blackboard.GetValue<EntityUid>("Target");
        var item = blackboard.GetValue<string>("VendItem");

        _vending.TryEjectVendorItem(machine, InventoryType.Regular, item, false, owner);
        blackboard.SetValue("IdleTime", 3f);
        return HTNOperatorStatus.Finished;
    }
}

/// <summary>
/// Eats or drinks the entity in "Target" (the colonist has to be standing next to it).
/// </summary>
public sealed partial class ColonistEatOperator : HTNOperator
{
    [Dependency] private IngestionSystem _ingestion = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (true, new Dictionary<string, object> { { "IdleTime", 1f } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var target = blackboard.GetValue<EntityUid>("Target");

        var started = _ingestion.TryIngest(owner, target);

        // Eating takes a few seconds (a do-after), so wait a bit before looking for the next bite.
        blackboard.SetValue("IdleTime", started ? 4f : 1f);
        return started ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
