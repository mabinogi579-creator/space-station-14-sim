using Content.Shared._Colony.Mood;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Colony.Mood;

/// <summary>
/// Turns needs into thoughts, and thoughts into a single mood number.
/// </summary>
public sealed class MoodSystem : EntitySystem
{
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IngestionSystem _ingestion = default!;

    private static readonly ProtoId<SatiationTypePrototype> Rest = "Rest";
    private static readonly ProtoId<ThoughtPrototype> AteMeal = "ThoughtAteMeal";
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // The last thing we logged for each character, so we only log when something changes.
    private readonly Dictionary<EntityUid, string> _lastLogged = new();

    private TimeSpan _nextUpdate;
    private TimeSpan _nextHeartbeat;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColonyMoodComponent, IngestingEvent>(OnIngesting);
        Log.Info("MoodSystem initialized");
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var heartbeat = _timing.CurTime >= _nextHeartbeat;
        if (heartbeat)
            _nextHeartbeat = _timing.CurTime + HeartbeatInterval;

        var colonists = 0;

        var query = EntityQueryEnumerator<ColonyMoodComponent, SatiationComponent>();
        while (query.MoveNext(out var uid, out var mood, out var satiations))
        {
            colonists++;
            var entity = new Entity<SatiationComponent>(uid, satiations);

            RefreshNeedThoughts(entity, mood);
            ExpireEventThoughts(mood);
            mood.Mood = CalculateMood(mood);

            LogIfChanged(uid, entity, mood);
        }

        if (heartbeat)
        {
            var tables = 0;
            foreach (var _ in ProtoMan.EnumeratePrototypes<NeedThoughtsPrototype>())
            {
                tables++;
            }

            Log.Info($"MoodSystem heartbeat: {colonists} characters with mood, {tables} need-thought tables loaded");
        }
    }

    /// <summary>
    /// Runs every time a character takes a bite or a sip. Solid food gives the "ate a meal" thought; drinks do not.
    /// Eating again before it expires just restarts the timer, so thoughts never stack up.
    /// </summary>
    private void OnIngesting(Entity<ColonyMoodComponent> entity, ref IngestingEvent args)
    {
        var type = _ingestion.GetEdibleType(new Entity<EdibleComponent?>(args.Food, null));
        if (type != IngestionSystem.Food)
            return;

        AddThought(entity.AsNullable(), AteMeal);
    }

    /// <summary>
    /// Adds a timed thought caused by an event. Adding the same thought again restarts its timer.
    /// </summary>
    public void AddThought(Entity<ColonyMoodComponent?> entity, ProtoId<ThoughtPrototype> thought)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        if (!ProtoMan.Resolve(thought, out var proto))
            return;

        entity.Comp.EventThoughts[thought] = _timing.CurTime + proto.Duration;
    }

    private void RefreshNeedThoughts(Entity<SatiationComponent> entity, ColonyMoodComponent mood)
    {
        mood.NeedThoughts.Clear();

        foreach (var table in ProtoMan.EnumeratePrototypes<NeedThoughtsPrototype>())
        {
            if (_satiation.TryGetValueByThreshold(entity, table.Need, table.Thoughts, out var thought, out _, out _))
                mood.NeedThoughts.Add(thought);
        }
    }

    private void ExpireEventThoughts(ColonyMoodComponent mood)
    {
        if (mood.EventThoughts.Count == 0)
            return;

        var expired = new List<ProtoId<ThoughtPrototype>>();
        foreach (var (id, expiry) in mood.EventThoughts)
        {
            if (_timing.CurTime >= expiry)
                expired.Add(id);
        }

        foreach (var id in expired)
        {
            mood.EventThoughts.Remove(id);
        }
    }

    private float CalculateMood(ColonyMoodComponent mood)
    {
        var total = mood.BaseMood;

        foreach (var id in mood.NeedThoughts)
        {
            if (ProtoMan.Resolve(id, out var thought))
                total += thought.MoodEffect;
        }

        foreach (var id in mood.EventThoughts.Keys)
        {
            if (ProtoMan.Resolve(id, out var thought))
                total += thought.MoodEffect;
        }

        return Math.Clamp(total, 0f, 100f);
    }

    /// <summary>
    /// Prints one line whenever a character's mood or thoughts change. Needs drain constantly, so their
    /// raw values are shown in the line but are not what triggers it.
    /// </summary>
    private void LogIfChanged(EntityUid uid, Entity<SatiationComponent> entity, ColonyMoodComponent mood)
    {
        var labels = new List<string>();

        foreach (var id in mood.NeedThoughts)
        {
            if (ProtoMan.Resolve(id, out var thought))
                labels.Add($"{thought.Label} ({thought.MoodEffect:+0;-0})");
        }

        foreach (var id in mood.EventThoughts.Keys)
        {
            if (ProtoMan.Resolve(id, out var thought))
                labels.Add($"{thought.Label} ({thought.MoodEffect:+0;-0})");
        }

        var list = labels.Count == 0 ? "none" : string.Join(", ", labels);
        var signature = $"{mood.Mood:F0}|{list}";

        if (_lastLogged.TryGetValue(uid, out var last) && last == signature)
            return;

        _lastLogged[uid] = signature;

        var hunger = _satiation.GetValueOrNull(entity, SatiationSystem.Hunger);
        var rest = _satiation.GetValueOrNull(entity, Rest);
        var hungerText = hunger is { } h ? h.ToString("F0") : "none";
        var restText = rest is { } r ? r.ToString("F0") : "none";

        Log.Info($"Mood of {ToPrettyString(uid)}: {mood.Mood:F0} | thoughts: {list} | hunger {hungerText}, rest {restText}");
    }
}
