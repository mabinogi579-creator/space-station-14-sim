using Robust.Shared.Serialization;

namespace Content.Shared._Colony.Log;

public enum ColonyLogCategory : byte
{
    Speech,
    Action,
    Need,
    Announcement,
    Sound,
}

/// <summary>How big the line is drawn: whispers are small, shouting is big.</summary>
public enum ColonyLogSize : byte
{
    Small,
    Normal,
    Big,
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
    public readonly ColonyLogSize Size;

    public ColonyLogEvent(ColonyLogCategory category, string text, NetEntity? source, ColonyLogSize size = ColonyLogSize.Normal)
    {
        Category = category;
        Text = text;
        Source = source;
        Size = size;
    }
}
