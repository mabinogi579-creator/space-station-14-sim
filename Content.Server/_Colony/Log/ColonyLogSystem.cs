using Content.Shared._Colony.Log;
using Content.Shared._Colony.Mood;
using Content.Shared.Chat;
using Robust.Shared.Player;

namespace Content.Server._Colony.Log;

/// <summary>
/// Collects things worth reading (what colonists say, what they are doing, what you can hear)
/// and sends them to the Colony Log panel. Other systems add lines with <see cref="Add"/>.
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

        var message = ev.Message;
        var size = ColonyLogSize.Normal;

        // Like SS13: SHOUTING and exclamations are bigger, trailing off or whispering is smaller.
        var letters = 0;
        var capitals = 0;
        foreach (var c in message)
        {
            if (!char.IsLetter(c))
                continue;

            letters++;
            if (char.IsUpper(c))
                capitals++;
        }

        if (letters >= 3 && capitals * 10 >= letters * 7 || message.EndsWith("!!"))
            size = ColonyLogSize.Big;
        else if (message.StartsWith("...") || message.StartsWith("*") || message.EndsWith("..."))
            size = ColonyLogSize.Small;

        Add(ev.Source, ColonyLogCategory.Speech, $"{Name(ev.Source)} says, \"{message}\"", size);
    }

    public void Add(EntityUid? source, ColonyLogCategory category, string text, ColonyLogSize size = ColonyLogSize.Normal)
    {
        NetEntity? net = source is { } uid && Exists(uid) ? GetNetEntity(uid) : null;
        RaiseNetworkEvent(new ColonyLogEvent(category, text, net, size), Filter.Broadcast());
    }
}
