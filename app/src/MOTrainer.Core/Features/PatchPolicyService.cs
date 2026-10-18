using MOTrainer.Core.Actions;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Processes;
using MOTrainer.Core.Session;

namespace MOTrainer.Core.Features;

public sealed record PatchPolicyStatus(
    bool Desired,
    FeatureState State,
    string Message,
    TechUnlockMode? TechUnlockMode = null);

/// <summary>
/// Stores what the user wants every toggle to do and reconciles those wishes
/// with the game process. Toggles can therefore be prepared before the game
/// starts and remain active across map changes until the trainer closes.
/// </summary>
public sealed class PatchPolicyService
{
    private readonly IGameSessionProbe sessionProbe;
    private readonly GameActionService actions;
    private readonly Dictionary<string, bool> desired = [];
    private readonly Dictionary<string, int> appliedProcessIds = [];
    private TechUnlockMode desiredTechUnlockMode = TechUnlockMode.Faction;

    public PatchPolicyService(
        IGameSessionProbe sessionProbe,
        IMemoryAccessorFactory? memoryFactory = null,
        IModuleResolver? moduleResolver = null)
    {
        this.sessionProbe = sessionProbe;
        actions = new GameActionService(
            sessionProbe,
            memoryFactory,
            moduleResolver);
    }

    public bool IsDesired(string patchId) =>
        desired.TryGetValue(patchId, out bool value) && value;

    public TechUnlockMode DesiredTechUnlockMode => desiredTechUnlockMode;

    public void SetDesired(
        string patchId,
        bool enabled,
        TechUnlockMode? techUnlockMode = null)
    {
        desired[patchId] = enabled;
        if (techUnlockMode is TechUnlockMode mode)
        {
            desiredTechUnlockMode = mode;
        }
    }

    public void SetTechUnlockMode(TechUnlockMode mode)
    {
        desiredTechUnlockMode = mode;
    }

    public IReadOnlyDictionary<string, PatchPolicyStatus> Reconcile() =>
        Reconcile(sessionProbe.Probe());

    public IReadOnlyDictionary<string, PatchPolicyStatus> Reconcile(
        GameSessionSnapshot session)
    {
        Dictionary<string, PatchPolicyStatus> statuses =
            new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> item in desired.ToArray())
        {
            statuses[item.Key] = ReconcilePatch(
                item.Key,
                item.Value,
                session);
        }

        return statuses;
    }

    public IReadOnlyDictionary<string, PatchPolicyStatus> RestoreOwned() =>
        RestoreOwned(sessionProbe.Probe());

    public IReadOnlyDictionary<string, PatchPolicyStatus> RestoreOwned(
        GameSessionSnapshot session)
    {
        Dictionary<string, PatchPolicyStatus> statuses =
            new(StringComparer.Ordinal);
        foreach (string patchId in desired.Keys.ToArray())
        {
            if (!desired[patchId] ||
                session.Profile is not VersionProfile ||
                session.ProcessId is null)
            {
                desired[patchId] = false;
                appliedProcessIds.Remove(patchId);
                continue;
            }

            ActionResult restore;
            if (patchId == KnownPatchIds.SafeTechUnlock)
            {
                restore = actions.SetTechUnlockMode(
                    patchId,
                    CoreStrings.Get("TechUnlockDisplayName"),
                    TechUnlockMode.Off,
                    session);
            }
            else
            {
                restore = actions.SetPatchEnabled(
                    patchId,
                    patchId,
                    false,
                    session);
            }

            desired[patchId] = false;
            appliedProcessIds.Remove(patchId);
            statuses[patchId] = new PatchPolicyStatus(
                false,
                restore.Success
                    ? FeatureState.Off
                    : FeatureState.Failed,
                restore.Message);
        }

        return statuses;
    }

    private PatchPolicyStatus ReconcilePatch(
        string patchId,
        bool enabled,
        GameSessionSnapshot session)
    {
        if (!enabled)
        {
            appliedProcessIds.Remove(patchId);
            return new PatchPolicyStatus(
                false,
                FeatureState.Off,
                CoreStrings.Get("PatchPolicyOff"));
        }

        if (session.ProcessId is not int processId ||
            session.Profile is null)
        {
            return new PatchPolicyStatus(
                true,
                FeatureState.Pending,
                CoreStrings.Get("PatchPolicyWaitingForGame"));
        }

        PatchInspection inspection =
            actions.InspectPatch(patchId, session);
        if (inspection.State == PatchRuntimeState.Enabled &&
            appliedProcessIds.TryGetValue(patchId, out int appliedProcessId) &&
            appliedProcessId == processId)
        {
            return new PatchPolicyStatus(
                true,
                FeatureState.Active,
                inspection.Message);
        }

        if (inspection.State == PatchRuntimeState.ModuleUnavailable)
        {
            return new PatchPolicyStatus(
                true,
                FeatureState.Pending,
                inspection.Message);
        }

        ActionResult result;
        if (patchId == KnownPatchIds.SafeTechUnlock)
        {
            result = actions.SetTechUnlockMode(
                patchId,
                CoreStrings.Get("TechUnlockDisplayName"),
                desiredTechUnlockMode,
                session);
        }
        else
        {
            result = actions.SetPatchEnabled(
                patchId,
                patchId,
                true,
                session);
        }

        if (!result.Success)
        {
            return new PatchPolicyStatus(
                true,
                FeatureState.Failed,
                result.Message);
        }

        appliedProcessIds[patchId] = processId;
        return new PatchPolicyStatus(
            true,
            FeatureState.Active,
            result.Message,
            patchId == KnownPatchIds.SafeTechUnlock
                ? desiredTechUnlockMode
                : null);
    }
}
