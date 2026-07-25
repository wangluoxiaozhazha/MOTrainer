namespace MOTrainer.Core.Session;

public enum SessionState
{
    NoProcess,
    ProcessFound,
    VersionVerified,
    ModulesReady,
    MatchReady,
    Unsupported,
    Error,
}
