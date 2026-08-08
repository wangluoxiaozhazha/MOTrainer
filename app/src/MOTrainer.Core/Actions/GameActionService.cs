using MOTrainer.Core.Memory;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Localization;
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
                return ActionResult.Fail(CoreStrings.Get("MoneyCurrentPlayerUnavailable"));
            }

            uint target = checked(currentPlayer + profile.EconomyOffset);
            if (!memory.TryRead(target, out uint original))
            {
                return ActionResult.Fail(CoreStrings.Get("MoneyReadFailed"));
            }

            if (!memory.TryWrite(target, amount) ||
                !memory.TryRead(target, out uint readBack) ||
                readBack != amount)
            {
                memory.TryWrite(target, original);
                return ActionResult.Fail(CoreStrings.Get("MoneyWriteVerificationFailed"));
            }

            return new ActionResult(
                true,
                CoreStrings.Format("MoneyChanged", original, amount),
                1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("MoneyChangeFailed", exception.Message));
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
                return ActionResult.Fail(
                    CoreStrings.Format("SelectionUnavailable", selection.Status));
            }

            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail(CoreStrings.Get("NoFriendlyUnitsSelected"));
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
                    return ActionResult.Fail(CoreStrings.Get("UnitStateChanged"));
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
                    return ActionResult.Fail(CoreStrings.Get("VeterancyWriteFailed"));
                }

                written++;
            }

            foreach ((uint address, float _) in targets)
            {
                if (!memory.TryRead(address, out float readBack) ||
                    readBack != elite)
                {
                    RestoreVeterancy(memory, targets, targets.Count);
                    return ActionResult.Fail(CoreStrings.Get("VeterancyReadbackFailed"));
                }
            }

            return new ActionResult(
                true,
                CoreStrings.Format("VeterancyApplied", targets.Count),
                targets.Count);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("VeterancyChangeFailed", exception.Message));
        }
    }

    public ActionResult EditSelectedUnits(
        SelectedUnitEditRequest request)
    {
        if (request.Veterancy is null && request.Health is null)
        {
            return ActionResult.Fail(CoreStrings.Get("SelectUnitAttribute"));
        }
        if (request.Health is <= 0)
        {
            return ActionResult.Fail(CoreStrings.Get("HealthMustBePositive"));
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
                    CoreStrings.Format("SelectionUnavailable", selection.Status));
            }
            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail(
                    allCurrentPlayer
                        ? CoreStrings.Get("NoFriendlyUnitsSelected")
                        : CoreStrings.Get("NoUnitsSelected"));
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
                            CoreStrings.Get("UnitOwnerChanged"));
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
                            CoreStrings.Get("UnknownVeterancy")),
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
                            CoreStrings.Get("VeterancyReadFailed"));
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
                            CoreStrings.Get("HealthReadFailed"));
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
                        CoreStrings.Get("UnitAttributeWriteFailed"));
                }

                written++;
            }

                string attributes = string.Join(
                    CoreStrings.Get("AttributeSeparator"),
                new[]
                {
                    request.Veterancy is null ? null : CoreStrings.Get("VeterancyAttribute"),
                    request.Health is null ? null : CoreStrings.Get("HealthAttribute"),
                }.Where(value => value is not null));
            return new ActionResult(
                true,
                CoreStrings.Format(
                    "UnitAttributesChanged",
                    selection.Objects.Count,
                    attributes),
                selection.Objects.Count);
        }
        catch (OverflowException)
        {
            return ActionResult.Fail(CoreStrings.Get("UnitAttributeAddressOverflow"));
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("UnitAttributeChangeFailed", exception.Message));
        }
    }

    public ActionResult AddHealthToSelectedUnits(int amount)
    {
        return ChangeSelectedUnitHealth(
            current => checked(current + amount),
            CoreStrings.Format("HealthIncreased", amount));
    }

    public ActionResult SetSelectedUnitsHealth(int value)
    {
        return ChangeSelectedUnitHealth(
            _ => value,
            CoreStrings.Format("HealthSet", value));
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
                    CoreStrings.Format("SelectionUnavailable", selection.Status));
            }
            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail(CoreStrings.Get("NoUnitsSelected"));
            }

            if (!memory.TryRead(
                    profile.Selection.CurrentPlayerAddress,
                    out uint currentPlayer) ||
                currentPlayer == 0)
            {
                return ActionResult.Fail(CoreStrings.Get("OwnerCurrentPlayerUnavailable"));
            }

            List<UnitEditWrite> writes = [];
            foreach (uint objectAddress in selection.Objects)
            {
                uint ownerAddress = checked(
                    objectAddress + profile.Selection.OwnerOffset);
                if (!memory.TryRead(ownerAddress, out uint originalOwner))
                {
                    return ActionResult.Fail(
                        CoreStrings.Get("SelectedUnitOwnerReadFailed"));
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
                    CoreStrings.Get("UnitsAlreadyOwned"),
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
                        CoreStrings.Get("OwnerWriteFailed"));
                }

                written++;
            }

            return new ActionResult(
                true,
                CoreStrings.Format("OwnerChanged", writes.Count),
                writes.Count);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("OwnerChangeFailed", exception.Message));
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
                return ActionResult.Fail(CoreStrings.Get("FactionEconomyReadFailed"));
            }

            if (!TryWriteAndVerify(
                    memory,
                    economyAddress,
                    0u,
                    original))
            {
                return ActionResult.Fail(CoreStrings.Get("FactionEconomyWriteFailed"));
            }

            return new ActionResult(
                true,
                CoreStrings.Format("FactionEconomyZeroed", original),
                1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("FactionEconomyZeroFailed", exception.Message));
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
                return ActionResult.Fail(CoreStrings.Get("FactionDefeatReadFailed"));
            }

            const byte defeated = 1;
            if (!TryWriteAndVerify(
                    memory,
                    defeatedAddress,
                    defeated,
                    original))
            {
                return ActionResult.Fail(CoreStrings.Get("FactionSurrenderWriteFailed"));
            }

            return new ActionResult(
                true,
                original == defeated
                    ? CoreStrings.Get("FactionAlreadyDefeated")
                    : CoreStrings.Get("FactionMarkedSurrendered"),
                original == defeated ? 0 : 1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("FactionSurrenderFailed", exception.Message));
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
                return ActionResult.Fail(CoreStrings.Get("SuperweaponIdUnsupported"));
            }

            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
            if (memory is not IRemoteCodeMemory remoteMemory ||
                memory is not IRemoteThreadExecutor threadExecutor)
            {
                return ActionResult.Fail(
                    CoreStrings.Get("RemoteExecutionUnsupported"));
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
                    ? CoreStrings.Format("SuperweaponExecutedById", ids[0])
                    : CoreStrings.Format("SuperweaponBatchExecutedSerialized", ids.Count));
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("SuperweaponExecutionFailed", exception.Message));
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
            return ActionResult.Fail(CoreStrings.Get("SuperweaponAllocationFailed"));
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
                CoreStrings.Get("SuperweaponPayloadFailed"));
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
                CoreStrings.Format(
                    "RemoteExecutionPageRetained",
                    execution.Message));
        }
        if (!remoteMemory.TryFreeRemotePage(remoteAddress))
        {
            return ActionResult.Fail(
                CoreStrings.Get("SuperweaponPageReleaseFailed"));
        }

        return new ActionResult(
            true,
            ids.Count == 1
                ? CoreStrings.Format("SuperweaponExecutedById", ids[0])
                : CoreStrings.Format("SuperweaponBatchExecuted", ids.Count));
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
                    CoreStrings.Format("ConditionalHookInspectionFailed", exception.Message));
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
                    CoreStrings.Format("VeterancyHookInspectionFailed", exception.Message));
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
                    CoreStrings.Format("TechUnlockHookInspectionFailed", exception.Message));
            }
        }

        if (!profile.Patches.TryGetValue(patchId, out PatchDefinition? patch))
        {
            return new PatchInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Format("PatchDefinitionMissing", patchId));
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
                CoreStrings.Format("PatchInspectionFailed", exception.Message));
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
                CoreStrings.Format("TechUnlockDefinitionMissing", patchId));
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
                CoreStrings.Format("TechUnlockModeInspectionFailed", exception.Message));
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
                CoreStrings.Format("TechUnlockDefinitionMissing", displayName));
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
                : CoreStrings.Format(
                    "FeatureToggleTransitionFailed",
                    displayName,
                    transition.Message);
            return new ActionResult(transition.Success, message);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format(
                    "FeatureModeChangeFailed",
                    displayName,
                    exception.Message));
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
                    : CoreStrings.Format(
                        "FeatureToggleTransitionFailed",
                        displayName,
                        transition.Message);
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    CoreStrings.Format(
                        "ConditionalHookToggleFailed",
                        displayName,
                        exception.Message));
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
                    : CoreStrings.Format(
                        "FeatureToggleTransitionFailed",
                        displayName,
                        transition.Message);
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    CoreStrings.Format(
                        "EventHookToggleFailed",
                        displayName,
                        exception.Message));
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
                    : CoreStrings.Format(
                        "FeatureToggleTransitionFailed",
                        displayName,
                        transition.Message);
                return new ActionResult(transition.Success, message);
            }
            catch (Exception exception)
            {
                return ActionResult.Fail(
                    CoreStrings.Format(
                        "SafeHookToggleFailed",
                        displayName,
                        exception.Message));
            }
        }

        if (!profile.Patches.TryGetValue(patchId, out PatchDefinition? patch))
        {
            return ActionResult.Fail(
                CoreStrings.Format("PatchDefinitionMissing", displayName));
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
                    ? CoreStrings.Format("FeatureEnabled", displayName)
                    : CoreStrings.Format("FeatureDisabledAndRestored", displayName)
                : transition.Message;

            return new ActionResult(transition.Success, message);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format(
                    "FeatureToggleFailed",
                    displayName,
                    exception.Message));
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
                CoreStrings.Get("SelectionEditHookDefinitionMissing"));
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
                CoreStrings.Format("SelectionEditModeToggleFailed", exception.Message));
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
            CoreStrings.Get("EconomyLockDisplayName"),
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
                    CoreStrings.Get("AutoRepairReadFailed"));
            }

            return value switch
            {
                1 => new PatchInspection(
                    PatchRuntimeState.Original,
                    CoreStrings.Get("AutoRepairDisabled")),
                0 => new PatchInspection(
                    PatchRuntimeState.Enabled,
                    CoreStrings.Get("AutoRepairEnabled")),
                _ => new PatchInspection(
                    PatchRuntimeState.Unknown,
                    CoreStrings.Format("AutoRepairUnknownValue", value)),
            };
        }
        catch (Exception exception)
        {
            return new PatchInspection(
                PatchRuntimeState.Unknown,
                CoreStrings.Format("AutoRepairInspectionFailed", exception.Message));
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
                    CoreStrings.Get("AutoRepairUnsafeValue"));
            }

            byte target = enabled ? (byte)0 : (byte)1;
            if (original == target)
            {
                return new ActionResult(
                    true,
                    enabled
                        ? CoreStrings.Format("FeatureAlreadyEnabled", displayName)
                        : CoreStrings.Format("FeatureAlreadyDisabled", displayName));
            }

            if (!memory.TryWrite(address, in target) ||
                !memory.TryRead(address, out byte readBack) ||
                readBack != target)
            {
                memory.TryWrite(address, in original);
                return ActionResult.Fail(
                    CoreStrings.Format("FeatureWriteFailed", displayName));
            }

            return new ActionResult(
                true,
                enabled
                    ? CoreStrings.Format("InvertedFeatureEnabled", displayName)
                    : CoreStrings.Format("InvertedFeatureDisabled", displayName),
                1);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format(
                    "FeatureToggleFailed",
                    displayName,
                    exception.Message));
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
            error = CoreStrings.Get("AutoRepairNotReady");
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
            error = CoreStrings.Get("AutoRepairAddressOverflow");
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
                    .Read(SelectionScope.All, currentPlayerOnly: false);

            if (!selection.IsSuccess)
            {
                return ActionResult.Fail(
                    CoreStrings.Format("SelectionUnavailable", selection.Status));
            }

            if (selection.Objects.Count == 0)
            {
                return ActionResult.Fail(CoreStrings.Get("NoUnitsSelected"));
            }

            List<UnitEditWrite> writes = [];
            foreach (uint objectAddress in selection.Objects)
            {
                uint healthAddress = checked(
                    objectAddress + profile.Selection.HealthOffset);
                if (!memory.TryRead(healthAddress, out int original))
                {
                    return ActionResult.Fail(
                        CoreStrings.Get("UnitStateChanged"));
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
                        CoreStrings.Get("HealthWriteFailed"));
                }

                written++;
            }

            return new ActionResult(
                true,
                CoreStrings.Format(
                    "SelectedUnitOperationSucceeded",
                    successPrefix,
                    writes.Count),
                writes.Count);
        }
        catch (OverflowException)
        {
            return ActionResult.Fail(CoreStrings.Get("HealthCalculationOverflow"));
        }
        catch (Exception exception)
        {
            return ActionResult.Fail(
                CoreStrings.Format("HealthChangeFailed", exception.Message));
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
            error = CoreStrings.Format("SelectionUnavailable", selection.Status);
            return false;
        }

        if (selection.Objects.Count == 0)
        {
            error = CoreStrings.Get("NoUnitsSelected");
            return false;
        }

        objectAddress = selection.Objects[0];
        ownerAddress = checked(
            objectAddress + profile.Selection.OwnerOffset);
        if (!memory.TryRead(ownerAddress, out owner) || owner == 0)
        {
            error = CoreStrings.Get("SelectedOwnerReadFailed");
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
