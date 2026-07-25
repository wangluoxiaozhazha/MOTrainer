using System.Diagnostics;

namespace MOTrainer.Core.Processes;

public sealed class ProcessModuleResolver : IModuleResolver
{
    public static ProcessModuleResolver Instance { get; } = new();
    private readonly Dictionary<ModuleIdentity, uint> moduleCache = new();
    private readonly object cacheGate = new();

    private ProcessModuleResolver()
    {
    }

    public bool TryGetModuleBase(
        int processId,
        string moduleName,
        out uint moduleBase)
    {
        moduleBase = 0;

        try
        {
            using Process process = Process.GetProcessById(processId);
            ModuleIdentity identity = new(
                processId,
                process.StartTime.ToUniversalTime().Ticks,
                moduleName.ToUpperInvariant());
            lock (cacheGate)
            {
                if (moduleCache.TryGetValue(identity, out moduleBase))
                {
                    return true;
                }
            }

            foreach (ProcessModule module in process.Modules)
            {
                if (!string.Equals(
                        module.ModuleName,
                        moduleName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long address = module.BaseAddress.ToInt64();
                if (address <= 0 || address > uint.MaxValue)
                {
                    return false;
                }

                moduleBase = (uint)address;
                lock (cacheGate)
                {
                    moduleCache[identity] = moduleBase;
                }

                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private readonly record struct ModuleIdentity(
        int ProcessId,
        long StartTimeTicks,
        string ModuleName);
}
