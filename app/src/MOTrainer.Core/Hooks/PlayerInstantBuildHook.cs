using MOTrainer.Core.Memory;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Processes;

namespace MOTrainer.Core.Hooks;

public sealed class PlayerInstantBuildHook
{
    private const int RemotePageSize = 64;
    private readonly int processId;
    private readonly IMemoryAccessor memory;
    private readonly IRemoteCodeMemory remoteMemory;
    private readonly IModuleResolver modules;

    public PlayerInstantBuildHook(
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
        ConditionalFactoryHookDefinition definition)
    {
        HookInspection inspection = InspectDetailed(definition);
        return new PatchInspection(inspection.State, inspection.Message);
    }

    public PatchTransitionResult SetEnabled(
        ConditionalFactoryHookDefinition definition,
        bool enabled)
    {
        HookInspection inspection = InspectDetailed(definition);
        PatchRuntimeState desired = enabled
            ? PatchRuntimeState.Enabled
            : PatchRuntimeState.Original;
        PatchRuntimeState expected = enabled
            ? PatchRuntimeState.Original
            : PatchRuntimeState.Enabled;

        if (inspection.State == desired)
        {
            return new PatchTransitionResult(
                true,
                desired,
                inspection.Message);
        }

        if (inspection.State != expected)
        {
            return new PatchTransitionResult(
                false,
                inspection.State,
                inspection.Message);
        }

        return enabled
            ? Enable(definition, inspection.EntryAddress)
            : Disable(
                definition,
                inspection.EntryAddress,
                inspection.RemoteAddress);
    }

