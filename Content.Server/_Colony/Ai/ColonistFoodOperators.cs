using Content.Server._Colony.Log;
using Content.Server._Colony.Traits;
using Content.Server.NPC;
using Content.Shared._Colony.Log;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._Colony.Diet;
using Content.Shared.Clothing.Components;
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

internal static class ColonyAiLog
{
    private static readonly Dictionary<string, long> LastSeen = new();

    /// <summary>Writes to the server log. The same message is skipped for 5 seconds, because planning repeats itself a lot.</summary>
    public static void Info(string message)
    {
        var now = Environment.TickCount64;
        if (LastSeen.TryGetValue(message, out var last) && now - last < 5000)
            return;

        LastSeen[message] = now;
        Robust.Shared.Log.Logger.InfoS("system.colony_ai", message);
    }

    /// <summary>Adds a line to the Colony Log panel.</summary>
    public static void Action(IEntityManager ent, EntityUid who, string text)
    {
        ent.System<ColonyLogSystem>().Add(who, ColonyLogCategory.Action, text);
    }

    /// <summary>Adds an SS13-style sound line ("*clunk*") to the Colony Log panel.</summary>
    public static void Sound(IEntityManager ent, EntityUid who, string text)
    {
        ent.System<ColonyLogSystem>().Add(who, ColonyLogCategory.Sound, text);
    }

    public static string NameOf(IEntityManager ent, EntityUid uid)
    {
        // Food gets deleted once it is eaten, so the name may no longer exist.
        return ent.TryGetComponent(uid, out MetaDataComponent? meta) ? meta.EntityName : "something";
    }

    private static readonly Dictionary<(EntityUid Who, EntityUid What), long> Ignored = new();

    /// <summary>Remember that this colonist couldn't eat this thing, so they stop trying it for a minute.</summary>
    public static void Ignore(EntityUid who, EntityUid what) => Ignored[(who, what)] = Environment.TickCount64;

    public static bool IsIgnored(EntityUid who, EntityUid what)
    {
        return Ignored.TryGetValue((who, what), out var when) && Environment.TickCount64 - when < 60_000;
    }

    private static readonly Dictionary<EntityUid, (EntityUid Who, long When)> Claims = new();
    private static readonly Dictionary<EntityUid, (EntityUid Who, long When)> Vends = new();

    /// <summary>Reserve a target so other colonists leave it alone for a while (stops everyone crowding the same item).</summary>
    public static void Claim(EntityUid target, EntityUid who) => Claims[target] = (who, Environment.TickCount64);

    public static bool ClaimedByOther(EntityUid target, EntityUid who, long ms)
    {
        return Claims.TryGetValue(target, out var c) && c.Who != who && Environment.TickCount64 - c.When < ms;
    }

    /// <summary>Remember that this colonist just bought something here, so the item that drops out is theirs.</summary>
    public static void NoteVend(EntityUid machine, EntityUid who) => Vends[machine] = (who, Environment.TickCount64);

    /// <summary>True if the food is lying next to a machine where someone else has just bought it.</summary>
    public static bool IsSomeoneElsesPurchase(IEntityManager ent, EntityUid food, EntityUid who)
    {
        var xform = ent.System<SharedTransformSystem>();
        foreach (var (machine, vend) in Vends)
        {
            if (vend.Who == who || Environment.TickCount64 - vend.When > 12_000 || !ent.EntityExists(machine))
                continue;

            if ((xform.GetWorldPosition(machine) - xform.GetWorldPosition(food)).Length() < 2.5f)
                return true;
        }

        return false;
    }

    // ---- Vending machine queues ----
    public const int MaxQueue = 3;
    private static readonly Dictionary<EntityUid, List<(EntityUid Who, long When)>> Queues = new();
    private static readonly Dictionary<EntityUid, (EntityUid Who, long Start)> Serving = new();
    private static readonly Dictionary<EntityUid, long> WaitStart = new();

    /// <summary>How long a colonist will stand in line before giving up. Traits will change this later.</summary>
    public static float PatienceOf(IEntityManager ent, EntityUid who) =>
        (12f + (who.Id % 7) * 2f) * ent.System<ColonyTraitSystem>().PatienceMultiplier(who);

    /// <summary>How long a colonist keeps the machine busy. Traits will change this later (clumsy, slow, quick...).</summary>
    public static float UseDelayOf(IEntityManager ent, EntityUid who) =>
        3f * ent.System<ColonyTraitSystem>().UseTimeMultiplier(who);

