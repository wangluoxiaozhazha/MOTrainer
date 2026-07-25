using MOTrainer.Core.Memory;

namespace MOTrainer.Core.SmokeTests;

internal sealed class FakeMemoryAccessorFactory : IMemoryAccessorFactory
{
    private readonly IMemoryAccessor memory;

    public FakeMemoryAccessorFactory(IMemoryAccessor memory)
    {
        this.memory = memory;
    }

    public IMemoryAccessor Open(
        int processId,
        MemoryAccessMode mode = MemoryAccessMode.ReadOnly)
    {
        return memory;
    }
}
