namespace MOTrainer.Core.Hooks;

public sealed record PlayerTechUnlockHookDefinition(
    string Id,
    string ModuleName,
    uint Rva,
    byte[] OriginalBytes,
    uint CurrentPlayerAddress,
    uint RecheckTechTreeOffset,
    uint OverriddenReturnRva,
    uint FactoryOwnerRva,
    byte[] FactoryOwnerOriginalBytes,
    uint FactoryObjectOwnerOffset,
    uint FactoryOwnerAllowedRva,
    uint FactoryOwnerRejectedRva)
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

        if (FactoryOwnerOriginalBytes.Length < 5)
        {
            throw new InvalidOperationException(
                "Factory-owner Hook entry must cover at least one relative jump.");
        }

        if (CurrentPlayerAddress == 0 ||
            RecheckTechTreeOffset == 0 ||
            OverriddenReturnRva == 0 ||
            FactoryOwnerRva == 0 ||
            FactoryObjectOwnerOffset == 0 ||
            FactoryOwnerAllowedRva == 0 ||
            FactoryOwnerRejectedRva == 0)
        {
            throw new InvalidOperationException(
                "CurrentPlayer, recheck and overridden return addresses are required.");
        }
    }
}
