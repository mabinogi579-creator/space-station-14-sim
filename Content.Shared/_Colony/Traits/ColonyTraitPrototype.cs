using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Traits;

/// <summary>
/// A personality trait. Multipliers stack by multiplying: 1 changes nothing, 1.2 is 20% more, 0.8 is 20% less.
/// Add new ones in Resources/Prototypes/_Colony/Traits/traits.yml.
/// </summary>
[Prototype]
public sealed partial class ColonyTraitPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>How fast they walk.</summary>
    [DataField]
    public float WalkSpeed = 1f;

    /// <summary>How fast they run.</summary>
    [DataField]
    public float RunSpeed = 1f;

    /// <summary>How long they will wait in a line before giving up.</summary>
    [DataField]
    public float Patience = 1f;

    /// <summary>How long they take to use a machine. Smaller is quicker.</summary>
    [DataField]
    public float UseTime = 1f;

    /// <summary>Traits that cannot be had together with this one.</summary>
    [DataField]
    public List<ProtoId<ColonyTraitPrototype>> Conflicts = new();
}
