using MOTrainer.Core.Memory;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Processes;

namespace MOTrainer.Core.Hooks;

public sealed class PlayerTechUnlockCompositeHook
{
    private const int RemotePageSize = 128;
    private const uint FactoryStubOffset = 64;
    private readonly int processId;
    private readonly IMemoryAccessor memory;
    private readonly IRemoteCodeMemory remoteMemory;
    private readonly IModuleResolver modules;

    public PlayerTechUnlockCompositeHook(
        int processId,
        IMemoryAccessor memory,
        IModuleResolver modules)
    {
        this.processId = processId;
        this.memory = memory;
        remoteMemory = memory as IRemoteCodeMemory ??
            throw new InvalidOperationException(
                CoreStrings.Get("RemoteCodePagesUnsupported"));
        this.modules = modules;
    }

    public PatchInspection Inspect(
        PlayerTechUnlockHookDefinition definition)
    {
        HookInspection inspection = InspectDetailed(definition);
        return new PatchInspection(inspection.State, inspection.Message);
    }

    public TechUnlockModeInspection InspectMode(
        PlayerTechUnlockHookDefinition definition)
    {
        HookInspection inspection = InspectDetailed(definition);
        return new TechUnlockModeInspection(
            inspection.Mode,
            inspection.State,
            inspection.Message);
    }

    public PatchTransitionResult SetEnabled(
        PlayerTechUnlockHookDefinition definition,
        bool enabled)
    {
        return SetMode(
            definition,
            enabled ? TechUnlockMode.Extended : TechUnlockMode.Off);
    }

    public PatchTransitionResult SetMode(
        PlayerTechUnlockHookDefinition definition,
        TechUnlockMode mode)
    {
        HookInspection inspection = InspectDetailed(definition);
        if (inspection.Mode == mode)
        {
            return new PatchTransitionResult(
                true,
                inspection.State,
                inspection.Message);
        }

        if (inspection.Mode is null)
        {
            return new PatchTransitionResult(
                false,
                inspection.State,
                inspection.Message);
        }

        return (inspection.Mode.Value, mode) switch
        {
            (TechUnlockMode.Off, TechUnlockMode.Faction) =>
                Enable(definition, inspection, TechUnlockMode.Faction),
            (TechUnlockMode.Off, TechUnlockMode.Extended) =>
                Enable(definition, inspection, TechUnlockMode.Extended),
            (TechUnlockMode.Faction, TechUnlockMode.Extended) =>
                EnableFactoryOwnerBypass(definition, inspection),
            (TechUnlockMode.Extended, TechUnlockMode.Faction) =>
                DisableFactoryOwnerBypass(definition, inspection),
            (TechUnlockMode.Faction, TechUnlockMode.Off) =>
                Disable(definition, inspection),
            (TechUnlockMode.Extended, TechUnlockMode.Off) =>
                Disable(definition, inspection),
            _ => new PatchTransitionResult(
                false,
                PatchRuntimeState.Unknown,
                CoreStrings.Format(
                    "TechUnlockModeChangeUnsupported",
                    inspection.Mode,
                    mode)),
        };
    }

    private HookInspection InspectDetailed(
        PlayerTechUnlockHookDefinition definition)
    {
        definition.Validate();
        if (!TryResolveEntries(
                definition,
                out uint moduleBase,
                out uint requirementEntry,
                out uint factoryEntry))
        {
            return Unknown(
                PatchRuntimeState.ModuleUnavailable,
                CoreStrings.Format("ModuleUnavailable", definition.ModuleName));
        }

        byte[] requirementBytes =
            new byte[definition.OriginalBytes.Length];
        byte[] factoryBytes =
            new byte[definition.FactoryOwnerOriginalBytes.Length];
        if (!memory.TryReadBytes(requirementEntry, requirementBytes) ||
            !memory.TryReadBytes(factoryEntry, factoryBytes))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("TechUnlockEntriesReadFailed"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                0,
                null);
        }

