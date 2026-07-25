using MOTrainer.Core.Memory;

namespace MOTrainer.Core.SmokeTests;

internal sealed class FakeMemoryAccessor :
    IMemoryAccessor,
    IRemoteCodeMemory,
    IRemoteThreadExecutor
{
    private readonly Dictionary<uint, byte> memory = new();
    private readonly Dictionary<uint, int> allocations = new();
    private uint nextAllocationAddress = 0x10000000;

    public int ProcessId => 1234;

    public bool CanWrite => true;

    public bool IsDisposed { get; private set; }

    public uint? FailNextCodeWriteAddress { get; set; }

    public uint? FailNextDataWriteAddress { get; set; }

    public List<byte[]> ExecutedPayloads { get; } = [];

    public bool CompleteRemoteThreads { get; set; } = true;

    public int RemoteAllocationCount => allocations.Count;

    public void SetInt32(uint address, int value)
    {
        SetBytes(address, BitConverter.GetBytes(value));
    }

    public void SetUInt32(uint address, uint value)
    {
        SetBytes(address, BitConverter.GetBytes(value));
    }

    public void SetBytes(uint address, byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            memory[checked(address + (uint)index)] = bytes[index];
        }
    }

    public bool TryReadBytes(uint address, byte[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            if (!memory.TryGetValue(checked(address + (uint)index), out byte value))
            {
                return false;
            }

            destination[index] = value;
        }

        return true;
    }

    public bool TryWriteBytes(uint address, byte[] source)
    {
        if (FailNextDataWriteAddress == address)
        {
            FailNextDataWriteAddress = null;
            return false;
        }

        SetBytes(address, source);
        return true;
    }

    public bool TryWriteCodeBytes(uint address, byte[] source)
    {
        if (FailNextCodeWriteAddress == address)
        {
            FailNextCodeWriteAddress = null;
            return false;
        }

        return TryWriteBytes(address, source);
    }

    public bool TryAllocateRemotePage(int size, out uint address)
    {
        if (size <= 0)
        {
            address = 0;
            return false;
        }

        address = nextAllocationAddress;
        nextAllocationAddress = checked(
            nextAllocationAddress + (uint)Math.Max(size, 0x1000));
        allocations.Add(address, size);
        return true;
    }

    public bool TryMakeRemotePageExecutable(uint address, int size)
    {
        return allocations.TryGetValue(address, out int allocationSize) &&
            size > 0 &&
            size <= allocationSize;
    }

    public bool TryFreeRemotePage(uint address)
    {
        if (!allocations.Remove(address, out int size))
        {
            return false;
        }

        for (int offset = 0; offset < size; offset++)
        {
            memory.Remove(checked(address + (uint)offset));
        }

        return true;
    }

    public bool IsRemotePageAllocated(uint address)
    {
        return allocations.ContainsKey(address);
    }

    public RemoteThreadExecutionResult ExecuteRemoteThread(
        uint startAddress,
        int timeoutMilliseconds)
    {
        if (!allocations.TryGetValue(startAddress, out int size))
        {
            return new RemoteThreadExecutionResult(
                false,
                false,
                0,
                "fake start address is not allocated");
        }

        byte[] payload = new byte[size];
        if (!TryReadBytes(startAddress, payload))
        {
            return new RemoteThreadExecutionResult(
                false,
                false,
                0,
                "fake payload read failed");
        }
        ExecutedPayloads.Add(payload);
        return CompleteRemoteThreads
            ? new RemoteThreadExecutionResult(
                true,
                true,
                0,
                "fake thread completed")
            : new RemoteThreadExecutionResult(
                true,
                false,
                0,
                "fake thread timed out");
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
