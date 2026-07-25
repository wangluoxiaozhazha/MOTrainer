namespace MOTrainer.Core.Profiles;

public sealed record ModuleFingerprint(string FileName, string Sha256)
{
    public bool Matches(string fileName, string sha256)
    {
        return string.Equals(FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeHash(Sha256), NormalizeHash(sha256), StringComparison.Ordinal);
    }

    private static string NormalizeHash(string value)
    {
        return value.Replace(" ", string.Empty)
            .ToUpperInvariant();
    }
}
