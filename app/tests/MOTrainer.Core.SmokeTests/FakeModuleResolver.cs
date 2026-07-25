using MOTrainer.Core.Processes;

namespace MOTrainer.Core.SmokeTests;

internal sealed class FakeModuleResolver : IModuleResolver
{
    private readonly Dictionary<string, uint> modules =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(string moduleName, uint moduleBase)
    {
        modules[moduleName] = moduleBase;
    }

    public bool TryGetModuleBase(
        int processId,
        string moduleName,
        out uint moduleBase)
    {
        return modules.TryGetValue(moduleName, out moduleBase);
    }
}
