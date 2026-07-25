namespace MOTrainer.Core.Processes;

public interface IModuleResolver
{
    bool TryGetModuleBase(
        int processId,
        string moduleName,
        out uint moduleBase);
}