    /// <summary>Join the line for a machine. Returns false if the line is already full.</summary>
    public static bool TryJoinQueue(IEntityManager ent, EntityUid machine, EntityUid who)
    {
        var now = Environment.TickCount64;
        foreach (var q in Queues.Values)
            q.RemoveAll(e => now - e.When > 40_000 || !ent.EntityExists(e.Who));

        if (!Queues.TryGetValue(machine, out var list))
            Queues[machine] = list = new();

        var index = list.FindIndex(e => e.Who == who);
        if (index >= 0)
        {
            list[index] = (who, now);
            return true;
        }

        if (list.Count >= MaxQueue)
            return false;

        // Only one line at a time.
        foreach (var (m, q) in Queues)
        {
            if (m != machine)
                q.RemoveAll(e => e.Who == who);
        }

        list.Add((who, now));
        WaitStart.Remove(who);
        return true;
    }

    public static void LeaveQueue(EntityUid machine, EntityUid who)
    {
        if (Queues.TryGetValue(machine, out var list))
            list.RemoveAll(e => e.Who == who);

        if (Serving.TryGetValue(machine, out var s) && s.Who == who)
            Serving.Remove(machine);

        WaitStart.Remove(who);
    }

    /// <summary>Is someone else using this machine right now?</summary>
    public static bool IsBusyWithOther(IEntityManager ent, EntityUid machine, EntityUid who)
    {
        return Serving.TryGetValue(machine, out var s)
            && s.Who != who
            && ent.EntityExists(s.Who)
            && Environment.TickCount64 - s.Start < 15_000;
    }

    public static void StartServing(EntityUid machine, EntityUid who) => Serving[machine] = (who, Environment.TickCount64);

    public static float ServedFor(EntityUid machine, EntityUid who)
    {
        return Serving.TryGetValue(machine, out var s) && s.Who == who
            ? (Environment.TickCount64 - s.Start) / 1000f
            : -1f;
    }

    /// <summary>Seconds this colonist has been waiting at the machine.</summary>
    public static float WaitedFor(EntityUid who)
    {
        var now = Environment.TickCount64;
        if (!WaitStart.TryGetValue(who, out var start))
        {
            WaitStart[who] = now;
            return 0f;
        }

        return (now - start) / 1000f;
    }

    // Things the game calls "edible" but that are not a meal. Matched against the item's prototype name.
    private static readonly string[] NotFood = { "Paper", "Trash", "Wrapper", "Crayon", "Packet", "Material", "Cloth", "Thread", "Pill", "Soap" };

    /// <summary>The colonist's diet, or null if they have none (then they eat any real food).</summary>
    public static ColonyDietPrototype? DietOf(IEntityManager ent, IPrototypeManager proto, EntityUid who)
    {
        return ent.TryGetComponent(who, out ColonyDietComponent? comp) && proto.TryIndex(comp.Diet, out var diet)
            ? diet
            : null;
    }