    private HookInspection InspectDetailed(
        ConditionalFactoryHookDefinition definition)
    {
        definition.Validate();
        if (!TryResolveEntry(definition, out uint entryAddress))
        {
            return new HookInspection(
                PatchRuntimeState.ModuleUnavailable,
                CoreStrings.Format("ModuleUnavailable", definition.ModuleName),
                0,
                0);
        }

        byte[] entry = new byte[definition.OriginalBytes.Length];
        if (!memory.TryReadBytes(entryAddress, entry))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("InstantBuildEntryReadFailed"),
                entryAddress,
                0);
        }

        if (entry.SequenceEqual(definition.OriginalBytes))
        {
            return new HookInspection(
                PatchRuntimeState.Original,
                CoreStrings.Get("InstantBuildDisabled"),
                entryAddress,
                0);
        }

        if (!TryDecodeEntry(entryAddress, entry, out uint remoteAddress))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("InstantBuildUnknownBytes"),
                entryAddress,
                0);
        }

        byte[] expectedStub =
            BuildStub(definition, entryAddress, remoteAddress);
        byte[] actualStub = new byte[expectedStub.Length];
        if (!memory.TryReadBytes(remoteAddress, actualStub) ||
            !actualStub.SequenceEqual(expectedStub))
        {
            return new HookInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Get("InstantBuildUnknownTarget"),
                entryAddress,
                remoteAddress);
        }

        return new HookInspection(
            PatchRuntimeState.Enabled,
            CoreStrings.Get("InstantBuildEnabled"),
            entryAddress,
            remoteAddress);
    }

    private PatchTransitionResult Enable(
        ConditionalFactoryHookDefinition definition,
        uint entryAddress)
    {
        if (!remoteMemory.TryAllocateRemotePage(
                RemotePageSize,
                out uint remoteAddress))
        {
            return Fail(CoreStrings.Get("InstantBuildAllocationFailed"));
        }

        byte[] stub = BuildStub(definition, entryAddress, remoteAddress);
        byte[] entry = BuildEntry(
            entryAddress,
            remoteAddress,
            definition.OriginalBytes.Length);

        bool prepared =
            memory.TryWriteBytes(remoteAddress, stub) &&
            ReadbackMatches(remoteAddress, stub) &&
            remoteMemory.TryMakeRemotePageExecutable(
                remoteAddress,
                RemotePageSize);

        if (!prepared)
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("InstantBuildRemotePageFailed"));
        }

        byte[] current = new byte[definition.OriginalBytes.Length];
        if (!memory.TryReadBytes(entryAddress, current) ||
            !current.SequenceEqual(definition.OriginalBytes))
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("InstantBuildEntryChanged"));
        }

        if (!memory.TryWriteCodeBytes(entryAddress, entry) ||
            !ReadbackMatches(entryAddress, entry))
        {
            memory.TryWriteCodeBytes(
                entryAddress,
                definition.OriginalBytes);
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(CoreStrings.Get("InstantBuildEntryWriteFailed"));
        }

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            CoreStrings.Get("InstantBuildEnabled"));
    }

    private PatchTransitionResult Disable(
        ConditionalFactoryHookDefinition definition,
        uint entryAddress,
        uint remoteAddress)
    {
        byte[] expectedEntry = BuildEntry(
            entryAddress,
            remoteAddress,
            definition.OriginalBytes.Length);
        if (!ReadbackMatches(entryAddress, expectedEntry))
        {
            return Fail(CoreStrings.Get("InstantBuildDisableEntryChanged"));
        }

        if (!memory.TryWriteCodeBytes(
                entryAddress,
                definition.OriginalBytes) ||
            !ReadbackMatches(entryAddress, definition.OriginalBytes))
        {
            return Fail(CoreStrings.Get("InstantBuildEntryRestoreFailed"));
        }

        bool pageFreed =
            remoteMemory.TryFreeRemotePage(remoteAddress);
        string message = pageFreed
            ? CoreStrings.Get("InstantBuildDisabledAndRestored")
            : CoreStrings.Get("InstantBuildPageReleaseFailed");

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Original,
            message);
    }

    private bool TryResolveEntry(
        ConditionalFactoryHookDefinition definition,
        out uint entryAddress)
    {
        entryAddress = 0;
        if (!modules.TryGetModuleBase(
                processId,
                definition.ModuleName,
                out uint moduleBase))
        {
            return false;
        }

        try
        {
            entryAddress = checked(moduleBase + definition.Rva);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static byte[] BuildEntry(
        uint entryAddress,
        uint remoteAddress,
        int length)
    {
        byte[] entry = Enumerable.Repeat((byte)0x90, length).ToArray();
        entry[0] = 0xE9;
        WriteRelativeOffset(entry, 1, entryAddress, remoteAddress);
        return entry;
    }

    private static byte[] BuildStub(
        ConditionalFactoryHookDefinition definition,
        uint entryAddress,
        uint remoteAddress)
    {
        // At the verified FactoryClass update site ESI is FactoryClass* and
        // ECX is the normal progress increment. The original EAX is written
        // into the production timer immediately after the return address, so
        // the owner comparison must preserve it. BL retains the original 1.
        List<byte> bytes =
        [
            0x50,
            0x8B, 0x46, definition.OwnerOffset,
            0x3B, 0x05,
        ];
        bytes.AddRange(BitConverter.GetBytes(definition.CurrentPlayerAddress));
        bytes.AddRange(
        [
            0x58,
            0x75, 0x0B,
            0xC7, 0x46, definition.ProgressOffset,
        ]);
        bytes.AddRange(BitConverter.GetBytes(definition.CompletedProgress));
        bytes.AddRange(
        [
            0xB3, 0x01,
            0xEB, 0x0A,
            0x8B, 0x56, definition.ProgressOffset,
            0x03, 0xD1,
            0xB3, 0x01,
            0x89, 0x56, definition.ProgressOffset,
            0xE9,
        ]);

        uint jumpAddress = checked(
            remoteAddress + (uint)bytes.Count - 1);
        uint returnAddress = checked(
            entryAddress + (uint)definition.OriginalBytes.Length);
        bytes.AddRange(GetRelativeOffset(jumpAddress, returnAddress));
        return bytes.ToArray();
    }

    private static bool TryDecodeEntry(
        uint entryAddress,
        byte[] entry,
        out uint remoteAddress)
    {
        remoteAddress = 0;
        if (entry.Length < 5 ||
            entry[0] != 0xE9)
        {
            return false;
        }

        for (int index = 5; index < entry.Length; index++)
        {
            if (entry[index] != 0x90)
            {
                return false;
            }
        }

        int relative =
            entry[1] |
            (entry[2] << 8) |
            (entry[3] << 16) |
            (entry[4] << 24);
        long target = (long)entryAddress + 5 + relative;
        if (target <= 0 || target > uint.MaxValue)
        {
            return false;
        }

        remoteAddress = (uint)target;
        return true;
    }

    private static void WriteRelativeOffset(
        byte[] target,
        int offset,
        uint instructionAddress,
        uint destination)
    {
        byte[] relative = GetRelativeOffset(instructionAddress, destination);
        Array.Copy(relative, 0, target, offset, relative.Length);
    }

    private static byte[] GetRelativeOffset(
        uint instructionAddress,
        uint destination)
    {
        uint nextInstruction = unchecked(instructionAddress + 5);
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
        uint EntryAddress,
        uint RemoteAddress);
}
