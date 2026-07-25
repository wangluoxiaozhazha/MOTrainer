namespace MOTrainer.Core.Memory;

public interface IRemoteThreadExecutor
{
    RemoteThreadExecutionResult ExecuteRemoteThread(
        uint startAddress,
        int timeoutMilliseconds);
}

public sealed record RemoteThreadExecutionResult(
    bool Started,
    bool Completed,
    uint ExitCode,
    string Message);
