using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._Colony.Diet;
using Content.Shared._Colony.Mood;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Colony.Diet;

/// <summary>
/// Sets the diet of colonists. Examples:
///   colonydiet list                  (shows the diets)
///   colonydiet Vegetarian            (every colonist)
///   colonydiet Vegetarian Damon      (only colonists with "Damon" in their name)
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ColonyDietCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public string Command => "colonydiet";
    public string Description => "Sets the diet of every colonist, or only those matching a name. 'colonydiet list' shows the diets.";
    public string Help => "colonydiet <diet|list> [part of name]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("Usage: " + Help);
            return;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var diet in _proto.EnumeratePrototypes<ColonyDietPrototype>())
                shell.WriteLine($"{diet.ID}: {diet.Description}");

            var query = _ent.EntityQueryEnumerator<ColonyMoodComponent, ColonyDietComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out _, out var current, out var meta))
                shell.WriteLine($"  {meta.EntityName} follows {current.Diet}");
            return;
        }

        if (!_proto.TryIndex<ColonyDietPrototype>(args[0], out var chosen))
        {
            shell.WriteError($"No diet called '{args[0]}'. Type 'colonydiet list' to see them.");
            return;
        }

        var filter = args.Length > 1 ? args[1] : null;
        var count = 0;
        var q = _ent.EntityQueryEnumerator<ColonyMoodComponent, MetaDataComponent>();
        while (q.MoveNext(out var uid, out _, out var meta))
        {
            if (filter != null && !meta.EntityName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            _ent.EnsureComponent<ColonyDietComponent>(uid).Diet = chosen.ID;
            shell.WriteLine($"{meta.EntityName} now follows {chosen.ID}");
            count++;
        }

        if (count == 0)
            shell.WriteLine("No matching colonists.");
    }
}
