using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Colony.Mood;

/// <summary>
/// First draft of the colony mood system. For now it only reads needs and logs a number.
/// </summary>
public sealed class MoodSystem : EntitySystem
{
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<SatiationTypePrototype> Rest = "Rest";
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextLog;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextLog)
            return;

        _nextLog = _timing.CurTime + LogInterval;

        var query = EntityQueryEnumerator<SatiationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var entity = new Entity<SatiationComponent>(uid, comp);

            if (_satiation.GetValueOrNull(entity, SatiationSystem.Hunger) is not { } hunger ||
                _satiation.GetMaximumValue(entity, SatiationSystem.Hunger) is not { } hungerMax ||
                _satiation.GetValueOrNull(entity, Rest) is not { } rest ||
                _satiation.GetMaximumValue(entity, Rest) is not { } restMax)
                continue;

            var mood = (hunger / hungerMax + rest / restMax) / 2f * 100f;

            Log.Info($"Mood of {ToPrettyString(uid)}: {mood:F0} (hunger {hunger:F0}/{hungerMax}, rest {rest:F0}/{restMax})");
        }
    }
}