    private static bool HasWord(string id, List<string> words)
    {
        foreach (var word in words)
        {
            if (id.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Can this colonist eat this kind of item, going by their diet?</summary>
    public static bool Allows(ColonyDietPrototype? diet, string? protoId, bool isClothing)
    {
        if (isClothing)
            return diet?.EatClothing ?? false;

        if (protoId == null)
            return true;

        if (diet == null || !diet.EatJunk)
        {
            foreach (var word in NotFood)
            {
                if (protoId.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return diet == null || !HasWord(protoId, diet.Forbidden);
    }

    public static bool IsFavorite(ColonyDietPrototype? diet, string? protoId)
    {
        return diet != null && protoId != null && HasWord(protoId, diet.Favorites);
    }
}

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
    [Dependency] private IPrototypeManager _proto = default!;

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

        var diet = ColonyAiLog.DietOf(_ent, _proto, owner);
        var found = new HashSet<Entity<EdibleComponent>>();
        _lookup.GetEntitiesInRange(coords, Range, found);

        var candidates = new List<(float Distance, EntityUid Uid)>();
        int wrongType = 0, inContainer = 0, empty = 0, notAllowed = 0, noValue = 0;
        foreach (var edible in found)
        {
            if (_ingestion.GetEdibleType((edible.Owner, edible.Comp)) != EdibleType)
            {
                wrongType++;
                continue;
            }

            // Skip anything inside a fridge, a hand, a bag, etc. Only things lying out in the open count.
            if (_container.IsEntityOrParentInContainer(edible.Owner))
            {
                inContainer++;
                continue;
            }

            if (_ingestion.IsEmpty(edible))
            {
                empty++;
                continue;
            }

            if (ColonyAiLog.IsIgnored(owner, edible.Owner)
                || ColonyAiLog.ClaimedByOther(edible.Owner, owner, 20_000)
                || ColonyAiLog.IsSomeoneElsesPurchase(_ent, edible.Owner, owner))
                continue;

            var protoId = _ent.GetComponent<MetaDataComponent>(edible.Owner).EntityPrototype?.ID;
            if (!ColonyAiLog.Allows(diet, protoId, _ent.HasComponent<ClothingComponent>(edible.Owner)))
            {
                notAllowed++;
                continue;
            }

            // Paper, crayons and other junk count as "edible" in the game, but give nothing. Skip them.
            var value = EdibleType == IngestionSystem.Drink
                ? _ingestion.TotalHydration((edible.Owner, edible.Comp))
                : _ingestion.TotalNutrition((edible.Owner, edible.Comp));
            if (value <= 0f)
            {
                noValue++;
                continue;
            }

            var pos = _ent.GetComponent<TransformComponent>(edible.Owner).Coordinates;
            if (!coords.TryDistance(_ent, pos, out var distance))
                continue;

            // Favorite foods count as if they were closer, so the colonist goes out of their way for them.
            if (ColonyAiLog.IsFavorite(diet, protoId))
                distance *= 0.4f;

            candidates.Add((distance, edible.Owner));
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} looking for loose {EdibleType}: {found.Count} edibles nearby, {candidates.Count} usable (skipped: {wrongType} other type, {inContainer} in containers, {empty} empty, {notAllowed} junk/diet, {noValue} no nutrition)");

        // Only bother pathfinding to the closest few.
        var tried = 0;
        foreach (var (_, target) in candidates)
        {
            if (tried++ >= 4)
                break;

            ColonyAiLog.Claim(target, owner);
            var path = await _pathfinding.GetPath(owner, target, 1f, cancelToken, flags: _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path || !_ent.EntityExists(target))
                continue;

            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} going to eat {_ent.ToPrettyString(target)}");
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

        var diet = ColonyAiLog.DietOf(_ent, _proto, owner);
        var found = new HashSet<Entity<VendingMachineComponent>>();
        _lookup.GetEntitiesInRange(coords, Range, found);

        var candidates = new List<(float Distance, EntityUid Uid, string Item)>();
        int broken = 0, noPower = 0, noProduct = 0, full = 0;
        foreach (var machine in found)
        {
            if (ColonyAiLog.IsIgnored(owner, machine.Owner))
                continue;

            if (machine.Comp.Broken)
            {
                broken++;
                continue;
            }

            if (!_power.IsPowered(machine.Owner))
            {
                noPower++;
                continue;
            }

            var item = FindEdibleProduct(machine.Comp, diet);
            if (item == null)
            {
                noProduct++;
                continue;
            }

            var pos = _ent.GetComponent<TransformComponent>(machine.Owner).Coordinates;
            if (!coords.TryDistance(_ent, pos, out var distance))
                continue;

            candidates.Add((distance, machine.Owner, item));
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} looking for a vending machine selling {EdibleType}: {found.Count} machines nearby, {candidates.Count} usable (skipped: {broken} broken, {noPower} unpowered, {noProduct} without suitable product, {full} lines full)");

        var tried = 0;
        foreach (var (_, target, item) in candidates)
        {
            if (tried++ >= 4)
                break;

            if (!ColonyAiLog.TryJoinQueue(_ent, target, owner))
            {
                full++;
                ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} sees the line at {_ent.ToPrettyString(target)} is full");
                continue;
            }

            var path = await _pathfinding.GetPath(owner, target, 1f, cancelToken, flags: _pathfinding.GetFlags(blackboard));
            if (path.Result != PathResult.Path || !_ent.EntityExists(target))
                continue;

            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} going to buy {item} from {_ent.ToPrettyString(target)}");
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

    private string? FindEdibleProduct(VendingMachineComponent machine, ColonyDietPrototype? diet)
    {
        string? fallback = null;
        foreach (var (_, entry) in machine.Inventory)
        {
            if (entry.Amount == 0)
                continue;

            if (!_proto.TryIndex<EntityPrototype>(entry.ID.Id, out var proto))
                continue;

            if (!proto.Components.TryGetValue("Edible", out var registration))
                continue;

            if (registration.Component is not EdibleComponent edible || edible.Edible != EdibleType)
                continue;

            if (!ColonyAiLog.Allows(diet, entry.ID.Id, proto.Components.ContainsKey("Clothing")))
                continue;

            if (ColonyAiLog.IsFavorite(diet, entry.ID.Id))
                return entry.ID.Id;

            fallback ??= entry.ID.Id;
        }

        return fallback;
    }
}

/// <summary>
/// Buys the product chosen by <see cref="PickVendingMachineOperator"/> from the machine in "Target".
/// The item drops out next to the machine, and the brain then picks it up like any other loose food.
/// </summary>
public sealed partial class UseVendingMachineOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
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

        if (!_ent.EntityExists(machine) || !_ent.TryGetComponent<VendingMachineComponent>(machine, out var vendingComp))
        {
            ColonyAiLog.LeaveQueue(machine, owner);
            return HTNOperatorStatus.Failed;
        }

        // Someone else is at the machine: wait in line, but not forever.
        if (ColonyAiLog.IsBusyWithOther(_ent, machine, owner))
        {
            if (ColonyAiLog.WaitedFor(owner) > ColonyAiLog.PatienceOf(_ent, owner))
            {
                ColonyAiLog.LeaveQueue(machine, owner);
                ColonyAiLog.Ignore(owner, machine);
                ColonyAiLog.Action(_ent, owner, $"{ColonyAiLog.NameOf(_ent, owner)} gets tired of waiting at {ColonyAiLog.NameOf(_ent, machine)} and goes elsewhere");
                return HTNOperatorStatus.Failed;
            }

            return HTNOperatorStatus.Continuing;
        }

        var served = ColonyAiLog.ServedFor(machine, owner);
        if (served < 0f)
        {
            // Our turn.
            ColonyAiLog.StartServing(machine, owner);
            ColonyAiLog.NoteVend(machine, owner);
            ColonyAiLog.Info($"{_ent.ToPrettyString(owner)} buying {item} from {_ent.ToPrettyString(machine)}");
            ColonyAiLog.Action(_ent, owner, $"{ColonyAiLog.NameOf(_ent, owner)} buys something from {ColonyAiLog.NameOf(_ent, machine)}");
            _vending.AuthorizedVend(machine, owner, InventoryType.Regular, item, vendingComp);
            ColonyAiLog.Sound(_ent, owner, $"*clunk* Something drops out of the {ColonyAiLog.NameOf(_ent, machine)}.");
            return HTNOperatorStatus.Continuing;
        }

        // Keep the machine busy while they fumble with the buttons and pick up the snack.
        if (served < ColonyAiLog.UseDelayOf(_ent, owner))
            return HTNOperatorStatus.Continuing;

        ColonyAiLog.LeaveQueue(machine, owner);
        blackboard.SetValue("IdleTime", 3f);
        return HTNOperatorStatus.Finished;
    }
}

/// <summary>
/// Eats or drinks the entity in "Target" (the colonist has to be standing next to it).
/// </summary>
public sealed partial class ColonistEatOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
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

        // Someone else may have eaten it while we were walking over.
        if (!_ent.EntityExists(target))
            return HTNOperatorStatus.Failed;

        // Work out the names first, because the food is deleted once it has been eaten.
        var who = ColonyAiLog.NameOf(_ent, owner);
        var what = ColonyAiLog.NameOf(_ent, target);

        var started = _ingestion.TryIngest(owner, target);
        ColonyAiLog.Info($"{who} tried to eat {what}: {(started ? "started" : "FAILED")}");

        if (started)
        {
            ColonyAiLog.Action(_ent, owner, $"{who} starts on the {what}");
        }
        else
        {
            ColonyAiLog.Ignore(owner, target);
            ColonyAiLog.Action(_ent, owner, $"{who} can't eat the {what}");
        }

        // Eating takes a few seconds (a do-after), so wait a bit before looking for the next bite.
        blackboard.SetValue("IdleTime", started ? 4f : 1f);
        return started ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
