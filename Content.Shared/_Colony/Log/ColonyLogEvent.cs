using Robust.Shared.Serialization;

namespace Content.Shared._Colony.Log;

public enum ColonyLogCategory : byte
{
    Speech,
    Action,
    Need,
    Announcement,
}

/// <summary>
/// One line for the Colony Log panel, sent from the server to every player.
/// </summary>
[Serializable, NetSerializable]
public sealed class ColonyLogEvent : EntityEventArgs
{
    public readonly ColonyLogCategory Category;
    public readonly string Text;
    public readonly NetEntity? Source;

    public ColonyLogEvent(ColonyLogCategory category, string text, NetEntity? source)
    {
        Category = category;
        Text = text;
        Source = source;
    }
}
