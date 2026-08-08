using MOTrainer.Core.Memory;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Processes;

namespace MOTrainer.Core.Patching;

public sealed class PatchManager
{
    private readonly int processId;
    private readonly IMemoryAccessor memory;
    private readonly IModuleResolver modules;

    public PatchManager(
        int processId,
        IMemoryAccessor memory,
        IModuleResolver modules)
    {
        this.processId = processId;
        this.memory = memory;
        this.modules = modules;
    }

    public PatchInspection Inspect(PatchDefinition definition)
    {
        definition.Validate();
        List<PatchByteState> states = new(definition.Sites.Count);

        foreach (PatchSite site in definition.Sites)
        {
            if (!TryResolve(site, out uint address))
            {
                return new PatchInspection(
                    PatchRuntimeState.ModuleUnavailable,
                    CoreStrings.Format("ModuleUnavailable", site.ModuleName));
            }

            byte[] actual = new byte[site.OriginalBytes.Length];
            if (!memory.TryReadBytes(address, actual))
            {
                return new PatchInspection(
                    PatchRuntimeState.Unknown,
                    CoreStrings.Format("PatchSiteReadFailed", site.ModuleName, site.Rva));
            }

            states.Add(PatchByteClassifier.Classify(site, actual));
        }

        if (states.All(state => state == PatchByteState.Original))
        {
            return new PatchInspection(
                PatchRuntimeState.Original,
                CoreStrings.Get("PatchDisabled"));
        }

        if (states.All(state => state == PatchByteState.Enabled))
        {
            return new PatchInspection(
                PatchRuntimeState.Enabled,
                CoreStrings.Get("PatchEnabled"));
        }

        return new PatchInspection(
            PatchRuntimeState.Unknown,
            CoreStrings.Get("PatchBytesUnknown"));
    }

    public PatchTransitionResult SetEnabled(
        PatchDefinition definition,
        bool enabled)
    {
        PatchInspection inspection = Inspect(definition);
        PatchRuntimeState expected = enabled
            ? PatchRuntimeState.Original
            : PatchRuntimeState.Enabled;
        PatchRuntimeState desired = enabled
            ? PatchRuntimeState.Enabled
            : PatchRuntimeState.Original;

        if (inspection.State == desired)
        {
            return new PatchTransitionResult(true, desired, inspection.Message);
        }

        if (inspection.State != expected)
        {
            return new PatchTransitionResult(false, inspection.State, inspection.Message);
        }

        List<ResolvedPatchSite> sites = new(definition.Sites.Count);
        foreach (PatchSite site in definition.Sites)
        {
            if (!TryResolve(site, out uint address))
            {
                return new PatchTransitionResult(
                    false,
                    PatchRuntimeState.ModuleUnavailable,
                    CoreStrings.Format("ModuleUnavailable", site.ModuleName));
            }

            byte[] actual = new byte[site.OriginalBytes.Length];
            if (!memory.TryReadBytes(address, actual) ||
                PatchByteClassifier.Classify(site, actual) !=
                    (enabled ? PatchByteState.Original : PatchByteState.Enabled))
            {
                return new PatchTransitionResult(
                    false,
                    PatchRuntimeState.Unknown,
                    CoreStrings.Get("PatchBytesChanged"));
            }

            sites.Add(new ResolvedPatchSite(site, address, actual));
        }

        int changed = 0;
        foreach (ResolvedPatchSite resolved in sites)
        {
            byte[] target = enabled
                ? resolved.Site.EnabledBytes
                : resolved.Site.OriginalBytes;

            if (!memory.TryWriteCodeBytes(resolved.Address, target) ||
                !ReadbackMatches(resolved.Address, target))
            {
                bool restored = RestorePreviousBytes(sites, changed + 1);
                string suffix = restored
                    ? CoreStrings.Get("PatchRollbackSucceeded")
                    : CoreStrings.Get("PatchRollbackFailed");
                return new PatchTransitionResult(
                    false,
                    PatchRuntimeState.Unknown,
                    CoreStrings.Format("PatchWriteFailed", suffix));
            }

            changed++;
        }

        return new PatchTransitionResult(
            true,
            desired,
            enabled
                ? CoreStrings.Get("PatchEnabledResult")
                : CoreStrings.Get("PatchDisabledResult"));
    }

    private bool TryResolve(PatchSite site, out uint address)
    {
        address = 0;
        if (!modules.TryGetModuleBase(processId, site.ModuleName, out uint moduleBase))
        {
            return false;
        }

        try
        {
            address = checked(moduleBase + site.Rva);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private bool ReadbackMatches(uint address, byte[] expected)
    {
        byte[] actual = new byte[expected.Length];
        return memory.TryReadBytes(address, actual) &&
            actual.SequenceEqual(expected);
    }

    private bool RestorePreviousBytes(
        IReadOnlyList<ResolvedPatchSite> sites,
        int count)
    {
        bool success = true;
        for (int index = 0; index < count; index++)
        {
            ResolvedPatchSite site = sites[index];
            success &=
                memory.TryWriteCodeBytes(site.Address, site.PreviousBytes) &&
                ReadbackMatches(site.Address, site.PreviousBytes);
        }

        return success;
    }

    private sealed record ResolvedPatchSite(
        PatchSite Site,
        uint Address,
        byte[] PreviousBytes);
}
