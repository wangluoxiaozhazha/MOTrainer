namespace MOTrainer.Core.Patching;

public sealed record PatchTransitionResult(
    bool Success,
    PatchRuntimeState State,
    string Message);
