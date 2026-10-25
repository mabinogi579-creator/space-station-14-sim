using Content.Shared._Colony.Log;
using Content.Shared._Colony.Mood;
using Content.Shared.Chat;
using Robust.Shared.Player;

namespace Content.Server._Colony.Log;

/// <summary>
/// Collects things worth reading (what colonists say, what they are doing) and sends them to the Colony Log panel.
/// Other systems add lines with <see cref="Add"/>.
/// </summary>
public sealed class ColonyLogSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        Log.Info("ColonyLogSystem initialized");
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        // Only colonists' speech goes in the log, not vending machines, animals and so on.
        if (!HasComp<ColonyMoodComponent>(ev.Source))
            return;

        Add(ev.Source, ColonyLogCategory.Speech, $"{Name(ev.Source)}: \"{ev.Message}\"");
    }

    public void Add(EntityUid? source, ColonyLogCategory category, string text)
    {
        NetEntity? net = source is { } uid && Exists(uid) ? GetNetEntity(uid) : null;
        RaiseNetworkEvent(new ColonyLogEvent(category, text, net), Filter.Broadcast());
    }
}
