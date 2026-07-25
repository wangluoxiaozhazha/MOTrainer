using MOTrainer.Core.Memory;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Processes;

namespace MOTrainer.Core.Hooks;

public sealed class SelectionVeterancyEventHook
{
    private const int RemotePageSize = 512;
    private const int SecondWrapperOffset = 96;
    private const int PolicyOffset = 192;
    private const int VeterancyValueOffset = PolicyOffset + 4;
    private const int HealthValueOffset = PolicyOffset + 8;
    private readonly int processId;
    private readonly IMemoryAccessor memory;
    private readonly IRemoteCodeMemory remoteMemory;
    private readonly IModuleResolver modules;

    public SelectionVeterancyEventHook(
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
        SelectionVeterancyHookDefinition definition)
    {
        HookInspection inspection = InspectDetailed(definition);
        return new PatchInspection(inspection.State, inspection.Message);
    }

    public PatchTransitionResult SetEnabled(
        SelectionVeterancyHookDefinition definition,
        bool enabled)
    {
        return SetPolicy(
            definition,
            enabled
                ? new SelectionEditHookPolicy(2.0f, null)
                : null);
    }

    public PatchTransitionResult SetPolicy(
        SelectionVeterancyHookDefinition definition,
        SelectionEditHookPolicy? policy)
    {
        policy?.Validate();
        HookInspection inspection = InspectDetailed(definition);
        if (policy is null)
        {
            if (inspection.State == PatchRuntimeState.Original)
            {
                return new PatchTransitionResult(
                    true,
                    PatchRuntimeState.Original,
                    inspection.Message);
            }

            if (inspection.State != PatchRuntimeState.Enabled)
            {
                return new PatchTransitionResult(
                    false,
                    inspection.State,
                    inspection.Message);
            }

            return Disable(
                definition,
                inspection.EntryAddresses,
                inspection.RemoteAddress);
        }

        if (inspection.State == PatchRuntimeState.Enabled &&
            inspection.Policy == policy)
        {
            return new PatchTransitionResult(
                true,
                PatchRuntimeState.Enabled,
                inspection.Message);
        }

        if (inspection.State == PatchRuntimeState.Enabled)
        {
            SelectionEditHookPolicy previousPolicy = inspection.Policy!;
            PatchTransitionResult disabled = Disable(
                definition,
                inspection.EntryAddresses,
                inspection.RemoteAddress);
            if (!disabled.Success)
            {
                return disabled;
            }

            inspection = InspectDetailed(definition);
            if (inspection.State != PatchRuntimeState.Original)
            {
                return new PatchTransitionResult(
                    false,
                    inspection.State,
                    inspection.Message);
            }

            PatchTransitionResult updated = Enable(
                definition,
                inspection.EntryAddresses,
                policy);
            if (updated.Success)
            {
                return updated;
            }

            HookInspection afterFailure = InspectDetailed(definition);
            if (afterFailure.State == PatchRuntimeState.Original)
            {
                PatchTransitionResult restored = Enable(
                    definition,
                    afterFailure.EntryAddresses,
                    previousPolicy);
                return new PatchTransitionResult(
                    false,
                    restored.Success
                        ? PatchRuntimeState.Enabled
                        : restored.State,
                    restored.Success
                        ? $"{updated.Message}；旧事件策略已恢复"
                        : $"{updated.Message}；旧事件策略恢复失败：{restored.Message}");
            }

            return updated;
        }

        if (inspection.State != PatchRuntimeState.Original)
        {
            return new PatchTransitionResult(
                false,
                inspection.State,
                inspection.Message);
        }

        return Enable(definition, inspection.EntryAddresses, policy);
    }

    private HookInspection InspectDetailed(
        SelectionVeterancyHookDefinition definition)
    {
        definition.Validate();
        if (!TryResolveEntries(definition, out uint[] entryAddresses))
        {
            return new HookInspection(
                PatchRuntimeState.ModuleUnavailable,
                $"模块不可用：{definition.ModuleName}",
                [],
                0,
                null);
        }

        byte[][] entries = new byte[definition.Sites.Count][];
        bool allOriginal = true;
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            SelectionHookSite site = definition.Sites[index];
            byte[] entry = new byte[site.OriginalBytes.Length];
            if (!memory.TryReadBytes(entryAddresses[index], entry))
            {
                return Unknown(
                    entryAddresses,
                    "无法读取对象选择 Hook 入口");
            }

            entries[index] = entry;
            allOriginal &= entry.SequenceEqual(site.OriginalBytes);
        }

