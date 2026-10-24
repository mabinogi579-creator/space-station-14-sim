using System.Numerics;
using Content.Shared._Colony.Mood;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Colony.Mood;

/// <summary>
/// A RimWorld-style strip across the top of the screen: one portrait per colonist with a mood bar.
/// Click a portrait to open that colonist's mood window.
/// </summary>
public sealed class ColonistBar : BoxContainer
{
    private static readonly Color Good = Color.FromHex("#6fcf6f");
    private static readonly Color Okay = Color.FromHex("#d8d85a");
    private static readonly Color Warn = Color.FromHex("#e8a04a");
    private static readonly Color Bad = Color.FromHex("#e05555");
    private static readonly Color BarBackground = Color.FromHex("#1b1b22");

    private readonly IEntityManager _entMan;
    private readonly Action<EntityUid> _onClick;
    private bool _built;
    private readonly List<EntityUid> _shown = new();
    private readonly Dictionary<EntityUid, ProgressBar> _bars = new();

    public ColonistBar(IEntityManager entMan, Action<EntityUid> onClick)
    {
        _entMan = entMan;
        _onClick = onClick;
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Work out who should be on the bar right now.
        var current = new List<EntityUid>();
        var query = _entMan.AllEntityQueryEnumerator<ColonyMoodComponent>();
        while (query.MoveNext(out var uid, out _))
            current.Add(uid);

        current.Sort();

        if (!_built || !System.Linq.Enumerable.SequenceEqual(current, _shown))
            Rebuild(current);

        // Keep the mood bars fresh.
        foreach (var (uid, bar) in _bars)
        {
            if (!_entMan.TryGetComponent(uid, out ColonyMoodComponent? mood))
                continue;

            bar.Value = mood.Mood;
            bar.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = MoodColor(mood.Mood) };
        }
    }

    private void Rebuild(List<EntityUid> colonists)
    {
        _built = true;
        RemoveAllChildren();
        _bars.Clear();
        _shown.Clear();
        _shown.AddRange(colonists);

        // So you can tell the bar itself is working even when nobody is in range yet.
        if (colonists.Count == 0)
        {
            AddChild(new Label { Text = "Colonists: none in view" });
            return;
        }

        foreach (var uid in colonists)
        {
            var target = uid;
            var name = _entMan.GetComponent<MetaDataComponent>(uid).EntityName;
            var firstName = name.Split(' ')[0];

            var column = new BoxContainer { Orientation = LayoutOrientation.Vertical };
            column.AddChild(new SpriteView(uid, _entMan) { Scale = new Vector2(2, 2) });
            column.AddChild(new Label { Text = firstName, HorizontalAlignment = HAlignment.Center });

            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                MinSize = new Vector2(48, 8),
                BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = BarBackground },
                ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Good },
            };
            column.AddChild(bar);
            _bars[uid] = bar;

            var button = new ContainerButton();
            button.AddChild(column);
            button.OnPressed += _ => _onClick(target);
            AddChild(button);
        }
    }

    private static Color MoodColor(float mood)
    {
        if (mood >= 55)
            return Good;
        if (mood >= 40)
            return Okay;
        if (mood >= 25)
            return Warn;
        return Bad;
    }
}
