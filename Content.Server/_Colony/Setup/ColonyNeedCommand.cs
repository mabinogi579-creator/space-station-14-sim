using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._Colony.Mood;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Colony.Setup;

/// <summary>
/// Sets a need on colonists without having to select them. Examples:
///   colonyneed Hunger 20          (every colonist)
///   colonyneed Hunger 20 Damon    (only colonists with "Damon" in their name)
///   colonyneed Rest 100 chef      (names are matched, so use part of a name)
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ColonyNeedCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ent = default!;

    public string Command => "colonyneed";
    public string Description => "Sets a need (Hunger, Thirst, Rest) on every colonist, or only those matching a name.";
    public string Help => "colonyneed <need> <value> [part of name]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: " + Help);
            return;
        }

        if (!float.TryParse(args[1], out var value))
        {
            shell.WriteError("The value must be a number, for example 20.");
            return;
        }

        ProtoId<SatiationTypePrototype> type = args[0];
        var filter = args.Length > 2 ? args[2] : null;
        var satiation = _ent.System<SatiationSystem>();
        var count = 0;

        var query = _ent.EntityQueryEnumerator<ColonyMoodComponent, SatiationComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var sat, out var meta))
        {
            if (filter != null && !meta.EntityName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (sat.GetOrNull(type) == null)
                continue;

            satiation.SetValue((uid, sat), type, value);
            shell.WriteLine($"{meta.EntityName}: {type} set to {value}");
            count++;
        }

        if (count == 0)
            shell.WriteLine("No matching colonists.");
    }
}
