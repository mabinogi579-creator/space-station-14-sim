using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Colony.Mood;

/// <summary>
/// Holds a character's mood and active thoughts. Attached in YAML as "ColonyMood".
/// </summary>
[RegisterComponent]
public sealed partial class ColonyMoodComponent : Component
{
    /// <summary>
    /// Mood with no thoughts at all, on a 0 to 100 scale.
    /// </summary>
    [DataField]
    public float BaseMood = 50f;

    [ViewVariables]
    public float Mood;

    /// <summary>
    /// Thoughts produced by needs right now. Rebuilt every second.
    /// </summary>
    [ViewVariables]
    public List<ProtoId<ThoughtPrototype>> NeedThoughts = new();

    /// <summary>
    /// Thoughts caused by events, with the time each one expires.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<ThoughtPrototype>, TimeSpan> EventThoughts = new();
}