using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Traits;

/// <summary>The traits a colonist has. Change them in game with the colonytrait command.</summary>
[RegisterComponent]
public sealed partial class ColonyTraitsComponent : Component
{
    [DataField]
    public List<ProtoId<ColonyTraitPrototype>> Traits = new();
}
