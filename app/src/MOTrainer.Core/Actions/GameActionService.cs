using MOTrainer.Core.Memory;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Processes;
using MOTrainer.Core.Selection;
using MOTrainer.Core.Session;

namespace MOTrainer.Core.Actions;

public sealed class GameActionService
{
    private readonly IGameSessionProbe sessionProbe;
    private readonly IMemoryAccessorFactory memoryFactory;
    private readonly IModuleResolver moduleResolver;

    public GameActionService(
        IGameSessionProbe sessionProbe,
        IMemoryAccessorFactory? memoryFactory = null,
        IModuleResolver? moduleResolver = null)
    {
        this.sessionProbe = sessionProbe;
        this.memoryFactory = memoryFactory ?? ProcessMemoryAccessorFactory.Instance;
        this.moduleResolver = moduleResolver ?? ProcessModuleResolver.Instance;
    }

    public ActionResult SetMoney(uint amount)
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            if (!memory.TryRead(profile.Selection.CurrentPlayerAddress, out uint currentPlayer) ||
                currentPlayer == 0)
            {
                return ActionResult.Fail("CurrentPlayer 不可用，拒绝修改金钱");
            }

            uint target = checked(currentPlayer + profile.EconomyOffset);
            if (!memory.TryRead(target, out uint original))
            {
                return ActionResult.Fail("无法读取当前金钱，拒绝写入");
            }

            if (!memory.TryWrite(target, amount) ||
                !memory.TryRead(target, out uint readBack) ||
                readBack != amount)
            {
                memory.TryWrite(target, original);
                return ActionResult.Fail("金钱写入或回读失败，已尝试恢复原值");
            }

