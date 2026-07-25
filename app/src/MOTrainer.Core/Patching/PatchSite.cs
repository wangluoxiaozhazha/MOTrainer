namespace MOTrainer.Core.Patching;

public sealed record PatchSite(
    string ModuleName,
    uint Rva,
    byte[] OriginalBytes,
    byte[] EnabledBytes)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModuleName))
        {
            throw new InvalidOperationException("Patch module name is required.");
        }

        if (OriginalBytes.Length == 0)
        {
            throw new InvalidOperationException("Patch bytes cannot be empty.");
        }

        if (OriginalBytes.Length != EnabledBytes.Length)
        {
            throw new InvalidOperationException("Original and enabled byte lengths must match.");
        }
    }
}
