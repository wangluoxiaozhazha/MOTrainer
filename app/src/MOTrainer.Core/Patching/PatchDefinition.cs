namespace MOTrainer.Core.Patching;

public sealed record PatchDefinition(string Id, IReadOnlyList<PatchSite> Sites)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("Patch id is required.");
        }

        if (Sites.Count == 0)
        {
            throw new InvalidOperationException("At least one patch site is required.");
        }

        foreach (PatchSite site in Sites)
        {
            site.Validate();
        }
    }
}