            return new ActionResult(true, $"金钱已由 {original} 修改为 {amount}", 1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"金钱修改失败：{exception.Message}");
        }
    }

    public ActionResult PromoteSelectedUnits()
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            SelectionSnapshot selection =
                new SelectionService(memory, profile.Selection)
                    .Read(SelectionScope.All, currentPlayerOnly: true);

            if (!selection.IsSuccess)
            {
                return ActionResult.Fail($"选择向量不可用：{selection.Status}");
            }

            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail("没有选中属于当前玩家的单位");
            }

            List<(uint Address, float Original)> targets = new(selection.Objects.Count);
            foreach (uint objectAddress in selection.Objects)
            {
                uint ownerAddress = checked(objectAddress + profile.Selection.OwnerOffset);
                uint veterancyAddress = checked(objectAddress + profile.Selection.VeterancyOffset);

                if (!memory.TryRead(ownerAddress, out uint owner) ||
                    owner != selection.CurrentPlayer ||
                    !memory.TryRead(veterancyAddress, out float original))
                {
                    return ActionResult.Fail("单位状态在写入前发生变化，已取消本次操作");
                }

                targets.Add((veterancyAddress, original));
            }

            const float elite = 2.0f;
            int written = 0;

            foreach ((uint address, float _) in targets)
            {
                if (!memory.TryWrite(address, elite))
                {
                    RestoreVeterancy(memory, targets, written);
                    return ActionResult.Fail("升星写入失败，已尝试恢复本次已修改单位");
                }

                written++;
            }

            foreach ((uint address, float _) in targets)
            {
                if (!memory.TryRead(address, out float readBack) ||
                    readBack != elite)
                {
                    RestoreVeterancy(memory, targets, targets.Count);
                    return ActionResult.Fail("升星回读失败，已尝试恢复本次修改");
                }
            }

            return new ActionResult(
                true,
                $"已将 {targets.Count} 个当前玩家单位设为三星",
                targets.Count);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"选中单位升星失败：{exception.Message}");
        }
    }

    public ActionResult EditSelectedUnits(
        SelectedUnitEditRequest request)
    {
        if (request.Veterancy is null && request.Health is null)
        {
            return ActionResult.Fail("请至少选择一项要修改的单位属性");
        }
        if (request.Health is <= 0)
        {
            return ActionResult.Fail("单位生命值必须大于 0");
        }
        if (!TryGetReadySession(
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            bool allCurrentPlayer =
                request.Scope == SelectedUnitEditScope.AllCurrentPlayer;
            SelectionSnapshot selection =
                new SelectionService(memory, profile.Selection).Read(
                    allCurrentPlayer
                        ? SelectionScope.All
                        : SelectionScope.First,
                    currentPlayerOnly: allCurrentPlayer);
            if (!selection.IsSuccess)
            {
                return ActionResult.Fail(
                    $"选择向量不可用：{selection.Status}");
            }
            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail(
                    allCurrentPlayer
                        ? "没有选中属于当前玩家的单位"
                        : "没有选中单位");
            }

            List<UnitEditWrite> writes = [];
            foreach (uint objectAddress in selection.Objects)
            {
                if (allCurrentPlayer)
                {
                    uint ownerAddress = checked(
                        objectAddress + profile.Selection.OwnerOffset);
                    if (!memory.TryRead(ownerAddress, out uint owner) ||
                        owner != selection.CurrentPlayer)
                    {
                        return ActionResult.Fail(
                            "单位归属在写入前发生变化，已取消本次操作");
                    }
                }

                if (request.Veterancy is UnitVeterancyLevel level)
                {
                    float target = level switch
                    {
                        UnitVeterancyLevel.Rookie => 0.0f,
                        UnitVeterancyLevel.Veteran => 1.0f,
                        UnitVeterancyLevel.Elite => 2.0f,
                        _ => throw new InvalidOperationException(
                            "未知单位等级"),
                    };
                    if (!TryPrepareUnitEditWrite(
                            memory,
                            checked(
                                objectAddress +
                                profile.Selection.VeterancyOffset),
                            BitConverter.GetBytes(target),
                            out UnitEditWrite write))
                    {
                        return ActionResult.Fail(
                            "无法读取单位等级，已取消本次操作");
                    }

                    writes.Add(write);
                }

                if (request.Health is int health)
                {
                    if (!TryPrepareUnitEditWrite(
                            memory,
                            checked(
                                objectAddress +
                                profile.Selection.HealthOffset),
                            BitConverter.GetBytes(health),
                            out UnitEditWrite write))
                    {
                        return ActionResult.Fail(
                            "无法读取单位生命值，已取消本次操作");
                    }

                    writes.Add(write);
                }
            }

            int written = 0;
            foreach (UnitEditWrite write in writes)
            {
                if (!memory.TryWriteBytes(write.Address, write.Target) ||
                    !ReadUnitEditBack(memory, write))
                {
                    RestoreUnitEditWrites(
                        memory,
                        writes,
                        Math.Min(written + 1, writes.Count));
                    return ActionResult.Fail(
                        "单位属性写入或回读失败，已尝试恢复本次修改");
                }

                written++;
            }

            string attributes = string.Join(
                "、",
                new[]
                {
                    request.Veterancy is null ? null : "等级",
                    request.Health is null ? null : "生命",
                }.Where(value => value is not null));
            return new ActionResult(
                true,
                $"已修改 {selection.Objects.Count} 个单位的{attributes}",
                selection.Objects.Count);
        }
        catch (OverflowException)
        {
            return ActionResult.Fail("单位属性地址计算溢出，拒绝写入");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                $"选中单位组合修改失败：{exception.Message}");
        }
    }

    public ActionResult AddHealthToSelectedUnits(int amount)
    {
        return ChangeSelectedUnitHealth(
            current => checked(current + amount),
            $"选中单位生命值增加 {amount}");
    }

    public ActionResult SetSelectedUnitsHealth(int value)
    {
        return ChangeSelectedUnitHealth(
            _ => value,
            $"选中单位生命值设为 {value}");
    }

    public ActionResult TransferSelectedUnitsToCurrentPlayer()
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            SelectionSnapshot selection =
                new SelectionService(memory, profile.Selection)
                    .Read(SelectionScope.All, currentPlayerOnly: false);
            if (!selection.IsSuccess)
            {
                return ActionResult.Fail(
                    $"选择向量不可用：{selection.Status}");
            }
            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail("没有选中单位");
            }

            if (!memory.TryRead(
                    profile.Selection.CurrentPlayerAddress,
                    out uint currentPlayer) ||
                currentPlayer == 0)
            {
                return ActionResult.Fail("CurrentPlayer 不可用，拒绝修改单位归属");
            }

            List<UnitEditWrite> writes = [];
            foreach (uint objectAddress in selection.Objects)
            {
                uint ownerAddress = checked(
                    objectAddress + profile.Selection.OwnerOffset);
                if (!memory.TryRead(ownerAddress, out uint originalOwner))
                {
                    return ActionResult.Fail(
                        "无法读取选中单位归属，已取消本次操作");
                }

                if (originalOwner == currentPlayer)
                {
                    continue;
                }

                writes.Add(new UnitEditWrite(
                    ownerAddress,
                    BitConverter.GetBytes(originalOwner),
                    BitConverter.GetBytes(currentPlayer)));
            }

            if (writes.Count == 0)
            {
                return new ActionResult(
                    true,
                    "选中单位均已属于当前玩家",
                    0);
            }

            int written = 0;
            foreach (UnitEditWrite write in writes)
            {
                if (!memory.TryWriteBytes(write.Address, write.Target) ||
                    !ReadUnitEditBack(memory, write))
                {
                    RestoreUnitEditWrites(
                        memory,
                        writes,
                        Math.Min(written + 1, writes.Count));
                    return ActionResult.Fail(
                        "单位归属写入或回读失败，已尝试恢复本次修改");
                }

                written++;
            }

            return new ActionResult(
                true,
                $"已将 {writes.Count} 个选中单位归属改为当前玩家",
                writes.Count);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"单位归属修改失败：{exception.Message}");
        }
    }

    public ActionResult ZeroFirstSelectedOwnerEconomy()
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            if (!TryReadFirstSelectedOwner(
                    memory,
                    profile,
                    out uint _,
                    out uint _,
                    out uint owner,
                    out error))
            {
                return ActionResult.Fail(error);
            }

            uint economyAddress = checked(owner + profile.EconomyOffset);
            if (!memory.TryRead(economyAddress, out uint original))
            {
                return ActionResult.Fail("无法读取目标阵营经济，拒绝写入");
            }

            if (!TryWriteAndVerify(
                    memory,
                    economyAddress,
                    0u,
                    original))
            {
                return ActionResult.Fail("目标阵营经济清零失败，已尝试恢复原值");
            }

            return new ActionResult(
                true,
                $"目标阵营经济已由 {original} 清零",
                1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"目标阵营经济清零失败：{exception.Message}");
        }
    }

    public ActionResult DefeatFirstSelectedOwner()
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            if (!TryReadFirstSelectedOwner(
                    memory,
                    profile,
                    out uint _,
                    out uint _,
                    out uint owner,
                    out error))
            {
                return ActionResult.Fail(error);
            }

            uint defeatedAddress = checked(
                owner + profile.DefeatedStateOffset);
            if (!memory.TryRead(defeatedAddress, out byte original))
            {
                return ActionResult.Fail("无法读取目标阵营失败状态，拒绝写入");
            }

            const byte defeated = 1;
            if (!TryWriteAndVerify(
                    memory,
                    defeatedAddress,
                    defeated,
                    original))
            {
                return ActionResult.Fail("目标阵营投降标记写入失败，已尝试恢复原值");
            }

            return new ActionResult(
                true,
                original == defeated
                    ? "目标阵营已经处于失败状态"
                    : "目标阵营已标记为投降；继续游戏后由引擎处理",
                original == defeated ? 0 : 1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"目标阵营投降失败：{exception.Message}");
        }
    }

    public ActionResult GrantSuperweapon(uint superweaponId)
    {
        return GrantSuperweapons([superweaponId]);
    }

    public ActionResult GrantSuperweapons(
        IReadOnlyList<uint> superweaponIds)
    {
        if (!TryGetReadySession(
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        return ExecuteSuperweaponIds(
            processId,
            profile.SuperweaponExecutor,
            superweaponIds);
    }

    public ActionResult GrantAllSuperweapons()
    {
        if (!TryGetReadySession(
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        uint[] ids = Enumerable.Range(
                0,
                checked((int)profile.SuperweaponExecutor.MaximumId + 1))
            .Select(value => (uint)value)
            .ToArray();
        return ExecuteSuperweaponIds(
            processId,
            profile.SuperweaponExecutor,
            ids);
    }

    public ActionResult GrantSafeSuperweapons()
    {
        if (!TryGetReadySession(
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        return ExecuteSuperweaponIds(
            processId,
            profile.SuperweaponExecutor,
            profile.SuperweaponExecutor.SafeIds);
    }

    private ActionResult ExecuteSuperweaponIds(
        int processId,
        SuperweaponExecutorDefinition definition,
        IReadOnlyList<uint> ids)
    {
        try
        {
            definition.Validate();
            if (ids.Count == 0 ||
                ids.Any(id => id > definition.MaximumId))
            {
                return ActionResult.Fail("超武 ID 超出已验证范围");
            }

            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            if (memory is not IRemoteCodeMemory remoteMemory ||
                memory is not IRemoteThreadExecutor threadExecutor)
            {
                return ActionResult.Fail(
                    "当前内存访问器不支持远程执行");
            }

            ActionResult result = ExecuteSuperweaponBatch(
                definition,
                ids,
                memory,
                remoteMemory,
                threadExecutor);
            if (!result.Success)
            {
                return result;
            }

            return new ActionResult(
                true,
                ids.Count == 1
                    ? $"已执行超武 ID {ids[0]}"
                    : $"已在单个批处理线程中顺序执行 {ids.Count} 个超武 ID");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                $"超武执行失败：{exception.Message}");
        }
    }

    private static ActionResult ExecuteSuperweaponBatch(
        SuperweaponExecutorDefinition definition,
        IReadOnlyList<uint> ids,
        IMemoryAccessor memory,
        IRemoteCodeMemory remoteMemory,
        IRemoteThreadExecutor threadExecutor)
    {
        byte[] payload = BuildSuperweaponBatch(definition, ids);

        if (!remoteMemory.TryAllocateRemotePage(
                payload.Length,
                out uint remoteAddress))
        {
            return ActionResult.Fail("无法分配超武远程代码页");
        }

        byte[] readback = new byte[payload.Length];
        bool prepared =
            memory.TryWriteBytes(remoteAddress, payload) &&
            memory.TryReadBytes(remoteAddress, readback) &&
            readback.SequenceEqual(payload) &&
            remoteMemory.TryMakeRemotePageExecutable(
                remoteAddress,
                payload.Length);
        if (!prepared)
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return ActionResult.Fail(
                "超武载荷写入、回读或 RX 权限切换失败");
        }

        RemoteThreadExecutionResult execution =
            threadExecutor.ExecuteRemoteThread(
                remoteAddress,
                timeoutMilliseconds: 5000);
        if (!execution.Started)
        {
            remoteMemory.TryFreeRemotePage(remoteAddress);
            return ActionResult.Fail(execution.Message);
        }
        if (!execution.Completed)
        {
            return ActionResult.Fail(
                $"{execution.Message}；为避免释放仍在执行的代码，远程页保留至游戏退出");
        }
        if (!remoteMemory.TryFreeRemotePage(remoteAddress))
        {
            return ActionResult.Fail(
                "超武已执行，但远程代码页释放失败");
        }

        return new ActionResult(
            true,
            ids.Count == 1
                ? $"已执行超武 ID {ids[0]}"
                : $"已执行 {ids.Count} 项超武批处理");
    }

    private static byte[] BuildSuperweaponBatch(
        SuperweaponExecutorDefinition definition,
        IReadOnlyList<uint> ids)
    {
        int bodyLength = definition.Template.Length - 1;
        byte[] payload = new byte[
            checked(bodyLength * ids.Count + 1)];
        int destination = 0;

        for (int index = 0; index < ids.Count; index++)
        {
            byte[] body = (byte[])definition.Template.Clone();
            byte[] parameter = BitConverter.GetBytes(ids[index]);
            Array.Copy(
                parameter,
                0,
                body,
                definition.FirstParameterOffset,
                parameter.Length);
            Array.Copy(
                parameter,
                0,
                body,
                definition.SecondParameterOffset,
                parameter.Length);

            int copyLength = index == ids.Count - 1
                ? body.Length
                : bodyLength;
            Array.Copy(
                body,
                0,
                payload,
                destination,
                copyLength);
            destination += copyLength;
        }

        return payload;
    }

    public PatchInspection InspectPatch(string patchId)
    {
        return InspectPatch(patchId, sessionProbe.Probe());
    }

    public PatchInspection InspectPatch(
        string patchId,
        GameSessionSnapshot session)
    {
        if (!TryGetSession(
                session,
                requireMatch: false,
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return new PatchInspection(PatchRuntimeState.ModuleUnavailable, error);
        }

        if (patchId == KnownPatchIds.AutoRepair)
        {
            return InspectAutoRepair(processId, profile);
        }

        if (profile.Hooks.TryGetValue(
                patchId,
                out ConditionalFactoryHookDefinition? hook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
                return new PlayerInstantBuildHook(
                        processId,
                        memory,
                        moduleResolver)
                    .Inspect(hook);
            }
            catch (Exception exception)
            {
                return new PatchInspection(
                    PatchRuntimeState.Unknown,
                    $"读取条件 Hook 状态失败：{exception.Message}");
            }
        }

        if (profile.SelectionHooks.TryGetValue(
                patchId,
                out SelectionVeterancyHookDefinition? selectionHook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
                return new SelectionVeterancyEventHook(
                        processId,
                        memory,
                        moduleResolver)
                    .Inspect(selectionHook);
            }
            catch (Exception exception)
            {
                return new PatchInspection(
                    PatchRuntimeState.Unknown,
                    $"读取框选三星 Hook 状态失败：{exception.Message}");
            }
        }

        if (profile.TechUnlockHooks.TryGetValue(
                patchId,
                out PlayerTechUnlockHookDefinition? techUnlockHook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
                return new PlayerTechUnlockCompositeHook(
                        processId,
                        memory,
                        moduleResolver)
                    .Inspect(techUnlockHook);
            }
            catch (Exception exception)
            {
                return new PatchInspection(
                    PatchRuntimeState.Unknown,
                    $"读取科技全开 Hook 状态失败：{exception.Message}");
            }
        }

        if (!profile.Patches.TryGetValue(patchId, out PatchDefinition? patch))
        {
            return new PatchInspection(
                PatchRuntimeState.Unknown,
                $"当前版本没有补丁配置：{patchId}");
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
            return new PatchManager(processId, memory, moduleResolver).Inspect(patch);
        }
        catch (Exception exception)
        {
            return new PatchInspection(
                PatchRuntimeState.Unknown,
                $"读取补丁状态失败：{exception.Message}");
        }
    }

    public TechUnlockModeInspection InspectTechUnlockMode(
        string patchId)
    {
        return InspectTechUnlockMode(patchId, sessionProbe.Probe());
    }

    public TechUnlockModeInspection InspectTechUnlockMode(
        string patchId,
        GameSessionSnapshot session)
    {
        if (!TryGetSession(
                session,
                requireMatch: false,
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return new TechUnlockModeInspection(
                null,
                PatchRuntimeState.ModuleUnavailable,
                error);
        }

        if (!profile.TechUnlockHooks.TryGetValue(
                patchId,
                out PlayerTechUnlockHookDefinition? techUnlockHook))
        {
            return new TechUnlockModeInspection(
                null,
                PatchRuntimeState.Unknown,
                $"当前版本没有科技全开 Hook 配置：{patchId}");
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
            return new PlayerTechUnlockCompositeHook(
                    processId,
                    memory,
                    moduleResolver)
                .InspectMode(techUnlockHook);
        }
        catch (Exception exception)
        {
            return new TechUnlockModeInspection(
                null,
                PatchRuntimeState.Unknown,
                $"读取科技全开模式失败：{exception.Message}");
        }
    }

    public ActionResult SetTechUnlockMode(
        string patchId,
        string displayName,
        TechUnlockMode mode)
    {
        if (!TryGetSession(
                requireMatch: false,
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        if (!profile.TechUnlockHooks.TryGetValue(
                patchId,
                out PlayerTechUnlockHookDefinition? techUnlockHook))
        {
            return ActionResult.Fail(
                $"当前版本没有科技全开 Hook 配置：{displayName}");
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            PatchTransitionResult transition =
                new PlayerTechUnlockCompositeHook(
                        processId,
                        memory,
                        moduleResolver)
                    .SetMode(techUnlockHook, mode);

            string message = transition.Success
                ? transition.Message
                : $"{displayName}切换失败：{transition.Message}";
            return new ActionResult(transition.Success, message);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                $"{displayName}模式切换失败：{exception.Message}");
        }
    }

    public ActionResult SetPatchEnabled(
        string patchId,
        string displayName,
        bool enabled)
    {
        if (!TryGetSession(
                requireMatch: false,
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        if (patchId == KnownPatchIds.AutoRepair)
        {
            return SetAutoRepairEnabled(
                processId,
                profile,
                displayName,
                enabled);
        }

        if (profile.Hooks.TryGetValue(
                patchId,
                out ConditionalFactoryHookDefinition? hook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
                PatchTransitionResult transition =
                    new PlayerInstantBuildHook(
                            processId,
                            memory,
                            moduleResolver)
                        .SetEnabled(hook, enabled);

                string message = transition.Success
                    ? transition.Message
                    : $"{displayName}切换失败：{transition.Message}";
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    $"{displayName}条件 Hook 切换失败：{exception.Message}");
            }
        }

        if (profile.SelectionHooks.TryGetValue(
                patchId,
                out SelectionVeterancyHookDefinition? selectionHook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
                PatchTransitionResult transition =
                    new SelectionVeterancyEventHook(
                            processId,
                            memory,
                            moduleResolver)
                        .SetEnabled(selectionHook, enabled);

                string message = transition.Success
                    ? transition.Message
                    : $"{displayName}切换失败：{transition.Message}";
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    $"{displayName}事件 Hook 切换失败：{exception.Message}");
            }
        }

        if (profile.TechUnlockHooks.TryGetValue(
                patchId,
                out PlayerTechUnlockHookDefinition? techUnlockHook))
        {
            try
            {
                using IMemoryAccessor memory =
                    memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
                PatchTransitionResult transition =
                    new PlayerTechUnlockCompositeHook(
                            processId,
                            memory,
                            moduleResolver)
                        .SetEnabled(techUnlockHook, enabled);

                string message = transition.Success
                    ? transition.Message
                    : $"{displayName}切换失败：{transition.Message}";
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    $"{displayName}安全 Hook 切换失败：{exception.Message}");
            }
        }

        if (!profile.Patches.TryGetValue(patchId, out PatchDefinition? patch))
        {
            return ActionResult.Fail($"当前版本没有补丁配置：{displayName}");
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            PatchTransitionResult transition =
                new PatchManager(processId, memory, moduleResolver)
                    .SetEnabled(patch, enabled);

            string message = transition.Success
                ? enabled
                    ? $"{displayName}已启用"
                    : $"{displayName}已关闭并恢复原字节"
                : transition.Message;

            return new ActionResult(transition.Success, message);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"{displayName}切换失败：{exception.Message}");
        }
    }

    public ActionResult SetSelectionEditEventPolicy(
        SelectedUnitEditRequest? request)
    {
        if (!TryGetSession(
                requireMatch: false,
                out int processId,
                out VersionProfile profile,
                out string error))
        {
            return ActionResult.Fail(error);
        }

        if (!profile.SelectionHooks.TryGetValue(
                KnownPatchIds.AutoPromoteSelection,
                out SelectionVeterancyHookDefinition? selectionHook))
        {
            return ActionResult.Fail(
                "当前版本没有选中单位自动修改事件 Hook 配置");
        }

        SelectionEditHookPolicy? policy = request is null
            ? null
            : new SelectionEditHookPolicy(
                request.Veterancy switch
                {
                    UnitVeterancyLevel.Rookie => 0.0f,
                    UnitVeterancyLevel.Veteran => 1.0f,
                    UnitVeterancyLevel.Elite => 2.0f,
                    _ => null,
                },
                request.Health);

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(
                    processId,
                    MemoryAccessMode.ReadWrite);
            PatchTransitionResult transition =
                new SelectionVeterancyEventHook(
                        processId,
                        memory,
                        moduleResolver)
                    .SetPolicy(selectionHook, policy);
            return new ActionResult(
                transition.Success,
                transition.Message);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                $"选中单位自动修改事件模式切换失败：{exception.Message}");
        }
    }

    public PatchInspection InspectEconomyNoDecrease()
    {
        return InspectPatch(KnownPatchIds.EconomyNoDecrease);
    }

    public ActionResult SetEconomyNoDecrease(bool enabled)
    {
        return SetPatchEnabled(
            KnownPatchIds.EconomyNoDecrease,
            "锁定经济",
            enabled);
    }

    private PatchInspection InspectAutoRepair(
        int processId,
        VersionProfile profile)
    {
        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
            if (!TryResolveAutoRepairAddress(
                    memory,
                    profile,
                    out uint address,
                    out string error))
            {
                return new PatchInspection(
                    PatchRuntimeState.ModuleUnavailable,
                    error);
            }

            if (!memory.TryRead(address, out byte value))
            {
                return new PatchInspection(
                    PatchRuntimeState.Unknown,
                    "无法读取自动维修状态");
            }

            return value switch
            {
                1 => new PatchInspection(
                    PatchRuntimeState.Original,
                    "自动维修处于关闭状态"),
                0 => new PatchInspection(
                    PatchRuntimeState.Enabled,
                    "自动维修处于启用状态"),
                _ => new PatchInspection(
                    PatchRuntimeState.Unknown,
                    $"自动维修字段为未知值 {value}，拒绝操作"),
            };
        }
        catch (Exception exception)
        {
            return new PatchInspection(
                PatchRuntimeState.Unknown,
                $"读取自动维修状态失败：{exception.Message}");
        }
    }

    private ActionResult SetAutoRepairEnabled(
        int processId,
        VersionProfile profile,
        string displayName,
        bool enabled)
    {
        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            if (!TryResolveAutoRepairAddress(
                    memory,
                    profile,
                    out uint address,
                    out string error))
            {
                return ActionResult.Fail(error);
            }

            if (!memory.TryRead(address, out byte original) ||
                original is not (0 or 1))
            {
                return ActionResult.Fail(
                    "自动维修字段不可读或包含未知值，拒绝覆盖");
            }

            byte target = enabled ? (byte)0 : (byte)1;
            if (original == target)
            {
                return new ActionResult(
                    true,
                    enabled
                        ? $"{displayName}已经启用"
                        : $"{displayName}已经关闭");
            }

            if (!memory.TryWrite(address, in target) ||
                !memory.TryRead(address, out byte readBack) ||
                readBack != target)
            {
                memory.TryWrite(address, in original);
                return ActionResult.Fail(
                    $"{displayName}写入或回读失败，已尝试恢复原值");
            }

            return new ActionResult(
                true,
                enabled
                    ? $"{displayName}已启用（1 → 0）"
                    : $"{displayName}已关闭（0 → 1）",
                1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                $"{displayName}切换失败：{exception.Message}");
        }
    }

    private static bool TryResolveAutoRepairAddress(
        IMemoryAccessor memory,
        VersionProfile profile,
        out uint address,
        out string error)
    {
        address = 0;
        if (!memory.TryRead(
                profile.BuildQueueRootAddress,
                out uint root) ||
            root == 0)
        {
            error = "自动维修结构尚未就绪";
            return false;
        }

        try
        {
            address = checked(root + profile.AutoRepairOffset);
            error = string.Empty;
            return true;
        }
        catch (OverflowException)
        {
            error = "自动维修地址计算溢出";
            return false;
        }
    }

    private bool TryGetReadySession(
        out int processId,
        out VersionProfile profile,
        out string error)
    {
        return TryGetSession(
            requireMatch: true,
            out processId,
            out profile,
            out error);
    }

    private bool TryGetSession(
        bool requireMatch,
        out int processId,
        out VersionProfile profile,
        out string error)
    {
        return TryGetSession(
            sessionProbe.Probe(),
            requireMatch,
            out processId,
            out profile,
            out error);
    }

    private static bool TryGetSession(
        GameSessionSnapshot snapshot,
        bool requireMatch,
        out int processId,
        out VersionProfile profile,
        out string error)
    {
        bool stateAccepted = requireMatch
            ? snapshot.State == SessionState.MatchReady
            : snapshot.State is SessionState.ModulesReady or SessionState.MatchReady;

        if (!stateAccepted ||
            snapshot.ProcessId is not int readyProcessId ||
            snapshot.Profile is not VersionProfile readyProfile)
        {
            processId = 0;
            profile = null!;
            error = snapshot.Message;
            return false;
        }

        processId = readyProcessId;
        profile = readyProfile;
        error = string.Empty;
        return true;
    }

    private static void RestoreVeterancy(
        IMemoryAccessor memory,
        IReadOnlyList<(uint Address, float Original)> targets,
        int count)
    {
        for (int index = 0; index < count; index++)
        {
            (uint address, float original) = targets[index];
            memory.TryWrite(address, original);
        }
    }

    private static bool TryPrepareUnitEditWrite(
        IMemoryAccessor memory,
        uint address,
        byte[] target,
        out UnitEditWrite write)
    {
        byte[] original = new byte[target.Length];
        if (!memory.TryReadBytes(address, original))
        {
            write = null!;
            return false;
        }

        write = new UnitEditWrite(address, original, target);
        return true;
    }

    private static bool ReadUnitEditBack(
        IMemoryAccessor memory,
        UnitEditWrite write)
    {
        byte[] readBack = new byte[write.Target.Length];
        return memory.TryReadBytes(write.Address, readBack) &&
            readBack.SequenceEqual(write.Target);
    }

    private static void RestoreUnitEditWrites(
        IMemoryAccessor memory,
        IReadOnlyList<UnitEditWrite> writes,
        int count)
    {
        for (int index = 0; index < count; index++)
        {
            memory.TryWriteBytes(
                writes[index].Address,
                writes[index].Original);
        }
    }

    private ActionResult ChangeSelectedUnitHealth(
        Func<int, int> transform,
        string successPrefix)
    {
        if (!TryGetReadySession(out int processId, out VersionProfile profile, out string error))
        {
            return ActionResult.Fail(error);
        }

        try
        {
            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);

            SelectionSnapshot selection =
                new SelectionService(memory, profile.Selection)
                    .Read(SelectionScope.All, currentPlayerOnly: true);

            if (!selection.IsSuccess)
            {
                return ActionResult.Fail($"选择向量不可用：{selection.Status}");
            }

            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail("没有选中属于当前玩家的单位");
            }

            List<UnitEditWrite> writes = [];
            foreach (uint objectAddress in selection.Objects)
            {
                uint ownerAddress = checked(
                    objectAddress + profile.Selection.OwnerOffset);
                uint healthAddress = checked(
                    objectAddress + profile.Selection.HealthOffset);
                if (!memory.TryRead(ownerAddress, out uint owner) ||
                    owner != selection.CurrentPlayer ||
                    !memory.TryRead(healthAddress, out int original))
                {
                    return ActionResult.Fail(
                        "单位状态在写入前发生变化，已取消本次操作");
                }

                int target = transform(original);
                writes.Add(new UnitEditWrite(
                    healthAddress,
                    BitConverter.GetBytes(original),
                    BitConverter.GetBytes(target)));
            }

            int written = 0;
            foreach (UnitEditWrite write in writes)
            {
                if (!memory.TryWriteBytes(write.Address, write.Target) ||
                    !ReadUnitEditBack(memory, write))
                {
                    RestoreUnitEditWrites(
                        memory,
                        writes,
                        Math.Min(written + 1, writes.Count));
                    return ActionResult.Fail(
                        "生命值写入或回读失败，已尝试恢复本次修改");
                }

                written++;
            }

            return new ActionResult(
                true,
                $"{successPrefix}：{writes.Count} 个当前玩家单位",
                writes.Count);
        }
        catch (OverflowException)
        {
            return ActionResult.Fail("生命值计算溢出，拒绝写入");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"生命值修改失败：{exception.Message}");
        }
    }

    private sealed record UnitEditWrite(
        uint Address,
        byte[] Original,
        byte[] Target);

    private static bool TryReadFirstSelectedOwner(
        IMemoryAccessor memory,
        VersionProfile profile,
        out uint objectAddress,
        out uint ownerAddress,
        out uint owner,
        out string error)
    {
        objectAddress = 0;
        ownerAddress = 0;
        owner = 0;

        SelectionSnapshot selection =
            new SelectionService(memory, profile.Selection)
                .Read(SelectionScope.First, currentPlayerOnly: false);
        if (!selection.IsSuccess)
        {
            error = $"选择向量不可用：{selection.Status}";
            return false;
        }

        if (selection.Objects.Count == 0)
        {
            error = "没有选中单位";
            return false;
        }

        objectAddress = selection.Objects[0];
        ownerAddress = checked(
            objectAddress + profile.Selection.OwnerOffset);
        if (!memory.TryRead(ownerAddress, out owner) || owner == 0)
        {
            error = "无法读取选中单位 Owner，拒绝写入";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryWriteAndVerify<T>(
        IMemoryAccessor memory,
        uint address,
        T target,
        T original)
        where T : unmanaged, IEquatable<T>
    {
        if (memory.TryWrite(address, target) &&
            memory.TryRead(address, out T readBack) &&
            readBack.Equals(target))
        {
            return true;
        }

        memory.TryWrite(address, original);
        return false;
    }
}
