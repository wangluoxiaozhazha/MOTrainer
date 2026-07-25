namespace MOTrainer.Core.Hooks;

public sealed record SelectionEditHookPolicy(
    float? Veterancy,
    int? Health)
{
    public void Validate()
    {
        if (Veterancy is null && Health is null)
        {
            throw new InvalidOperationException(
                "Selection event policy must edit at least one field.");
        }

        if (Veterancy is float veterancy &&
            veterancy is not (0.0f or 1.0f or 2.0f))
        {
            throw new InvalidOperationException(
                "Selection event veterancy must be 0.0, 1.0 or 2.0.");
        }

        if (Health is int health && health < 1)
        {
            throw new InvalidOperationException(
                "Selection event health must be positive.");
        }
    }
}
