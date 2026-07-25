namespace MOTrainer.Core.Memory;

public interface IRemoteCodeMemory
{
    bool TryAllocateRemotePage(int size, out uint address);

    bool TryMakeRemotePageExecutable(uint address, int size);

    bool TryFreeRemotePage(uint address);
}
