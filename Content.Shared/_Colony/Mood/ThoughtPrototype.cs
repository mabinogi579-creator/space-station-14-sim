using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Mood;

/// <summary>
/// A single thought: a label, how much it moves mood, and (for event thoughts) how long it lasts.
/// </summary>
[Prototype]
public sealed partial class ThoughtPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Label = string.Empty;

    [DataField(required: true)]
    public float MoodEffect;

    /// <summary>
    /// Only used by event thoughts. Need thoughts last as long as the need stays in its band.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.Zero;
}

/// <summary>
/// Says which thought a need produces at each threshold, using the same threshold names as the satiation YAML.
/// </summary>
[Prototype]
public sealed partial class NeedThoughtsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<SatiationTypePrototype> Need;

    [DataField(required: true)]
    public Dictionary<SatiationValue, ProtoId<ThoughtPrototype>> Thoughts = new();
}