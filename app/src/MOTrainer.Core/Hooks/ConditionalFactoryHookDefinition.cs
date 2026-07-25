namespace MOTrainer.Core.Hooks;

public sealed record ConditionalFactoryHookDefinition(
    string Id,
    string ModuleName,
    uint Rva,
    byte[] OriginalBytes,
    uint CurrentPlayerAddress,
    byte OwnerOffset,
    byte ProgressOffset,
    int CompletedProgress)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            string.IsNullOrWhiteSpace(ModuleName))
        {
            throw new InvalidOperationException("Hook id and module are required.");
        }

        if (OriginalBytes.Length < 5)
        {
            throw new InvalidOperationException(
                "Hook entry must cover at least one relative jump.");
        }

        if (CurrentPlayerAddress == 0)
        {
            throw new InvalidOperationException(
                "CurrentPlayer address is required.");
        }
    }
}
