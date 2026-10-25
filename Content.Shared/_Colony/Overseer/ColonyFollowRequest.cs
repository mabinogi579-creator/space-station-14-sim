using Robust.Shared.Serialization;

namespace Content.Shared._Colony.Overseer;

/// <summary>Sent by the client when the overseer double-clicks a colonist portrait: "take my camera to this person".</summary>
[Serializable, NetSerializable]
public sealed class ColonyFollowRequest(NetEntity target) : EntityEventArgs
{
    public NetEntity Target = target;
}
