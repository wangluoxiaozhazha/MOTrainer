namespace MOTrainer.Core.Memory;

public interface IMemoryAccessorFactory
{
    IMemoryAccessor Open(
        int processId,
        MemoryAccessMode mode = MemoryAccessMode.ReadOnly);
}
