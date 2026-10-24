using System.Numerics;
using Content.Shared._Colony.Mood;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Colony.Mood;

/// <summary>
/// A RimWorld-style mood window: overall mood, the list of thoughts behind it, and the character's needs.
/// It rereads the character's data a few times a second while it is open.
/// </summary>
public sealed class MoodWindow : DefaultWindow
{
    private const float RefreshInterval = 0.25f;

    private static readonly Color Good = Color.FromHex("#6fcf6f");
    private static readonly Color Okay = Color.FromHex("#d8d85a");
    private static readonly Color Warn = Color.FromHex("#e8a04a");
    private static readonly Color Bad = Color.FromHex("#e05555");
    private static readonly Color Dim = Color.FromHex("#9a9aa5");
    private static readonly Color BarBackground = Color.FromHex("#1b1b22");

    private readonly IEntityManager _entMan;
    private readonly SatiationSystem _satiation;
    private readonly IGameTiming _timing;
    private readonly EntityUid _target;
    private readonly BoxContainer _content;

    // Start "due" so the first draw happens immediately.
    private float _sinceRefresh = RefreshInterval;

    public MoodWindow(EntityUid target, IEntityManager entMan, SatiationSystem satiation, IGameTiming timing)
    {
        _target = target;
        _entMan = entMan;
        _satiation = satiation;
        _timing = timing;

        MinSize = new Vector2(380, 480);

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        scroll.AddChild(_content);
        Contents.AddChild(scroll);

        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _sinceRefresh += args.DeltaSeconds;
        if (_sinceRefresh < RefreshInterval)
            return;

        _sinceRefresh = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (!_entMan.TryGetComponent(_target, out ColonyMoodComponent? mood))
        {
            Close();
            return;
        }

        Title = _entMan.GetComponentOrNull<MetaDataComponent>(_target)?.EntityName ?? "Unknown";
        _content.RemoveAllChildren();

        // Overall mood.
        var (band, bandColor) = DescribeMood(mood.Mood);
        _content.AddChild(new Label { Text = $"Mood: {mood.Mood:F0} / 100  ({band})", FontColorOverride = bandColor });
        _content.AddChild(MakeBar(mood.Mood, 100f, bandColor));
        _content.AddChild(new Label { Text = $"Starts at {mood.BaseMood:F0}, then each thought below adds or subtracts.", FontColorOverride = Dim });

        _content.AddChild(new Control { MinHeight = 6 });

        // Thoughts, worst first.
        _content.AddChild(new Label { Text = "Thoughts" });
        if (mood.Display.Count == 0)
        {
            _content.AddChild(new Label { Text = "Nothing on their mind right now.", FontColorOverride = Dim });
        }
        else
        {
            var thoughts = new List<MoodThoughtEntry>(mood.Display);
            thoughts.Sort((a, b) => a.Effect.CompareTo(b.Effect));

            foreach (var thought in thoughts)
            {
                _content.AddChild(MakeThoughtRow(thought));
            }
        }

        _content.AddChild(new Control { MinHeight = 6 });

        // Needs.
        if (_entMan.TryGetComponent(_target, out SatiationComponent? satiations))
        {
            _content.AddChild(new Label { Text = "Needs" });

            var entity = new Entity<SatiationComponent>(_target, satiations);
            foreach (var type in satiations.Satiations.Keys)
            {
                if (_satiation.GetValueOrNull(entity, type) is not { } value ||
                    _satiation.GetMaximumValue(entity, type) is not { } max ||
                    max <= 0)
                    continue;

                var ratio = value / max;
                var color = ratio > 0.5f ? Good : ratio > 0.25f ? Warn : Bad;

                _content.AddChild(new Label { Text = $"{type.Id}:  {value:F0} / {max}" });
                _content.AddChild(MakeBar(value, max, color));
            }
        }
    }

    private Control MakeThoughtRow(MoodThoughtEntry thought)
    {
        var text = thought.Label;
        if (thought.Expires > TimeSpan.Zero)
        {
            var remaining = thought.Expires - _timing.CurTime;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            text += $"   ({(int) remaining.TotalMinutes}:{remaining.Seconds:D2} left)";
        }

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        row.AddChild(new Label { Text = text, HorizontalExpand = true });
        row.AddChild(new Label
        {
            Text = $"{thought.Effect:+0;-0;0}",
            FontColorOverride = thought.Effect >= 0f ? Good : Bad,
        });

        return row;
    }

    private static ProgressBar MakeBar(float value, float max, Color color)
    {
        return new ProgressBar
        {
            MinValue = 0f,
            MaxValue = max,
            Value = value,
            MinHeight = 14,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = BarBackground },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
        };
    }

    private static (string Band, Color Color) DescribeMood(float mood)
    {
        if (mood >= 75f)
            return ("Elated", Good);
        if (mood >= 55f)
            return ("Content", Good);
        if (mood >= 40f)
            return ("Neutral", Okay);
        if (mood >= 25f)
            return ("Stressed", Warn);

        return ("Breaking point", Bad);
    }
}
