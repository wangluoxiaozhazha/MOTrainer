using MOTrainer.Core.Actions;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Processes;
using MOTrainer.Core.Session;

namespace MOTrainer.Core.Features;

public sealed record AutoApplyStatus(
    bool Desired,
    FeatureState State,
    string Message);

public sealed record AutoApplySnapshot(
    AutoApplyStatus Money,
    AutoApplyStatus SelectionEdit);

/// <summary>
/// Remembers the values the user configured and keeps them applied for as long
/// as the trainer is open. Money is re-applied once per match because every
/// match builds a fresh economy object, while the selected-unit event hook is
/// installed once since it lives in game code and therefore survives map
/// changes. <see cref="RestoreOwned"/> releases everything when the trainer
/// closes so nothing stays active after the user exits.
/// </summary>
public sealed class AutoApplyPolicyService
{
    private readonly IGameSessionProbe sessionProbe;
    private readonly IMemoryAccessorFactory memoryFactory;
    private readonly IModuleResolver moduleResolver;
    private readonly GameActionService actions;

    private bool moneyDesired;
    private uint moneyAmount;
    private int? moneyAppliedProcessId;
    private uint moneyAppliedPlayer;
    private uint moneyAppliedAmount;

    private SelectedUnitEditRequest? selectionDesired;
    private int? selectionAppliedProcessId;
    private SelectedUnitEditRequest? selectionAppliedRequest;

    public AutoApplyPolicyService(
        IGameSessionProbe sessionProbe,
        IMemoryAccessorFactory? memoryFactory = null,
        IModuleResolver? moduleResolver = null)
    {
        this.sessionProbe = sessionProbe;
        this.memoryFactory = memoryFactory ??
            ProcessMemoryAccessorFactory.Instance;
        this.moduleResolver = moduleResolver ??
            ProcessModuleResolver.Instance;
        actions = new GameActionService(
            sessionProbe,
            this.memoryFactory,
            this.moduleResolver);
    }

    public AutoApplySnapshot SetMoney(bool desired, uint amount)
    {
        moneyDesired = desired;
        moneyAmount = amount;
        return Reconcile();
    }

    public AutoApplySnapshot SetSelectionEdit(
        SelectedUnitEditRequest? request)
    {
        selectionDesired = request;
        return Reconcile();
    }

    public AutoApplySnapshot Reconcile() =>
        Reconcile(sessionProbe.Probe());

    public AutoApplySnapshot Reconcile(GameSessionSnapshot session)
    {
        // A restarted game process no longer owns anything we installed.
        if (moneyAppliedProcessId is not null &&
            moneyAppliedProcessId != session.ProcessId)
        {
            moneyAppliedProcessId = null;
            moneyAppliedPlayer = 0;
        }

        return new AutoApplySnapshot(
            ReconcileMoney(session),
            ReconcileSelectionEdit(session));
    }

    public AutoApplySnapshot RestoreOwned() =>
        RestoreOwned(sessionProbe.Probe());

    public AutoApplySnapshot RestoreOwned(GameSessionSnapshot session)
    {
        moneyDesired = false;
        selectionDesired = null;
        return Reconcile(session);
    }

    private AutoApplyStatus ReconcileMoney(GameSessionSnapshot session)
    {
        if (!moneyDesired)
        {
            return Off(CoreStrings.Get("AutoMoneyOff"));
        }

        if (session.State != SessionState.MatchReady ||
            session.ProcessId is not int processId ||
            session.Profile is not VersionProfile profile)
        {
            return Pending(true, CoreStrings.Get("AutoMoneyWaitingForMatch"));
        }

        // Applying once per match keeps the configured value without resetting
        // the player's spending every polling tick.
        if (moneyAppliedProcessId == processId &&
            moneyAppliedAmount == moneyAmount &&
            TryReadCurrentPlayer(processId, profile, out uint player) &&
            player == moneyAppliedPlayer)
        {
            return Active(true, CoreStrings.Format("AutoMoneyActive", moneyAmount));
        }

        ActionResult result = actions.SetMoney(moneyAmount);
        if (!result.Success)
        {
            return Failed(true, result.Message);
        }

        if (TryReadCurrentPlayer(processId, profile, out uint appliedPlayer))
        {
            moneyAppliedProcessId = processId;
            moneyAppliedPlayer = appliedPlayer;
            moneyAppliedAmount = moneyAmount;
        }

        return Active(true, CoreStrings.Format("AutoMoneyApplied", moneyAmount));
    }

    private AutoApplyStatus ReconcileSelectionEdit(GameSessionSnapshot session)
    {
        if (selectionDesired is null)
        {
            if (selectionAppliedProcessId is null)
            {
                return Off(CoreStrings.Get("AutoSelectionOff"));
            }

            // The user turned the feature off, so release the event hook that
            // this service installed and forget the ownership marker.
            ActionResult disable = actions.SetSelectionEditEventPolicy(null);
            if (!disable.Success)
            {
                return Failed(false, disable.Message);
            }

            selectionAppliedProcessId = null;
            selectionAppliedRequest = null;
            return Off(CoreStrings.Get("AutoSelectionOff"));
        }

        if (session.ProcessId is not int processId ||
            session.Profile is not VersionProfile)
        {
            return Pending(
                true,
                CoreStrings.Get("AutoSelectionWaitingForModules"));
        }

        if (selectionAppliedProcessId == processId &&
            selectionAppliedRequest == selectionDesired &&
            actions.InspectPatch(
                KnownPatchIds.AutoPromoteSelection,
                session).State == PatchRuntimeState.Enabled)
        {
            return Active(true, CoreStrings.Get("AutoSelectionArmed"));
        }

        ActionResult result =
            actions.SetSelectionEditEventPolicy(selectionDesired);
        if (!result.Success)
        {
            return Failed(true, result.Message);
        }

        selectionAppliedProcessId = processId;
        selectionAppliedRequest = selectionDesired;
        return Active(true, CoreStrings.Get("AutoSelectionArmed"));
    }

    private bool TryReadCurrentPlayer(
        int processId,
        VersionProfile profile,
        out uint player)
    {
        player = 0;
        try
        {
            using IMemoryAccessor memory = memoryFactory.Open(
                processId,
                MemoryAccessMode.ReadOnly);
            return memory.TryRead(
                    profile.Selection.CurrentPlayerAddress,
                    out player) &&
                player != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static AutoApplyStatus Off(string message) =>
        new(false, FeatureState.Off, message);

    private static AutoApplyStatus Pending(bool desired, string message) =>
        new(desired, FeatureState.Pending, message);

    private static AutoApplyStatus Active(bool desired, string message) =>
        new(desired, FeatureState.Active, message);

    private static AutoApplyStatus Failed(bool desired, string message) =>
        new(desired, FeatureState.Failed, message);
}
