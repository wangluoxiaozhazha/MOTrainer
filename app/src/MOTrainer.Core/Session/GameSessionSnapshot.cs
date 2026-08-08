using MOTrainer.Core.Profiles;
using MOTrainer.Core.Localization;

namespace MOTrainer.Core.Session;

public sealed record GameSessionSnapshot(
    SessionState State,
    int? ProcessId,
    string Message,
    VersionProfile? Profile = null)
{
    public static GameSessionSnapshot NotRunning =>
        new(SessionState.NoProcess, null, CoreStrings.Get("GameNotRunning"));
}
