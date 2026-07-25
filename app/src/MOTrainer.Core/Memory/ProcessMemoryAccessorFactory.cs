namespace MOTrainer.Core.Memory;

public sealed class ProcessMemoryAccessorFactory : IMemoryAccessorFactory
{
    public static ProcessMemoryAccessorFactory Instance { get; } = new();

    private ProcessMemoryAccessorFactory()
    {
    }

    public IMemoryAccessor Open(
        int processId,
        MemoryAccessMode mode = MemoryAccessMode.ReadOnly)
    {
        return ProcessMemoryAccessor.Open(processId, mode);
    }
}
