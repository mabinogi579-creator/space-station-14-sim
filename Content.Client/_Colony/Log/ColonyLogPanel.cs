using System.Linq;
using System.Numerics;
using Content.Shared._Colony.Log;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Colony.Log;

/// <summary>
/// A box in the bottom left of the screen listing recent events, like the SS13 chat window:
/// speech, actions, sounds, needs and news, each in its own colour. Whispers are small, shouting is big,
/// and a line that repeats within a few seconds gets a (x2), (x3)... counter instead of filling the box.
/// </summary>
public sealed class ColonyLogPanel : PanelContainer
{
    private const int KeptLines = 300;
    private const int ShownLines = 120; // older lines are still kept; scroll up to read them
    private const float ExpandedHeight = 260f;
    private const float CollapsedHeight = 40f;
    private const float PanelWidth = 600f;
    private const float EdgeMargin = 8f;
    private const float RepeatWindow = 8f; // seconds

    private static readonly Dictionary<ColonyLogCategory, string> Colors = new()
    {
        { ColonyLogCategory.Speech, "#f2f2f2" },
        { ColonyLogCategory.Action, "#8cc7ff" },
        { ColonyLogCategory.Need, "#f0a860" },
        { ColonyLogCategory.Announcement, "#ffe066" },
        { ColonyLogCategory.Sound, "#b8a2d9" },
    };

    private sealed class Entry
    {
        public ColonyLogCategory Category;
        public string Text = string.Empty;
        public ColonyLogSize Size;
        public NetEntity? Source;
        public int Count = 1;
        public float LastTime;
    }

    private readonly List<Entry> _lines = new();
    private readonly HashSet<ColonyLogCategory> _hidden = new();
    private readonly BoxContainer _body;
    private readonly ScrollContainer _scroll;
    private readonly Button _collapse;
    private readonly Button _focusButton;
    private NetEntity? _focus;
    private readonly BoxContainer _filters;
    private bool _collapsed;
    private int _unseen;
    private bool _stickToBottom = true;
    private int _scrollSettleFrames;
    private float _sinceRebuild;
    private float _clock;
    private bool _dirty;

    public ColonyLogPanel()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101018d0") };
        SetSize = new Vector2(PanelWidth, ExpandedHeight);

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6),
        };
        AddChild(column);

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _collapse = new Button { Text = "Colony log  [-]" };
        _collapse.OnPressed += _ => SetCollapsed(!_collapsed);
        header.AddChild(_collapse);

        _filters = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        AddFilter(_filters, "Speech", ColonyLogCategory.Speech);
        AddFilter(_filters, "Actions", ColonyLogCategory.Action);
        AddFilter(_filters, "Sounds", ColonyLogCategory.Sound);
        AddFilter(_filters, "Needs", ColonyLogCategory.Need);
        AddFilter(_filters, "News", ColonyLogCategory.Announcement);
        header.AddChild(_filters);

        // Shows who the log is limited to (click it to see everyone again).
        _focusButton = new Button { Visible = false };
        _focusButton.OnPressed += _ => FocusOn(null, null);
        header.AddChild(_focusButton);
        column.AddChild(header);

        _body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReturnMeasure = false };
        _scroll.AddChild(_body);
        column.AddChild(_scroll);
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

            _dirty = true;
        };
        parent.AddChild(button);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _clock += args.DeltaSeconds;
        _sinceRebuild += args.DeltaSeconds;

        // Redraw a few times a second at most, however many lines arrived.
        if (_dirty && !_collapsed && _sinceRebuild >= 0.2f)
        {
            _dirty = false;
            _sinceRebuild = 0f;

            // If the reader was at the bottom, keep following new lines. If they scrolled up, leave them be.
            _stickToBottom = _scroll.GetScrollValue().Y + _scroll.Size.Y >= _body.Size.Y - 6f;
            Rebuild();
            _scrollSettleFrames = 3;
        }

        // The new lines only have a size after the next layout pass, so scroll a moment later.
        if (_scrollSettleFrames > 0)
        {
            _scrollSettleFrames--;
            if (_stickToBottom)
                _scroll.SetScrollValue(new Vector2(0, 1_000_000f));
        }
    }

    private void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        _scroll.Visible = !collapsed;
        _filters.Visible = !collapsed;
        var height = collapsed ? CollapsedHeight : ExpandedHeight;
        SetSize = new Vector2(PanelWidth, height);

        // The panel is pinned to the bottom of the screen, so move its top edge, not just its size.
        LayoutContainer.SetMarginTop(this, -(height + EdgeMargin));
        LayoutContainer.SetMarginBottom(this, -EdgeMargin);
        if (!collapsed)
        {
            _unseen = 0;
            _dirty = true;
            _stickToBottom = true;
        }

        UpdateTitle();
    }

    private void UpdateTitle()
    {
        _collapse.Text = _collapsed
            ? (_unseen > 0 ? $"Colony log  [+]  ({_unseen} new)" : "Colony log  [+]")
            : "Colony log  [-]";
    }

    /// <summary>Only show lines about this colonist. Pass null to show everyone again.</summary>
    public void FocusOn(NetEntity? who, string? name)
    {
        _focus = who;
        _focusButton.Visible = who != null;
        _focusButton.Text = $"Only: {name}  [x]";
        _dirty = true;
        _stickToBottom = true;
    }

    public void AddLine(ColonyLogCategory category, string text, ColonyLogSize size, NetEntity? source = null)
    {
        // Same thing happening again right away: bump the counter instead of adding a new line.
        var last = _lines.LastOrDefault(l => l.Category == category && l.Text == text && l.Source == source);
        if (last != null && _clock - last.LastTime < RepeatWindow && _lines[^1] == last)
        {
            last.Count++;
            last.LastTime = _clock;
            _dirty = true;
            return;
        }

        _lines.Add(new Entry { Category = category, Text = text, Size = size, Source = source, LastTime = _clock });
        if (_lines.Count > KeptLines)
            _lines.RemoveAt(0);

        _dirty = true;
        if (_collapsed)
        {
            _unseen++;
            UpdateTitle();
        }
    }

    private void Rebuild()
    {
        _body.RemoveAllChildren();

        var visible = _lines.Where(l => !_hidden.Contains(l.Category) && (_focus == null || l.Source == _focus)).ToList();
        foreach (var line in visible.Skip(Math.Max(0, visible.Count - ShownLines)))
        {
            var size = line.Size switch
            {
                ColonyLogSize.Small => 10,
                ColonyLogSize.Big => 16,
                _ => 12,
            };

            var text = FormattedMessage.EscapeText(line.Text);
            if (line.Count > 1)
                text += $" (x{line.Count})";

            // Sounds are italic, shouting is bold.
            if (line.Category == ColonyLogCategory.Sound)
                text = $"[italic]{text}[/italic]";
            if (line.Size == ColonyLogSize.Big)
                text = $"[bold]{text}[/bold]";

            var label = new RichTextLabel();
            label.SetMessage(FormattedMessage.FromMarkupOrThrow($"[color={Colors[line.Category]}][font size={size}]{text}[/font][/color]"));
            _body.AddChild(label);
        }
    }
}
