using Content.Shared._Colony.Log;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Colony.Log;

/// <summary>
/// Puts the Colony Log panel on screen and feeds it the lines the server sends.
/// </summary>
public sealed class ColonyLogUiSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    private ColonyLogPanel? _panel;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ColonyLogEvent>(OnLog);

        _panel = new ColonyLogPanel();
        LayoutContainer.SetAnchorAndMarginPreset(_panel, LayoutContainer.LayoutPreset.BottomLeft, margin: 8);
        LayoutContainer.SetGrowVertical(_panel, LayoutContainer.GrowDirection.Begin);
        _ui.WindowRoot.AddChild(_panel);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _panel?.Orphan();
        _panel = null;
    }

    private void OnLog(ColonyLogEvent ev)
    {
        _panel?.AddLine(ev.Category, ev.Text, ev.Size, ev.Source);
    }

    /// <summary>Limit the log to one colonist (or pass null to see everyone).</summary>
    public void FocusOn(EntityUid? who)
    {
        if (who is not { } uid || !Exists(uid))
        {
            _panel?.FocusOn(null, null);
            return;
        }

        var first = Name(uid).Split(' ')[0];
        _panel?.FocusOn(GetNetEntity(uid), first);
    }
}
