using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Colony.Mood;

/// <summary>
/// Holds a character's mood and active thoughts. Attached in YAML as "ColonyMood".
/// The server works out the thoughts; the parts marked as networked are copied to every client
/// so the mood window can read them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ColonyMoodComponent : Component
{
    /// <summary>
    /// Mood with no thoughts at all, on a 0 to 100 scale.
    /// </summary>
    [DataField]
    public float BaseMood = 50f;

    [ViewVariables, AutoNetworkedField]
    public float Mood;

    /// <summary>
    /// What clients see: every active thought, ready to show in the mood window.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<MoodThoughtEntry> Display = new();

    /// <summary>
    /// Thoughts produced by needs right now. Rebuilt every second. Server only.
    /// </summary>
    [ViewVariables]
    public List<ProtoId<ThoughtPrototype>> NeedThoughts = new();

    /// <summary>
    /// Thoughts caused by events, with the time each one expires. Server only.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<ThoughtPrototype>, TimeSpan> EventThoughts = new();
}

/// <summary>
/// One line in the mood window. <paramref name="Expires"/> is zero for thoughts that last as long as a need stays
/// in its band, and the time the thought runs out for event thoughts.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct MoodThoughtEntry(string Label, float Effect, TimeSpan Expires);
