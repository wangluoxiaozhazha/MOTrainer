using MOTrainer.Core.Memory;
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
                "当前内存访问器不支持远程代码页");
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
                $"不支持的科技全开模式切换：{inspection.Mode} -> {mode}"),
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
                $"模块不可用：{definition.ModuleName}");
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
                "无法读取科技全开双 Hook 入口",
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
                "安全科技全开处于关闭状态",
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
                "科技全开 Hook 状态不一致，拒绝操作",
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
                "科技全开入口跳转到未知远程代码，拒绝操作",
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
                "科技全开阵营模式已启用",
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
                "科技全开工厂 Hook 指向未知代码，拒绝操作",
                moduleBase,
                requirementEntry,
                factoryEntry,
                remoteAddress,
                null);
        }

        return new HookInspection(
            PatchRuntimeState.Enabled,
            "科技全开扩展模式已启用",
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
            return Fail("无法分配科技全开远程代码页");
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
            return Fail("科技全开远程代码页写入或权限切换失败");
        }

        if (!ReadbackMatches(
                inspection.RequirementEntry,
                definition.OriginalBytes) ||
            !ReadbackMatches(
                inspection.FactoryEntry,
                definition.FactoryOwnerOriginalBytes))
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail("写入前科技全开入口发生变化，已取消操作");
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
            return Fail("科技全开 Hook 写入失败，已尝试恢复原字节");
        }

        if (!TryRequestTechTreeRecheck(definition))
        {
            bool restored =
                RestoreOriginalEntries(definition, inspection);
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail(restored
                ? "双 Hook 已安装但科技树重检通知失败，已回滚"
                : "科技树重检通知失败且入口回滚失败，请立即退出游戏");
        }

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            mode == TechUnlockMode.Faction
                ? "科技全开阵营模式已启用，已请求侧栏重建"
                : "科技全开扩展模式已启用，已请求侧栏重建");
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
            return Fail("切换扩展模式时工厂 Hook 写入失败");
        }

        bool recheckRequested = TryRequestTechTreeRecheck(definition);
        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            recheckRequested
                ? "已切换到科技全开扩展模式，并请求侧栏重建"
                : "已切换到科技全开扩展模式，但侧栏重检通知失败");
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
            return Fail("切换阵营模式时工厂 Hook 恢复失败");
        }

        bool recheckRequested = TryRequestTechTreeRecheck(definition);
        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            recheckRequested
                ? "已切换到科技全开阵营模式，并请求侧栏重建"
                : "已切换到科技全开阵营模式，但侧栏重检通知失败");
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
            return Fail("禁用前科技全开 Hook 入口发生变化，拒绝恢复");
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
            return Fail("禁用前科技全开工厂 Hook 入口发生变化，拒绝恢复");
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
            return Fail("科技全开前置 Hook 恢复失败，远程页保持不动");
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
            return Fail("工厂 Hook 恢复失败，已尝试将前置 Hook 回滚为启用");
        }

        bool recheckRequested =
            TryRequestTechTreeRecheck(definition);
        bool pageFreed =
            remoteMemory.TryFreeRemotePage(inspection.RemoteAddress);
        string message = (recheckRequested, pageFreed) switch
        {
            (true, true) =>
                "科技全开已关闭，入口和远程页均已恢复，已请求侧栏重建",
            (false, true) =>
                "科技全开已关闭并释放远程页，但侧栏重检通知失败",
            (true, false) =>
                "科技全开入口已恢复，但远程页释放失败；页面将在游戏退出时回收",
            _ =>
                "科技全开入口已恢复；侧栏重检和远程页释放失败",
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
