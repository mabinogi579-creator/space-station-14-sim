using Robust.Shared.Prototypes;

namespace Content.Shared._Colony.Diet;

/// <summary>
/// A diet colonists can follow. Foods are matched by keywords found in the item's prototype name
/// (for example "Meat" matches FoodMeat, FoodMeatBurger, ...). Add new ones in Resources/Prototypes/_Colony/Diet/diets.yml.
/// </summary>
[Prototype]
public sealed partial class ColonyDietPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>Foods whose name contains any of these words are never eaten.</summary>
    [DataField]
    public List<string> Forbidden = new();

    /// <summary>Foods whose name contains any of these words are chosen first (the colonist walks farther for them).</summary>
    [DataField]
    public List<string> Favorites = new();

    /// <summary>Hats, gloves and other clothing count as food (moths).</summary>
    [DataField]
    public bool EatClothing;

    /// <summary>Paper, wrappers, crayons and other junk count as food.</summary>
    [DataField]
    public bool EatJunk;
}
