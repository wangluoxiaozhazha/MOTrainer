namespace MOTrainer.Core.Actions;

public enum SelectedUnitEditScope
{
    FirstSelected,
    AllCurrentPlayer,
}

public enum UnitVeterancyLevel
{
    Rookie,
    Veteran,
    Elite,
}

public sealed record SelectedUnitEditRequest(
    SelectedUnitEditScope Scope,
    UnitVeterancyLevel? Veterancy,
    int? Health);
