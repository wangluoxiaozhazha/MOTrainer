using MOTrainer.Core.Profiles;

namespace MOTrainer.Core.Session;

public sealed record GameSessionSnapshot(
    SessionState State,
    int? ProcessId,
    string Message,
    VersionProfile? Profile = null)
{
    public static GameSessionSnapshot NotRunning { get; } =
        new(SessionState.NoProcess, null, "游戏未启动");
}