        if (allOriginal)
        {
            return new HookInspection(
                PatchRuntimeState.Original,
                "选中单位自动修改事件模式处于关闭状态",
                entryAddresses,
                0,
                null);
        }

        uint remoteAddress = 0;
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            uint wrapperOffset = WrapperOffset(index);
            if (!TryDecodeEntry(
                    entryAddresses[index],
                    entries[index],
                    wrapperOffset,
                    out uint candidateRemoteAddress) ||
                (index > 0 && candidateRemoteAddress != remoteAddress))
            {
                return Unknown(
                    entryAddresses,
                    "对象选择入口包含未知或不一致字节，拒绝操作");
            }

            remoteAddress = candidateRemoteAddress;
        }

        int payloadLength = PolicyOffset + 12;
        byte[] actual = new byte[payloadLength];
        if (!memory.TryReadBytes(remoteAddress, actual) ||
            !TryReadPolicy(actual, out SelectionEditHookPolicy policy))
        {
            return Unknown(
                entryAddresses,
                "对象选择入口跳转到未知远程代码，拒绝操作",
                remoteAddress);
        }

        byte[] expected = BuildRemotePage(
            definition,
            entryAddresses,
            remoteAddress,
            policy);
        if (!actual.SequenceEqual(expected))
        {
            return Unknown(
                entryAddresses,
                "对象选择远程代码与当前策略不匹配，拒绝操作",
                remoteAddress);
        }

        return new HookInspection(
            PatchRuntimeState.Enabled,
            "选中单位自动修改双路径事件模式已启用",
            entryAddresses,
            remoteAddress,
            policy);
    }

    private PatchTransitionResult Enable(
        SelectionVeterancyHookDefinition definition,
        uint[] entryAddresses,
        SelectionEditHookPolicy policy)
    {
        if (!remoteMemory.TryAllocateRemotePage(
                RemotePageSize,
                out uint remoteAddress))
        {
            return Fail("无法分配对象选择远程代码页");
        }

        byte[] payload = BuildRemotePage(
            definition,
            entryAddresses,
            remoteAddress,
            policy);
        byte[][] patches = definition.Sites
            .Select((site, index) => BuildEntry(
                entryAddresses[index],
                checked(remoteAddress + WrapperOffset(index)),
                site.OriginalBytes.Length))
            .ToArray();

        bool prepared =
            memory.TryWriteBytes(remoteAddress, payload) &&
            ReadbackMatches(remoteAddress, payload) &&
            remoteMemory.TryMakeRemotePageExecutable(
                remoteAddress,
                RemotePageSize);
        if (!prepared)
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return Fail("对象选择远程代码页写入或权限切换失败");
        }

        for (int index = 0; index < definition.Sites.Count; index++)
        {
            if (!ReadbackMatches(
                    entryAddresses[index],
                    definition.Sites[index].OriginalBytes))
            {
                remoteMemory.TryFreeRemotePage(remoteAddress);
                return Fail("写入前对象选择入口发生变化，已取消操作");
            }
        }

        List<int> installed = [];
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            if (memory.TryWriteCodeBytes(
                    entryAddresses[index],
                    patches[index]) &&
                ReadbackMatches(entryAddresses[index], patches[index]))
            {
                installed.Add(index);
                continue;
            }

            foreach (int installedIndex in installed.AsEnumerable().Reverse())
            {
                memory.TryWriteCodeBytes(
                    entryAddresses[installedIndex],
                    definition.Sites[installedIndex].OriginalBytes);
            }

            bool restored = installed.All(installedIndex =>
                ReadbackMatches(
                    entryAddresses[installedIndex],
                    definition.Sites[installedIndex].OriginalBytes));
            if (restored)
            {
                remoteMemory.TryFreeRemotePage(remoteAddress);
            }

            return Fail(
                restored
                    ? "对象选择入口写入失败，已恢复已安装入口"
                    : "对象选择入口写入失败，且部分入口恢复失败；远程页保持不动");
        }

        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Enabled,
            "选中单位自动修改双路径事件模式已启用");
    }

    private PatchTransitionResult Disable(
        SelectionVeterancyHookDefinition definition,
        uint[] entryAddresses,
        uint remoteAddress)
    {
        byte[][] expectedEntries = definition.Sites
            .Select((site, index) => BuildEntry(
                entryAddresses[index],
                checked(remoteAddress + WrapperOffset(index)),
                site.OriginalBytes.Length))
            .ToArray();
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            if (!ReadbackMatches(
                    entryAddresses[index],
                    expectedEntries[index]))
            {
                return Fail("禁用前对象选择入口发生变化，拒绝恢复");
            }
        }

        List<int> restored = [];
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            if (memory.TryWriteCodeBytes(
                    entryAddresses[index],
                    definition.Sites[index].OriginalBytes) &&
                ReadbackMatches(
                    entryAddresses[index],
                    definition.Sites[index].OriginalBytes))
            {
                restored.Add(index);
                continue;
            }

            foreach (int restoredIndex in restored)
            {
                memory.TryWriteCodeBytes(
                    entryAddresses[restoredIndex],
                    expectedEntries[restoredIndex]);
            }

            bool rolledBack = restored.All(restoredIndex =>
                ReadbackMatches(
                    entryAddresses[restoredIndex],
                    expectedEntries[restoredIndex]));
            return Fail(
                rolledBack
                    ? "对象选择入口恢复失败，已回滚到启用状态"
                    : "对象选择入口恢复失败，且启用状态回滚不完整；远程页保持不动");
        }

        bool pageFreed = remoteMemory.TryFreeRemotePage(remoteAddress);
        string message = pageFreed
            ? "选中单位自动修改事件模式已关闭，双入口和远程页均已恢复"
            : "对象选择双入口已恢复，但远程页释放失败；页面将在游戏退出时回收";
        return new PatchTransitionResult(
            true,
            PatchRuntimeState.Original,
            message);
    }

    private bool TryResolveEntries(
        SelectionVeterancyHookDefinition definition,
        out uint[] entryAddresses)
    {
        entryAddresses = [];
        if (!modules.TryGetModuleBase(
                processId,
                definition.ModuleName,
                out uint moduleBase))
        {
            return false;
        }

        try
        {
            entryAddresses = definition.Sites
                .Select(site => checked(moduleBase + site.Rva))
                .ToArray();
            return true;
        }
        catch (OverflowException)
        {
            entryAddresses = [];
            return false;
        }
    }

    private static uint WrapperOffset(int index) =>
        index switch
        {
            0 => 0,
            1 => SecondWrapperOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    internal static byte[] BuildRemotePage(
        SelectionVeterancyHookDefinition definition,
        IReadOnlyList<uint> entryAddresses,
        uint remoteAddress,
        SelectionEditHookPolicy policy)
    {
        definition.Validate();
        policy.Validate();
        if (entryAddresses.Count != definition.Sites.Count)
        {
            throw new InvalidOperationException(
                "Selection entry address count does not match the definition.");
        }

        List<byte> bytes = [];
        for (int index = 0; index < definition.Sites.Count; index++)
        {
            int wrapperOffset = checked((int)WrapperOffset(index));
            while (bytes.Count < wrapperOffset)
            {
                bytes.Add(0xCC);
            }

            byte[] wrapper = BuildWrapper(
                definition,
                definition.Sites[index],
                entryAddresses[index],
                checked(remoteAddress + (uint)wrapperOffset),
                policy);
            if (bytes.Count + wrapper.Length > PolicyOffset)
            {
                throw new InvalidOperationException(
                    "Selection wrappers overlap the policy header.");
            }
            bytes.AddRange(wrapper);
        }

        while (bytes.Count < PolicyOffset)
        {
            bytes.Add(0xCC);
        }

        int flags =
            (policy.Veterancy is not null ? 1 : 0) |
            (policy.Health is not null ? 2 : 0);
        bytes.AddRange(BitConverter.GetBytes(flags));
        bytes.AddRange(BitConverter.GetBytes(policy.Veterancy ?? 0.0f));
        bytes.AddRange(BitConverter.GetBytes(policy.Health ?? 0));
        return bytes.ToArray();
    }

    internal static byte[] BuildWrapper(
        SelectionVeterancyHookDefinition definition,
        SelectionHookSite site,
        uint entryAddress,
        uint wrapperAddress,
        SelectionEditHookPolicy policy)
    {
        List<byte> bytes =
        [
            0x9C,
            0x60,
            0x8B, 0x3D,
        ];
        bytes.AddRange(BitConverter.GetBytes(
            definition.CurrentPlayerAddress));
        bytes.AddRange(
        [
            0x85, 0xFF,
            0x0F, 0x84, 0x00, 0x00, 0x00, 0x00,
            0x39, 0xBE,
        ]);
        bytes.AddRange(BitConverter.GetBytes(definition.OwnerOffset));
        bytes.AddRange(
        [
            0x0F, 0x85, 0x00, 0x00, 0x00, 0x00,
        ]);

        if (policy.Veterancy is float veterancy)
        {
            bytes.AddRange([0xC7, 0x86]);
            bytes.AddRange(BitConverter.GetBytes(
                definition.VeterancyOffset));
            bytes.AddRange(BitConverter.GetBytes(veterancy));
        }

        if (policy.Health is int health)
        {
            bytes.AddRange([0xC7, 0x86]);
            bytes.AddRange(BitConverter.GetBytes(
                definition.HealthOffset));
            bytes.AddRange(BitConverter.GetBytes(health));
        }

        int doneOffset = bytes.Count;
        bytes.AddRange([0x61, 0x9D]);
        bytes.AddRange(site.OriginalBytes);
        int jumpOffset = bytes.Count;
        bytes.Add(0xE9);
        bytes.AddRange(GetRelativeOffset(
            checked(wrapperAddress + (uint)jumpOffset),
            5,
            checked(entryAddress + (uint)site.OriginalBytes.Length)));

        PatchRelative(
            bytes,
            displacementOffset: 12,
            instructionAddress: checked(wrapperAddress + 10),
            instructionLength: 6,
            destination: checked(wrapperAddress + (uint)doneOffset));
        PatchRelative(
            bytes,
            displacementOffset: 24,
            instructionAddress: checked(wrapperAddress + 22),
            instructionLength: 6,
            destination: checked(wrapperAddress + (uint)doneOffset));
        return bytes.ToArray();
    }

    internal static byte[] BuildEntry(
        uint entryAddress,
        uint wrapperAddress,
        int length)
    {
        byte[] entry = Enumerable.Repeat((byte)0x90, length).ToArray();
        entry[0] = 0xE9;
        WriteRelativeOffset(
            entry,
            1,
            entryAddress,
            5,
            wrapperAddress);
        return entry;
    }

    private static bool TryReadPolicy(
        byte[] payload,
        out SelectionEditHookPolicy policy)
    {
        policy = null!;
        if (payload.Length < HealthValueOffset + 4)
        {
            return false;
        }

        int flags = BitConverter.ToInt32(payload, PolicyOffset);
        if (flags is < 1 or > 3)
        {
            return false;
        }

        SelectionEditHookPolicy candidate = new(
            (flags & 1) != 0
                ? BitConverter.ToSingle(payload, VeterancyValueOffset)
                : null,
            (flags & 2) != 0
                ? BitConverter.ToInt32(payload, HealthValueOffset)
                : null);
        try
        {
            candidate.Validate();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        policy = candidate;
        return true;
    }

    private static bool TryDecodeEntry(
        uint entryAddress,
        byte[] entry,
        uint wrapperOffset,
        out uint remoteAddress)
    {
        remoteAddress = 0;
        if (entry.Length < 5 || entry[0] != 0xE9)
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

        int relative = BitConverter.ToInt32(entry, 1);
        long target = (long)entryAddress + 5 + relative;
        long baseAddress = target - wrapperOffset;
        if (baseAddress <= 0 || baseAddress > uint.MaxValue)
        {
            return false;
        }

        remoteAddress = (uint)baseAddress;
        return true;
    }

    private static void PatchRelative(
        List<byte> bytes,
        int displacementOffset,
        uint instructionAddress,
        int instructionLength,
        uint destination)
    {
        byte[] relative = GetRelativeOffset(
            instructionAddress,
            instructionLength,
            destination);
        for (int index = 0; index < relative.Length; index++)
        {
            bytes[displacementOffset + index] = relative[index];
        }
    }

    private static void WriteRelativeOffset(
        byte[] target,
        int offset,
        uint instructionAddress,
        int instructionLength,
        uint destination)
    {
        byte[] relative = GetRelativeOffset(
            instructionAddress,
            instructionLength,
            destination);
        Array.Copy(relative, 0, target, offset, relative.Length);
    }

    private static byte[] GetRelativeOffset(
        uint instructionAddress,
        int instructionLength,
        uint destination)
    {
        uint nextInstruction = unchecked(
            instructionAddress + (uint)instructionLength);
        int relative = unchecked((int)(destination - nextInstruction));
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

    private static HookInspection Unknown(
        uint[] entryAddresses,
        string message,
        uint remoteAddress = 0)
    {
        return new HookInspection(
            PatchRuntimeState.Unknown,
            message,
            entryAddresses,
            remoteAddress,
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
        uint[] EntryAddresses,
        uint RemoteAddress,
        SelectionEditHookPolicy? Policy);
}
