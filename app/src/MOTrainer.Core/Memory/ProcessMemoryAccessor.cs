using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace MOTrainer.Core.Memory;

public sealed class ProcessMemoryAccessor :
    IMemoryAccessor,
    IRemoteCodeMemory,
    IRemoteThreadExecutor
{
    private readonly SafeProcessHandle processHandle;

    private ProcessMemoryAccessor(
        int processId,
        MemoryAccessMode mode,
        SafeProcessHandle processHandle)
    {
        ProcessId = processId;
        Mode = mode;
        this.processHandle = processHandle;
    }

    public int ProcessId { get; }

    public MemoryAccessMode Mode { get; }

    public bool CanWrite => Mode == MemoryAccessMode.ReadWrite;

    public static ProcessMemoryAccessor Open(
        int processId,
        MemoryAccessMode mode = MemoryAccessMode.ReadOnly)
    {
        NativeMethods.ProcessAccessRights rights =
            NativeMethods.ProcessAccessRights.QueryLimitedInformation |
            NativeMethods.ProcessAccessRights.VmRead;

        if (mode == MemoryAccessMode.ReadWrite)
        {
            rights |=
                NativeMethods.ProcessAccessRights.VmOperation |
                NativeMethods.ProcessAccessRights.VmWrite |
                NativeMethods.ProcessAccessRights.CreateThread;
        }

        SafeProcessHandle handle = NativeMethods.OpenProcess(rights, false, processId);
        if (handle.IsInvalid)
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return new ProcessMemoryAccessor(processId, mode, handle);
    }

    public bool TryReadBytes(uint address, byte[] destination)
    {
        if (destination.Length == 0)
        {
            return true;
        }

        bool succeeded = NativeMethods.ReadProcessMemory(
            processHandle,
            new IntPtr(address),
            destination,
            (nuint)destination.Length,
            out nuint bytesRead);

        return succeeded && bytesRead == (nuint)destination.Length;
    }

    public bool TryWriteBytes(uint address, byte[] source)
    {
        if (!CanWrite)
        {
            return false;
        }

        if (source.Length == 0)
        {
            return true;
        }

        bool succeeded = NativeMethods.WriteProcessMemory(
            processHandle,
            new IntPtr(address),
            source,
            (nuint)source.Length,
            out nuint bytesWritten);

        return succeeded && bytesWritten == (nuint)source.Length;
    }

    public bool TryWriteCodeBytes(uint address, byte[] source)
    {
        if (!CanWrite)
        {
            return false;
        }

        if (source.Length == 0)
        {
            return true;
        }

        IntPtr target = new(address);
        nuint size = (nuint)source.Length;

        if (!NativeMethods.VirtualProtectEx(
                processHandle,
                target,
                size,
                NativeMethods.PageExecuteReadWrite,
                out uint oldProtection))
        {
            return false;
        }

        bool operationSucceeded;
        bool protectionRestored;

        try
        {
            bool writeSucceeded = NativeMethods.WriteProcessMemory(
                processHandle,
                target,
                source,
                size,
                out nuint bytesWritten);
            bool cacheFlushed = writeSucceeded &&
                bytesWritten == size &&
                NativeMethods.FlushInstructionCache(
                    processHandle,
                    target,
                    size);

            operationSucceeded =
                writeSucceeded &&
                bytesWritten == size &&
                cacheFlushed;
        }
        finally
        {
            protectionRestored = NativeMethods.VirtualProtectEx(
                processHandle,
                target,
                size,
                oldProtection,
                out uint _);
        }

        return operationSucceeded && protectionRestored;
    }

    public bool TryAllocateRemotePage(int size, out uint address)
    {
        address = 0;
        if (!CanWrite || size <= 0)
        {
            return false;
        }

        IntPtr allocation = NativeMethods.VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (nuint)size,
            NativeMethods.MemCommitReserve,
            NativeMethods.PageReadWrite);

        long value = allocation.ToInt64();
        if (allocation == IntPtr.Zero ||
            value <= 0 ||
            value > uint.MaxValue)
        {
            if (allocation != IntPtr.Zero)
            {
                NativeMethods.VirtualFreeEx(
                    processHandle,
                    allocation,
                    0,
                    NativeMethods.MemRelease);
            }

            return false;
        }

        address = (uint)value;
        return true;
    }

    public bool TryMakeRemotePageExecutable(uint address, int size)
    {
        if (!CanWrite || address == 0 || size <= 0)
        {
            return false;
        }

        IntPtr target = new(address);
        return NativeMethods.VirtualProtectEx(
                processHandle,
                target,
                (nuint)size,
                NativeMethods.PageExecuteRead,
                out uint _) &&
            NativeMethods.FlushInstructionCache(
                processHandle,
                target,
                (nuint)size);
    }

    public bool TryFreeRemotePage(uint address)
    {
        return CanWrite &&
            address != 0 &&
            NativeMethods.VirtualFreeEx(
                processHandle,
                new IntPtr(address),
                0,
            NativeMethods.MemRelease);
    }

    public RemoteThreadExecutionResult ExecuteRemoteThread(
        uint startAddress,
        int timeoutMilliseconds)
    {
        if (!CanWrite || startAddress == 0 || timeoutMilliseconds < 0)
        {
            return new RemoteThreadExecutionResult(
                false,
                false,
                0,
                "远程线程参数无效");
        }

        IntPtr thread = NativeMethods.CreateRemoteThread(
            processHandle,
            IntPtr.Zero,
            0,
            new IntPtr(startAddress),
            IntPtr.Zero,
            0,
            IntPtr.Zero);
        if (thread == IntPtr.Zero)
        {
            return new RemoteThreadExecutionResult(
                false,
                false,
                0,
                $"创建远程线程失败：{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        try
        {
            uint wait = NativeMethods.WaitForSingleObject(
                thread,
                (uint)timeoutMilliseconds);
            if (wait == NativeMethods.WaitTimeout)
            {
                return new RemoteThreadExecutionResult(
                    true,
                    false,
                    0,
                    "远程线程等待超时");
            }
            if (wait != NativeMethods.WaitObject0 ||
                !NativeMethods.GetExitCodeThread(thread, out uint exitCode))
            {
                return new RemoteThreadExecutionResult(
                    true,
                    false,
                    0,
                    "远程线程等待或退出码读取失败");
            }

            return new RemoteThreadExecutionResult(
                true,
                true,
                exitCode,
                "远程线程已完成");
        }
        finally
        {
            NativeMethods.CloseHandle(thread);
        }
    }

    public void Dispose()
    {
        processHandle.Dispose();
    }
}
