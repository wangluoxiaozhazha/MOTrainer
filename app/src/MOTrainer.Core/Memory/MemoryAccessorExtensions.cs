using System.Runtime.InteropServices;

namespace MOTrainer.Core.Memory;

public static class MemoryAccessorExtensions
{
    public static bool TryRead<T>(
        this IMemoryAccessor memory,
        uint address,
        out T value)
        where T : unmanaged
    {
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        if (!memory.TryReadBytes(address, bytes))
        {
            value = default;
            return false;
        }

        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            value = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            return true;
        }
        finally
        {
            handle.Free();
        }
    }

    public static bool TryWrite<T>(
        this IMemoryAccessor memory,
        uint address,
        in T value)
        where T : unmanaged
    {
        T copy = value;
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(copy, handle.AddrOfPinnedObject(), false);
        }
        finally
        {
            handle.Free();
        }

        return memory.TryWriteBytes(address, bytes);
    }
}
