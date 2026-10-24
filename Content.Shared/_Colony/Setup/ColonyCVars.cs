using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Colony.Setup;

[CVarDefs]
public sealed class ColonyCVars : CVars
{
    /// <summary>Which scenario (see scenarios.yml) the round starts with.</summary>
    public static readonly CVarDef<string> Scenario =
        CVarDef.Create("colony.scenario", "SmallCrew", CVar.SERVERONLY);
}
