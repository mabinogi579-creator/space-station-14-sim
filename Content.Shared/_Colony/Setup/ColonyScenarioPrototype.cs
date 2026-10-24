using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Setup;

/// <summary>
/// A starting scenario: who is on the station at the beginning, and how many of them are secretly traitors.
/// Add new ones in Resources/Prototypes/_Colony/Setup/scenarios.yml.
/// </summary>
[Prototype]
public sealed partial class ColonyScenarioPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>Job and how many colonists have it.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<JobPrototype>, int> Crew = new();

    /// <summary>How many of the crew are picked at random to be traitors.</summary>
    [DataField]
    public int Traitors;
}
