using MOTrainer.Core.Patching;

namespace MOTrainer.Core.Hooks;

public enum TechUnlockMode
{
    Off,
    Faction,
    Extended,
}

public sealed record TechUnlockModeInspection(
    TechUnlockMode? Mode,
    PatchRuntimeState State,
    string Message);
