using MOTrainer.Core.Actions;
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
                ? Pending(true, "等待游戏模块就绪")
                : Off("未预设");
        }

        PatchInspection inspection =
            actions.InspectPatch(KnownPatchIds.UnitUnlimited, session);
        if (unitDesired)
        {
            if (inspection.State == PatchRuntimeState.Original)
            {
                ActionResult result = actions.SetPatchEnabled(
                    KnownPatchIds.UnitUnlimited,
                    "解锁建造数量",
                    true);
                if (!result.Success)
                {
                    return Failed(true, result.Message);
                }

                unitOwnedProcessId = processId;
                return Active(true, "四处 BuildLimit 补丁已应用");
            }

            return inspection.State == PatchRuntimeState.Enabled
                ? Active(true, "四处 BuildLimit 补丁已生效")
                : Failed(true, inspection.Message);
        }

        if (unitOwnedProcessId != processId)
        {
            return inspection.State == PatchRuntimeState.Enabled
                ? Failed(false, "检测到非本次会话拥有的启用补丁")
                : Off("未启用");
        }

        if (inspection.State == PatchRuntimeState.Enabled)
        {
            ActionResult result = actions.SetPatchEnabled(
                KnownPatchIds.UnitUnlimited,
                "解锁建造数量",
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
        return Off("已恢复四处原字节");
    }

    private DelayedFeatureStatus ReconcileQueue(
        GameSessionSnapshot session)
    {
        if (session.ProcessId is not int processId ||
            session.Profile is not VersionProfile profile)
        {
            return queueDesired
                ? Pending(true, "等待建造系统出现")
                : Off("未预设");
        }

        if (!queueDesired && queueRestore is not null)
        {
            if (!TryRestoreQueue(queueRestore))
            {
                return Failed(false, "队列字段已变化或原值恢复失败");
            }

            uint restored = queueRestore.OriginalValue;
            queueRestore = null;
            return Off($"队列上限已恢复为 {restored}");
        }

        if (!TryResolveQueueAddress(
                processId,
                profile,
                out uint address,
                out uint current))
        {
            return queueDesired
                ? Pending(true, "已预设，等待基地建造列表")
                : Off("建造队列结构尚未出现");
        }

        if (queueDesired)
        {
            if (queueRestore is null ||
                queueRestore.ProcessId != processId ||
                queueRestore.Address != address)
            {
                if (queueRestore is not null &&
                    !TryRestoreQueue(queueRestore))
                {
                    return Failed(true, "旧队列地址恢复失败，拒绝迁移");
                }

                QueueRestorePoint restore =
                    new(processId, address, current);
                if (!TryWriteQueueValue(processId, address, QueueLimitValue))
                {
                    return Failed(true, "写入队列上限 999 失败");
                }

                queueRestore = restore;
                return Active(true, $"队列上限已应用；原值 {current}");
            }

            if (current == QueueLimitValue)
            {
                return Active(true, "队列容量上限为 1000");
            }
            if (current == queueRestore.OriginalValue &&
                TryWriteQueueValue(processId, address, QueueLimitValue))
            {
                return Active(true, "队列结构刷新后已重新应用");
            }

            return Failed(true, $"队列字段变为未知值 {current}，拒绝覆盖");
        }

        return Off("未启用");
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

    private bool TryRestoreQueue(QueueRestorePoint restore)
    {
        using IMemoryAccessor memory =
            memoryFactory.Open(
                restore.ProcessId,
                MemoryAccessMode.ReadWrite);
        if (!memory.TryRead(restore.Address, out uint current))
        {
            return false;
        }
        if (current == restore.OriginalValue)
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

    private sealed record QueueRestorePoint(
        int ProcessId,
        uint Address,
        uint OriginalValue);
}
