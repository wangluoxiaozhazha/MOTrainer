namespace MOTrainer.Core.Patching;

public static class PatchByteClassifier
{
    public static PatchByteState Classify(
        PatchSite site,
        byte[] actualBytes)
    {
        site.Validate();

        if (actualBytes.SequenceEqual(site.OriginalBytes))
        {
            return PatchByteState.Original;
        }

        return actualBytes.SequenceEqual(site.EnabledBytes)
            ? PatchByteState.Enabled
            : PatchByteState.Unknown;
    }
}
