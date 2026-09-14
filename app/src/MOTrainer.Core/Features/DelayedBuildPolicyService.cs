using MOTrainer.Core.Actions;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Processes;
using MOTrainer.Core.Session;

namespace MOTrainer.Core.Features;

public sealed class DelayedBuildPolicyService
{
    private const uint QueueLimitValue = 999;
    private readonly IGameSessionProbe sessionProbe;
    private readonly IMemoryAccessorFactory memoryFactory;
    private readonly GameActionService actions;
    private bool unitDesired;
    private bool queueDesired;
    private int? unitOwnedProcessId;
    private QueueRestorePoint? queueRestore;
    private QueueCandidate? queueCandidate;

    public DelayedBuildPolicyService(
        IGameSessionProbe sessionProbe,
        IMemoryAccessorFactory? memoryFactory = null,
        IModuleResolver? moduleResolver = null)
    {
        this.sessionProbe = sessionProbe;
        this.memoryFactory = memoryFactory ??
            ProcessMemoryAccessorFactory.Instance;
        actions = new GameActionService(
            sessionProbe,
            this.memoryFactory,
            moduleResolver);
    }

    public DelayedBuildPolicySnapshot SetDesired(
        DelayedBuildFeature feature,
        bool desired)
    {
        if (feature == DelayedBuildFeature.UnitUnlimited)
        {
            unitDesired = desired;
        }
        else
        {
            queueDesired = desired;
        }

        if (!queueDesired)
        {
            queueCandidate = null;
        }
        return Reconcile();
    }

    public DelayedBuildPolicySnapshot Reconcile()
    {
        return Reconcile(sessionProbe.Probe());
    }

    public DelayedBuildPolicySnapshot Reconcile(
        GameSessionSnapshot session)
    {
        if (session.ProcessId != unitOwnedProcessId &&
            unitOwnedProcessId is not null)
        {
            unitOwnedProcessId = null;
        }
        if (queueRestore is not null &&
            queueRestore.ProcessId != session.ProcessId)
        {
            queueRestore = null;
        }

        DelayedFeatureStatus unit = ReconcileUnit(session);
        DelayedFeatureStatus queue = ReconcileQueue(session);
        return new DelayedBuildPolicySnapshot(unit, queue);
    }

    public DelayedBuildPolicySnapshot RestoreOwned()
    {
        unitDesired = false;
        queueDesired = false;
        return Reconcile();
    }

    private DelayedFeatureStatus ReconcileUnit(
        GameSessionSnapshot session)
    {
        if (session.ProcessId is not int processId ||
            session.Profile is null)
        {
            return unitDesired
                ? Pending(true, CoreStrings.Get("WaitingForGameModules"))
                : Off(CoreStrings.Get("NotPreset"));
        }

        PatchInspection inspection =
            actions.InspectPatch(KnownPatchIds.UnitUnlimited, session);
        if (unitDesired)
        {
            if (inspection.State == PatchRuntimeState.Original)
            {
                ActionResult result = actions.SetPatchEnabled(
                    KnownPatchIds.UnitUnlimited,
                    CoreStrings.Get("BuildLimitUnlockDisplayName"),
                    true);
                if (!result.Success)
                {
                    return Failed(true, result.Message);
                }

                unitOwnedProcessId = processId;
                return Active(true, CoreStrings.Get("BuildLimitPatchesApplied"));
            }

            return inspection.State == PatchRuntimeState.Enabled
                ? Active(true, CoreStrings.Get("BuildLimitPatchesActive"))
                : Failed(true, inspection.Message);
        }

        if (unitOwnedProcessId != processId)
        {
            return inspection.State == PatchRuntimeState.Enabled
                ? Failed(false, CoreStrings.Get("PatchOwnedByAnotherSession"))
                : Off(CoreStrings.Get("NotEnabled"));
        }

        if (inspection.State == PatchRuntimeState.Enabled)
        {
            ActionResult result = actions.SetPatchEnabled(
                KnownPatchIds.UnitUnlimited,
                CoreStrings.Get("BuildLimitUnlockDisplayName"),
                false);
            if (!result.Success)
            {
                return Failed(false, result.Message);
            }
        }
        else if (inspection.State != PatchRuntimeState.Original)
        {
            return Failed(false, inspection.Message);
        }

        unitOwnedProcessId = null;
        return Off(CoreStrings.Get("BuildLimitPatchesRestored"));
    }

