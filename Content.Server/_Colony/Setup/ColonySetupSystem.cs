using Content.Server.GameTicking;
using Content.Shared._Colony.Setup;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Station.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Colony.Setup;

/// <summary>
/// Starts the round the colony way: instead of the joining player becoming a crew member,
/// the whole crew from the chosen scenario is created, and the player becomes a free-floating overseer.
/// </summary>
public sealed class ColonySetupSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private ServerGameTicker _ticker = default!;
    [Dependency] private IRobustRandom _random = default!;

    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private bool _crewSpawned;
    private EntityUid? _firstColonist;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _crewSpawned = false; _firstColonist = null; });
        Log.Info("ColonySetupSystem initialized");
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (ev.Station == EntityUid.Invalid)
        {
            Log.Error("No station to spawn the crew on; falling back to normal spawning.");
            return;
        }

        if (!_crewSpawned)
        {
            _crewSpawned = true;
            SpawnCrew(ev.Station);
        }

        // The player watches over the colony instead of being one of the crew.
        _ticker.SpawnObserver(ev.Player);

        // Drop the overseer in beside the crew so they are in view straight away.
        if (_firstColonist is { } first
            && ev.Player.AttachedEntity is { } overseer
            && Exists(first))
        {
            _transform.SetCoordinates(overseer, Transform(first).Coordinates);
        }
        ev.Handled = true;
    }

    private void SpawnCrew(EntityUid station)
    {
        var id = _cfg.GetCVar(ColonyCVars.Scenario);
        if (!ProtoMan.TryIndex<ColonyScenarioPrototype>(id, out var scenario))
        {
            Log.Error($"Unknown colony scenario '{id}'. Check scenarios.yml.");
            return;
        }

        Log.Info($"Starting scenario '{scenario.Name}' ({scenario.ID})");

        var crew = new List<EntityUid>();
        foreach (var (job, count) in scenario.Crew)
        {
            for (var i = 0; i < count; i++)
            {
                var profile = HumanoidCharacterProfile.RandomWithSpecies(HumanoidCharacterProfile.DefaultSpecies);
                var mob = _spawning.SpawnPlayerCharacterOnStation(station, job, profile);
                if (mob == null)
                {
                    Log.Warning($"Could not spawn a {job}.");
                    continue;
                }

                crew.Add(mob.Value);
                _firstColonist ??= mob.Value;

                // Always send colonists to the player, however far away the camera is.
                _pvs.AddGlobalOverride(mob.Value);
                Log.Info($"Spawned {ToPrettyString(mob.Value)} as {job}");
            }
        }

        _random.Shuffle(crew);
        for (var i = 0; i < scenario.Traitors && i < crew.Count; i++)
        {
            AddComp<ColonyTraitorComponent>(crew[i]);
            Log.Info($"{ToPrettyString(crew[i])} is secretly a traitor");
        }
    }
}
