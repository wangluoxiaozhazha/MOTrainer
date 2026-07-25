namespace MOTrainer.Core.Hooks;

public sealed record SelectionHookSite(
    uint Rva,
    byte[] OriginalBytes)
{
    public void Validate()
    {
        if (OriginalBytes.Length < 5)
        {
            throw new InvalidOperationException(
                "Selection hook site must cover a relative jump.");
        }
    }
}

public sealed record SelectionVeterancyHookDefinition(
    string Id,
    string ModuleName,
    IReadOnlyList<SelectionHookSite> Sites,
    uint CurrentPlayerAddress,
    uint OwnerOffset,
    uint VeterancyOffset,
    uint HealthOffset)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            string.IsNullOrWhiteSpace(ModuleName))
        {
            throw new InvalidOperationException(
                "Selection hook id and module are required.");
        }

        if (Sites.Count != 2)
        {
            throw new InvalidOperationException(
                "Object selection hook requires exactly two paths.");
        }

        foreach (SelectionHookSite site in Sites)
        {
            site.Validate();
        }

        if (Sites.Select(site => site.Rva).Distinct().Count() !=
            Sites.Count)
        {
            throw new InvalidOperationException(
                "Selection hook paths must use distinct RVAs.");
        }

        if (CurrentPlayerAddress == 0 ||
            OwnerOffset == 0 ||
            VeterancyOffset == 0 ||
            HealthOffset == 0)
        {
            throw new InvalidOperationException(
                "Selection hook layout is incomplete.");
        }
    }
}
