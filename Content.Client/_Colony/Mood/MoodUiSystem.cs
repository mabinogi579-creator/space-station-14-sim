using Content.Shared._Colony.Mood;
using Content.Shared._Colony.Overseer;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Colony.Mood;

/// <summary>
/// Adds a "View mood" option to the right-click menu of anyone who has a mood, and opens the mood window.
/// </summary>
public sealed class MoodUiSystem : EntitySystem
{
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private IUserInterfaceManager _ui = default!;

    private MoodWindow? _window;
    private ColonistBar? _bar;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColonyMoodComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);

        // The colonist bar: a strip of portraits across the top of the screen.
        _bar = new ColonistBar(EntityManager, OpenWindow, Follow);
        LayoutContainer.SetAnchorAndMarginPreset(_bar, LayoutContainer.LayoutPreset.CenterTop, margin: 8);
        LayoutContainer.SetGrowHorizontal(_bar, LayoutContainer.GrowDirection.Both);
        _ui.WindowRoot.AddChild(_bar);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
        _bar?.Orphan();
        _bar = null;
    }

    private void OnGetVerbs(EntityUid uid, ColonyMoodComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        var target = args.Target;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = "View mood",
            // Low priority, so alt-clicking someone still does whatever it did before.
            Priority = -100,
            // The window only exists on this computer, so the server doesn't need to hear about it.
            ClientExclusive = true,
            Act = () => OpenWindow(target),
        });
    }

    private void Follow(EntityUid target)
    {
        _window?.Close();
        RaiseNetworkEvent(new ColonyFollowRequest(GetNetEntity(target)));
    }

    public void OpenWindow(EntityUid target)
    {
        _window?.Close();

        var window = new MoodWindow(target, EntityManager, _satiation, _timing);
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;
        };

        _window = window;
        window.OpenCentered();
    }
}
