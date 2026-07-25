using MOTrainer.Core.Selection;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Actions;

namespace MOTrainer.Core.Profiles;

public sealed record VersionProfile(
    string Id,
    string DisplayName,
    ModuleFingerprint Game,
    ModuleFingerprint Ares,
    SelectionLayout Selection,
    uint CurrentFrameAddress,
    uint EconomyOffset,
    uint DefeatedStateOffset,
    uint BuildQueueRootAddress,
    uint BuildQueueLimitOffset,
    uint AutoRepairOffset,
    SuperweaponExecutorDefinition SuperweaponExecutor,
    IReadOnlyDictionary<string, PatchDefinition> Patches,
    IReadOnlyDictionary<string, ConditionalFactoryHookDefinition> Hooks,
    IReadOnlyDictionary<string, SelectionVeterancyHookDefinition> SelectionHooks,
    IReadOnlyDictionary<string, PlayerTechUnlockHookDefinition> TechUnlockHooks);
