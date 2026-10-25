using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._Colony.Mood;
using Content.Shared._Colony.Traits;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Colony.Traits;

/// <summary>
/// Looks at and changes colonist traits. Examples:
///   colonytrait list                 (every trait, and who has what)
///   colonytrait add Brisk Damon      (give Brisk to colonists with "Damon" in their name)
///   colonytrait remove Brisk Damon
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ColonyTraitCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public string Command => "colonytrait";
    public string Description => "Shows or changes colonist traits.";
    public string Help => "colonytrait list | colonytrait <add|remove> <trait> [part of name]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var traits = _ent.System<ColonyTraitSystem>();

        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var trait in _proto.EnumeratePrototypes<ColonyTraitPrototype>())
                shell.WriteLine($"{trait.ID}: {trait.Description}");

            shell.WriteLine("");
            var query = _ent.EntityQueryEnumerator<ColonyMoodComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
                shell.WriteLine($"{meta.EntityName}: {traits.Describe(uid)}");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: " + Help);
            return;
        }

        var adding = args[0].Equals("add", StringComparison.OrdinalIgnoreCase);
        if (!adding && !args[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteError("Usage: " + Help);
            return;
        }

        var filter = args.Length > 2 ? args[2] : null;
        var count = 0;
        var q = _ent.EntityQueryEnumerator<ColonyMoodComponent, MetaDataComponent>();
        while (q.MoveNext(out var uid, out _, out var meta))
        {
            if (filter != null && !meta.EntityName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var changed = adding ? traits.Add(uid, args[1]) : traits.Remove(uid, args[1]);
            shell.WriteLine(changed
                ? $"{meta.EntityName}: now {traits.Describe(uid)}"
                : $"{meta.EntityName}: unchanged (already has it, clashes with a trait, or no such trait)");
            count++;
        }

        if (count == 0)
            shell.WriteLine("No matching colonists.");
    }
}
