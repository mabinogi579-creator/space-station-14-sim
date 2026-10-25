using Content.Shared._Colony.Traits;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Colony.Traits;

/// <summary>
/// Applies colonist traits: movement speed changes the game's own speed, and the AI asks this
/// system how patient or quick a colonist is.
/// </summary>
public sealed class ColonyTraitSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColonyTraitsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        Log.Info("ColonyTraitSystem initialized");
    }

    private void OnRefreshSpeed(EntityUid uid, ColonyTraitsComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var walk = 1f;
        var run = 1f;
        foreach (var id in comp.Traits)
        {
            if (!ProtoMan.TryIndex(id, out var trait))
                continue;

            walk *= trait.WalkSpeed;
            run *= trait.RunSpeed;
        }

        args.ModifySpeed(walk, run);
    }

    /// <summary>Gives a colonist one or two random traits that do not clash.</summary>
    public void Assign(EntityUid uid)
    {
        var comp = EnsureComp<ColonyTraitsComponent>(uid);
        var all = new List<ColonyTraitPrototype>(ProtoMan.EnumeratePrototypes<ColonyTraitPrototype>());
        _random.Shuffle(all);

        var wanted = _random.Prob(0.5f) ? 2 : 1;
        foreach (var trait in all)
        {
            if (comp.Traits.Count >= wanted)
                break;

            Add(uid, trait.ID);
        }
    }

    public bool Add(EntityUid uid, string traitId)
    {
        if (!ProtoMan.TryIndex<ColonyTraitPrototype>(traitId, out var trait))
            return false;

        var comp = EnsureComp<ColonyTraitsComponent>(uid);
        if (comp.Traits.Contains(trait.ID))
            return false;

        // Don't pile on opposites (Brisk and Slowpoke, ...).
        foreach (var have in comp.Traits)
        {
            if (trait.Conflicts.Contains(have) ||
                (ProtoMan.TryIndex(have, out var other) && other.Conflicts.Contains(trait.ID)))
            {
                return false;
            }
        }

        comp.Traits.Add(trait.ID);
        _speed.RefreshMovementSpeedModifiers(uid);
        return true;
    }

    public bool Remove(EntityUid uid, string traitId)
    {
        if (!TryComp(uid, out ColonyTraitsComponent? comp) || !comp.Traits.Remove(traitId))
            return false;

        _speed.RefreshMovementSpeedModifiers(uid);
        return true;
    }

    public string Describe(EntityUid uid)
    {
        if (!TryComp(uid, out ColonyTraitsComponent? comp) || comp.Traits.Count == 0)
            return "no traits";

        return string.Join(", ", comp.Traits);
    }

    /// <summary>1 for an ordinary colonist; more means they wait longer in a line.</summary>
    public float PatienceMultiplier(EntityUid uid) => Product(uid, t => t.Patience);

    /// <summary>1 for an ordinary colonist; more means they take longer at a machine.</summary>
    public float UseTimeMultiplier(EntityUid uid) => Product(uid, t => t.UseTime);

    private float Product(EntityUid uid, Func<ColonyTraitPrototype, float> pick)
    {
        var result = 1f;
        if (!TryComp(uid, out ColonyTraitsComponent? comp))
            return result;

        foreach (var id in comp.Traits)
        {
            if (ProtoMan.TryIndex(id, out var trait))
                result *= pick(trait);
        }

        return result;
    }
}
