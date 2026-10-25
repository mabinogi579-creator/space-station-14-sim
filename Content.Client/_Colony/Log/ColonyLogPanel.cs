using System.Linq;
using System.Numerics;
using Content.Shared._Colony.Log;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Colony.Log;

/// <summary>
/// A box in the bottom left of the screen listing recent events, with buttons to show or hide each kind.
/// </summary>
public sealed class ColonyLogPanel : PanelContainer
{
    private const int KeptLines = 300;
    private const int ShownLines = 12;

    private static readonly Dictionary<ColonyLogCategory, Color> Colors = new()
    {
        { ColonyLogCategory.Speech, Color.FromHex("#f2f2f2") },
        { ColonyLogCategory.Action, Color.FromHex("#8cc7ff") },
        { ColonyLogCategory.Need, Color.FromHex("#f0a860") },
        { ColonyLogCategory.Announcement, Color.FromHex("#ffe066") },
    };

    private readonly List<(ColonyLogCategory Category, string Text)> _lines = new();
    private readonly HashSet<ColonyLogCategory> _hidden = new();
    private readonly BoxContainer _body;

    public ColonyLogPanel()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101018d0") };
        SetSize = new Vector2(460, 230);

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6),
        };
        AddChild(column);

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        header.AddChild(new Label { Text = "Colony log  " });
        AddFilter(header, "Speech", ColonyLogCategory.Speech);
        AddFilter(header, "Actions", ColonyLogCategory.Action);
        AddFilter(header, "Needs", ColonyLogCategory.Need);
        AddFilter(header, "News", ColonyLogCategory.Announcement);
        column.AddChild(header);

        _body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        column.AddChild(_body);
    }

    private void AddFilter(Control parent, string text, ColonyLogCategory category)
    {
        var button = new Button { Text = text, ToggleMode = true, Pressed = true };
        button.OnToggled += args =>
        {
            if (args.Pressed)
                _hidden.Remove(category);
            else
                _hidden.Add(category);

            Rebuild();
        };
        parent.AddChild(button);
    }

    public void AddLine(ColonyLogCategory category, string text)
    {
        _lines.Add((category, text));
        if (_lines.Count > KeptLines)
            _lines.RemoveAt(0);

        Rebuild();
    }

    private void Rebuild()
    {
        _body.RemoveAllChildren();

        var visible = _lines.Where(l => !_hidden.Contains(l.Category)).ToList();
        foreach (var (category, text) in visible.Skip(Math.Max(0, visible.Count - ShownLines)))
        {
            _body.AddChild(new Label
            {
                Text = text,
                FontColorOverride = Colors[category],
                ClipText = true,
            });
        }
    }
}
