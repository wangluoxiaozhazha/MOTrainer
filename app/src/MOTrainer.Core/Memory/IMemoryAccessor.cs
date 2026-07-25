namespace MOTrainer.Core.Memory;

public interface IMemoryAccessor : IDisposable
{
    int ProcessId { get; }

    bool CanWrite { get; }

    bool TryReadBytes(uint address, byte[] destination);

    bool TryWriteBytes(uint address, byte[] source);

    bool TryWriteCodeBytes(uint address, byte[] source);
}
