namespace MOTrainer.Core.Patching;

public sealed record PatchInspection(
    PatchRuntimeState State,
    string Message);