    private DelayedFeatureStatus ReconcileQueue(
        GameSessionSnapshot session)
    {
        if (session.ProcessId is not int processId ||
            session.Profile is not VersionProfile profile)
        {
            return queueDesired
                ? Pending(true, CoreStrings.Get("WaitingForBuildSystem"))
                : Off(CoreStrings.Get("NotPreset"));
        }

        if (!queueDesired && queueRestore is not null)
        {
            if (!TryRestoreQueue(queueRestore, profile))
            {
                return Failed(false, CoreStrings.Get("QueueRestoreFailed"));
            }

            queueRestore = null;
            return Off(CoreStrings.Get("QueuePolicyReleased"));
        }

        // Readable memory can still contain constructor defaults during loading.
        if (queueDesired && session.State != SessionState.MatchReady)
        {
            queueCandidate = null;
            return Pending(true, CoreStrings.Get("QueuePresetWaiting"));
        }

        if (!TryResolveQueueAddress(
                processId,
                profile,
                out uint address,
                out uint current))
        {
            queueCandidate = null;
            return queueDesired
                ? Pending(true, CoreStrings.Get("QueuePresetWaiting"))
                : Off(CoreStrings.Get("BuildQueueUnavailable"));
        }

        if (queueDesired)
        {
            if (queueRestore is null ||
                queueRestore.ProcessId != processId ||
                queueRestore.Address != address)
            {
                if (!QueueInitializationObserved(processId, profile, address, current))
                {
                    return Pending(true, CoreStrings.Get("QueuePresetWaiting"));
                }
                if (queueRestore is not null &&
                    !TryRestoreQueue(queueRestore, profile))
                {
                    return Failed(true, CoreStrings.Get("OldQueueRestoreFailed"));
                }

                QueueRestorePoint restore =
                    new(processId, address, current);
                if (!TryWriteQueueValue(processId, address, QueueLimitValue))
                {
                    return Failed(true, CoreStrings.Get("QueueLimitWriteFailed"));
                }

                queueRestore = restore;
                queueCandidate = null;
                return Active(true, CoreStrings.Format("QueueLimitApplied", current));
            }

            if (current == QueueLimitValue)
            {
                return Active(true, CoreStrings.Get("QueueLimitActive"));
            }
            if (current == queueRestore.OriginalValue &&
                TryWriteQueueValue(processId, address, QueueLimitValue))
            {
                return Active(true, CoreStrings.Get("QueueLimitReapplied"));
            }

            return Failed(true, CoreStrings.Format("QueueUnknownValue", current));
        }

        return Off(CoreStrings.Get("NotEnabled"));
    }

    private bool QueueInitializationObserved(
        int processId, VersionProfile profile, uint address, uint value)
    {
        using IMemoryAccessor memory =
            memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
        if (!memory.TryRead(profile.CurrentFrameAddress, out int frame) || frame <= 0)
        {
            queueCandidate = null;
            return false;
        }

        QueueCandidate? previous = queueCandidate;
        queueCandidate = new(processId, address, value, frame);
        // Require the same field/value across observations while simulation advances.
        // Paused games remain pending until they resume.
        return previous is not null &&
            previous.ProcessId == processId && previous.Address == address &&
            previous.Value == value && frame > previous.Frame;
    }

    private bool TryResolveQueueAddress(
        int processId,
        VersionProfile profile,
        out uint address,
        out uint value)
    {
        address = 0;
        value = 0;
        using IMemoryAccessor memory =
            memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);
        if (!memory.TryRead(
                profile.BuildQueueRootAddress,
                out uint root) ||
            root == 0)
        {
            return false;
        }

        try
        {
            address = checked(root + profile.BuildQueueLimitOffset);
            return memory.TryRead(address, out value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private bool TryRestoreQueue(QueueRestorePoint restore, VersionProfile profile)
    {
        using IMemoryAccessor memory =
            memoryFactory.Open(
                restore.ProcessId,
                MemoryAccessMode.ReadWrite);
        // The rules object may be replaced between matches or when loading a save.
        // Never restore a captured field into an object that is no longer current.
        if (!memory.TryRead(profile.BuildQueueRootAddress, out uint root))
        {
            return false;
        }
        if (root == 0 || (ulong)root + profile.BuildQueueLimitOffset != restore.Address)
        {
            return true;
        }
        if (!memory.TryRead(restore.Address, out uint current))
        {
            return false;
        }
        // A different value supersedes our write; preserve it and release ownership.
        if (current != QueueLimitValue)
        {
            return true;
        }
        uint originalValue = restore.OriginalValue;
        return current == QueueLimitValue &&
            memory.TryWrite(restore.Address, in originalValue) &&
            memory.TryRead(restore.Address, out uint readback) &&
            readback == originalValue;
    }

    private bool TryWriteQueueValue(
        int processId,
        uint address,
        uint value)
    {
        using IMemoryAccessor memory =
            memoryFactory.Open(processId, MemoryAccessMode.ReadWrite);
        return memory.TryWrite(address, in value) &&
            memory.TryRead(address, out uint readback) &&
            readback == value;
    }

    private static DelayedFeatureStatus Off(string message) =>
        new(false, FeatureState.Off, message);

    private static DelayedFeatureStatus Pending(
        bool desired,
        string message) =>
        new(desired, FeatureState.Pending, message);

    private static DelayedFeatureStatus Active(
        bool desired,
        string message) =>
        new(desired, FeatureState.Active, message);

    private static DelayedFeatureStatus Failed(
        bool desired,
        string message) =>
        new(desired, FeatureState.Failed, message);

    private sealed record QueueCandidate(int ProcessId, uint Address, uint Value, int Frame);

    private sealed record QueueRestorePoint(
        int ProcessId,
        uint Address,
        uint OriginalValue);
}
