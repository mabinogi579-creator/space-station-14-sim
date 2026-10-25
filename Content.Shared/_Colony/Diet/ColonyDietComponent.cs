using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Diet;

/// <summary>
/// Which diet a colonist follows. Change it in game with the colonydiet command.
/// </summary>
[RegisterComponent]
public sealed partial class ColonyDietComponent : Component
{
    [DataField]
    public ProtoId<ColonyDietPrototype> Diet = "Omnivore";
}