        bool requirementOriginal =
            requirementBytes.SequenceEqual(definition.OriginalBytes);
        bool factoryOriginal =
            factoryBytes.SequenceEqual(
                definition.FactoryOwnerOriginalBytes);
        if (requirementOriginal && factoryOriginal)
        {
            return new HookInspection(
                PatchRuntimeState.Original,
                CoreStrings.Get("TechUnlockDisabled"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                0,
                TechUnlockMode.Off);
        }

        if (requirementOriginal ||
            !TryDecodeEntry(
                requirementEntry,
                requirementBytes,
                out uint remoteAddress))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("TechUnlockStateInconsistent"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                0,
                null);
        }

        byte[] requirementStub = BuildRequirementStub(
            definition,
            moduleBase,
            requirementEntry,
            remoteAddress);
        byte[] factoryStub = BuildFactoryStub(
            definition,
            moduleBase,
            remoteAddress + FactoryStubOffset);
        if (!ReadbackMatches(remoteAddress, requirementStub) ||
            !ReadbackMatches(
                remoteAddress + FactoryStubOffset,
                factoryStub))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("TechUnlockUnknownTarget"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                remoteAddress,
                null);
        }

        if (factoryOriginal)
        {
            return new HookInspection(
                PatchRuntimeState.Enabled,
                CoreStrings.Get("TechUnlockFactionModeEnabled"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                remoteAddress,
                TechUnlockMode.Faction);
        }

        if (!TryDecodeEntry(
                factoryEntry,
                factoryBytes,
                out uint factoryRemoteAddress) ||
            factoryRemoteAddress != remoteAddress + FactoryStubOffset)
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("TechUnlockFactoryUnknownTarget"),
                moduleBase,
                requirementEntry,
                factoryEntry,
                remoteAddress,
                null);
        }

        return new HookInspection(
            PatchRuntimeState.Enabled,
            CoreStrings.Get("TechUnlockExtendedModeEnabled"),
            moduleBase,
            requirementEntry,
            factoryEntry,
            remoteAddress,
            TechUnlockMode.Extended);
    }

    private PatchTransitionResult Enable(
        PlayerTechUnlockHookDefinition definition,
        HookInspection inspection,
        TechUnlockMode mode)
    {
        if (!remoteMemory.TryAllocateRemotePage(
                RemotePageSize,
                out uint remoteAddress))
        {
            return Fail(CoreStrings.Get("TechUnlockAllocationFailed"));
        }

        byte[] requirementStub = BuildRequirementStub(
            definition,
            inspection.ModuleBase,
            inspection.RequirementEntry,
            remoteAddress);
        byte[] factoryStub = BuildFactoryStub(
            definition,
            inspection.ModuleBase,
            remoteAddress + FactoryStubOffset);
        byte[] page = new byte[RemotePageSize];
        Array.Copy(
            requirementStub,
            0,
            page,
            0,
            requirementStub.Length);
        Array.Copy(
            factoryStub,
            0,
            page,
            FactoryStubOffset,
            factoryStub.Length);

        byte[] requirementEntry = BuildEntry(
            inspection.RequirementEntry,
            remoteAddress,
            definition.OriginalBytes.Length);
        byte[] factoryEntry = BuildEntry(
            inspection.FactoryEntry,
            remoteAddress + FactoryStubOffset,
            definition.FactoryOwnerOriginalBytes.Length);

        bool prepared =
            memory.TryWriteBytes(remoteAddress, page) &&
            ReadbackMatches(remoteAddress, page) &&
            remoteMemory.TryMakeRemotePageExecutable(
                remoteAddress,
                RemotePageSize);
        if (!prepared)
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("TechUnlockRemotePageFailed"));
        }

        if (!ReadbackMatches(
                inspection.RequirementEntry,
                definition.OriginalBytes) ||
            !ReadbackMatches(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes))
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("TechUnlockEntryChanged"));
        }

        bool requirementWritten =
            memory.TryWriteCodeBytes(
                inspection.RequirementEntry,
                requirementEntry) &&
            ReadbackMatches(
                inspection.RequirementEntry,
                requirementEntry);
        bool factoryWritten =
            mode != TechUnlockMode.Extended ||
            (requirementWritten &&
            memory.TryWriteCodeBytes(
                inspection.FactoryEntry,
                factoryEntry) &&
            ReadbackMatches(
                inspection.FactoryEntry,
                factoryEntry));
        if (!requirementWritten || !factoryWritten)
        {
            RestoreOriginalEntries(definition, inspection);
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("TechUnlockWriteFailed"));
        }

        if (!TryRequestTechTreeRecheck(definition))
        {
            bool restored =
                RestoreOriginalEntries(definition, inspection);
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(restored
                ? CoreStrings.Get("TechUnlockRecheckFailedRolledBack")
                : CoreStrings.Get("TechUnlockRecheckAndRollbackFailed"));
        }

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            mode == TechUnlockMode.Faction
                ? CoreStrings.Get("TechUnlockFactionModeEnabledRebuildRequested")
                : CoreStrings.Get("TechUnlockExtendedModeEnabledRebuildRequested"));
    }

    private PatchTransitionResult EnableFactoryOwnerBypass(
        PlayerTechUnlockHookDefinition definition,
        HookInspection inspection)
    {
        byte[] factoryEntry = BuildEntry(
            inspection.FactoryEntry,
            inspection.RemoteAddress + FactoryStubOffset,
            definition.FactoryOwnerOriginalBytes.Length);
        if (!ReadbackMatches(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes) ||
            !memory.TryWriteCodeBytes(
                inspection.FactoryEntry,
                factoryEntry) ||
            !ReadbackMatches(
                inspection.FactoryEntry,
                factoryEntry))
        {
            return Fail(CoreStrings.Get("TechUnlockExtendedModeWriteFailed"));
        }

        bool recheckRequested = TryRequestTechTreeRecheck(definition);
        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            recheckRequested
                ? CoreStrings.Get("TechUnlockChangedToExtendedMode")
                : CoreStrings.Get("TechUnlockChangedToExtendedModeRecheckFailed"));
    }

    private PatchTransitionResult DisableFactoryOwnerBypass(
        PlayerTechUnlockHookDefinition definition,
        HookInspection inspection)
    {
        byte[] factoryEnabled = BuildEntry(
            inspection.FactoryEntry,
            inspection.RemoteAddress + FactoryStubOffset,
            definition.FactoryOwnerOriginalBytes.Length);
        if (!ReadbackMatches(inspection.FactoryEntry, factoryEnabled) ||
            !memory.TryWriteCodeBytes(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes) ||
            !ReadbackMatches(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes))
        {
            return Fail(CoreStrings.Get("TechUnlockFactionModeRestoreFailed"));
        }

        bool recheckRequested = TryRequestTechTreeRecheck(definition);
        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            recheckRequested
                ? CoreStrings.Get("TechUnlockChangedToFactionMode")
                : CoreStrings.Get("TechUnlockChangedToFactionModeRecheckFailed"));
    }

    private PatchTransitionResult Disable(
        PlayerTechUnlockHookDefinition definition,
        HookInspection inspection)
    {
        byte[] requirementEnabled = BuildEntry(
            inspection.RequirementEntry,
            inspection.RemoteAddress,
            definition.OriginalBytes.Length);
        if (!ReadbackMatches(
                inspection.RequirementEntry,
                requirementEnabled))
        {
            return Fail(CoreStrings.Get("TechUnlockDisableEntryChanged"));
        }

        byte[]? factoryEnabled = inspection.Mode == TechUnlockMode.Extended
            ? BuildEntry(
                inspection.FactoryEntry,
                inspection.RemoteAddress + FactoryStubOffset,
                definition.FactoryOwnerOriginalBytes.Length)
            : null;
        if (factoryEnabled is not null &&
            !ReadbackMatches(inspection.FactoryEntry, factoryEnabled))
        {
            return Fail(CoreStrings.Get("TechUnlockDisableFactoryChanged"));
        }

        bool requirementRestored =
            memory.TryWriteCodeBytes(
                inspection.RequirementEntry,
                definition.OriginalBytes) &&
            ReadbackMatches(
                inspection.RequirementEntry,
                definition.OriginalBytes);
        if (!requirementRestored)
        {
            return Fail(CoreStrings.Get("TechUnlockPrimaryRestoreFailed"));
        }

        bool factoryRestored =
            factoryEnabled is null ||
            (memory.TryWriteCodeBytes(
                    inspection.FactoryEntry,
                    definition.FactoryOwnerOriginalBytes) &&
                ReadbackMatches(
                    inspection.FactoryEntry,
                    definition.FactoryOwnerOriginalBytes));
        if (!factoryRestored)
        {
            memory.TryWriteCodeBytes(
                inspection.RequirementEntry,
                requirementEnabled);
            return Fail(CoreStrings.Get("TechUnlockFactoryRestoreFailed"));
        }

        bool recheckRequested =
            TryRequestTechTreeRecheck(definition);
        bool pageFreed =
            remoteMemory.TryFreeRemotePage(inspection.RemoteAddress);
        string message = (recheckRequested, pageFreed) switch
        {
            (true, true) =>
                CoreStrings.Get("TechUnlockDisabledAndRestored"),
            (false, true) =>
                CoreStrings.Get("TechUnlockDisabledRecheckFailed"),
            (true, false) =>
                CoreStrings.Get("TechUnlockPageReleaseFailed"),
            _ =>
                CoreStrings.Get("TechUnlockRecheckAndReleaseFailed"),
        };

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Original,
            message);
    }

    private bool RestoreOriginalEntries(
        PlayerTechUnlockHookDefinition definition,
        HookInspection inspection)
    {
        bool requirement =
            memory.TryWriteCodeBytes(
                inspection.RequirementEntry,
                definition.OriginalBytes) &&
            ReadbackMatches(
                inspection.RequirementEntry,
                definition.OriginalBytes);
        bool factory =
            memory.TryWriteCodeBytes(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes) &&
            ReadbackMatches(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes);
        return requirement && factory;
    }

    private bool TryResolveEntries(
        PlayerTechUnlockHookDefinition definition,
        out uint moduleBase,
        out uint requirementEntry,
        out uint factoryEntry)
    {
        requirementEntry = 0;
        factoryEntry = 0;
        if (!modules.TryGetModuleBase(
                processId,
                definition.ModuleName,
                out moduleBase))
        {
            return false;
        }

        try
        {
            requirementEntry = checked(moduleBase + definition.Rva);
            factoryEntry = checked(
                moduleBase + definition.FactoryOwnerRva);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static byte[] BuildRequirementStub(
        PlayerTechUnlockHookDefinition definition,
        uint moduleBase,
        uint entryAddress,
        uint remoteAddress)
    {
        List<byte> bytes =
        [
            0x3B, 0x15,
        ];
        bytes.AddRange(BitConverter.GetBytes(definition.CurrentPlayerAddress));
        bytes.AddRange(
        [
            0x75, 0x05,
            0xE9,
        ]);
        uint overriddenJump =
            remoteAddress + (uint)bytes.Count - 1;
        bytes.AddRange(GetRelativeOffset(
            overriddenJump,
            checked(moduleBase + definition.OverriddenReturnRva)));

        bytes.AddRange(definition.OriginalBytes);
        bytes.Add(0xE9);
        uint returnJump =
            remoteAddress + (uint)bytes.Count - 1;
        bytes.AddRange(GetRelativeOffset(
            returnJump,
            checked(entryAddress + (uint)definition.OriginalBytes.Length)));
        return bytes.ToArray();
    }

    private static byte[] BuildFactoryStub(
        PlayerTechUnlockHookDefinition definition,
        uint moduleBase,
        uint remoteAddress)
    {
        // At this point ESI points to the current House.Buildings vector item,
        // while EDI has already been replaced by BuildingTypeClass*. Resolve
        // the actual house through [ESI] -> BuildingClass.Owner and preserve
        // EAX. CurrentPlayer bypasses only the owner-bit intersection.
        List<byte> bytes =
        [
            0x50,
            0x8B, 0x06,
            0x8B, 0x80,
        ];
        bytes.AddRange(
            BitConverter.GetBytes(definition.FactoryObjectOwnerOffset));
        bytes.AddRange(
        [
            0x3B, 0x05,
        ]);
        bytes.AddRange(BitConverter.GetBytes(definition.CurrentPlayerAddress));
        bytes.AddRange(
        [
            0x58,
            0x74, 0x0A,
            0x85, 0x44, 0x24, 0x10,
            0x0F, 0x84,
        ]);
        uint rejectedJump =
            remoteAddress + (uint)bytes.Count - 2;
        bytes.AddRange(GetRelativeOffset(
            rejectedJump,
            6,
            checked(moduleBase + definition.FactoryOwnerRejectedRva)));

        bytes.Add(0xE9);
        uint allowedJump =
            remoteAddress + (uint)bytes.Count - 1;
        bytes.AddRange(GetRelativeOffset(
            allowedJump,
            checked(moduleBase + definition.FactoryOwnerAllowedRva)));
        return bytes.ToArray();
    }

    private static byte[] BuildEntry(
        uint entryAddress,
        uint remoteAddress,
        int length)
    {
        byte[] entry = Enumerable.Repeat((byte)0x90, length).ToArray();
        entry[0] = 0xE9;
        byte[] relative =
            GetRelativeOffset(entryAddress, remoteAddress);
        Array.Copy(relative, 0, entry, 1, relative.Length);
        return entry;
    }

    private static bool TryDecodeEntry(
        uint entryAddress,
        byte[] entry,
        out uint remoteAddress)
    {
        remoteAddress = 0;
        if (entry.Length < 5 ||
            entry[0] != 0xE9 ||
            entry.Skip(5).Any(value => value != 0x90))
        {
            return false;
        }

        int relative = BitConverter.ToInt32(entry, 1);
        long target = (long)entryAddress + 5 + relative;
        if (target <= 0 || target > uint.MaxValue)
        {
            return false;
        }

        remoteAddress = (uint)target;
        return true;
    }

    private static byte[] GetRelativeOffset(
        uint instructionAddress,
        uint destination)
    {
        return GetRelativeOffset(
            instructionAddress,
            5,
            destination);
    }

    private static byte[] GetRelativeOffset(
        uint instructionAddress,
        int instructionLength,
        uint destination)
    {
        uint nextInstruction = unchecked(
            instructionAddress + (uint)instructionLength);
        int relative = unchecked(
            (int)(destination - nextInstruction));
        return BitConverter.GetBytes(relative);
    }

    private bool ReadbackMatches(
        uint address,
        byte[] expected)
    {
        byte[] actual = new byte[expected.Length];
        return memory.TryReadBytes(address, actual) &&
            actual.SequenceEqual(expected);
    }

    private bool TryRequestTechTreeRecheck(
        PlayerTechUnlockHookDefinition definition)
    {
        if (!memory.TryRead(
                definition.CurrentPlayerAddress,
                out uint currentPlayer) ||
            currentPlayer == 0)
        {
            return false;
        }

        try
        {
            uint address = checked(
                currentPlayer + definition.RecheckTechTreeOffset);
            byte requested = 1;
            return memory.TryWrite(address, in requested);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static HookInspection Unknown(
        PatchRuntimeState state,
        string message)
    {
        return new HookInspection(
            state,
            message,
            0,
            0,
            0,
            0,
            null);
    }

    private static PatchTransitionResult Fail(string message)
    {
        return new PatchTransitionResult(
            false,
            PatchRuntimeState.Unknown,
            message);
    }

    private sealed record HookInspection(
        PatchRuntimeState State,
        string Message,
        uint ModuleBase,
        uint RequirementEntry,
        uint FactoryEntry,
        uint RemoteAddress,
        TechUnlockMode? Mode);
}
