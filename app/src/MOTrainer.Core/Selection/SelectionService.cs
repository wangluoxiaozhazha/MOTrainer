using MOTrainer.Core.Memory;

namespace MOTrainer.Core.Selection;

public sealed class SelectionService
{
    private readonly IMemoryAccessor memory;
    private readonly SelectionLayout layout;

    public SelectionService(IMemoryAccessor memory, SelectionLayout layout)
    {
        this.memory = memory;
        this.layout = layout;
    }

    public SelectionSnapshot Read(
        SelectionScope scope,
        bool currentPlayerOnly)
    {
        if (!memory.TryRead(layout.CountAddress, out int count) ||
            count < 0 ||
            count > layout.MaximumCount)
        {
            return new SelectionSnapshot(
                SelectionReadStatus.InvalidCount,
                0,
                Array.Empty<uint>());
        }

        if (count == 0)
        {
            return new SelectionSnapshot(
                SelectionReadStatus.Success,
                0,
                Array.Empty<uint>());
        }

        if (!memory.TryRead(layout.ItemsAddress, out uint itemsAddress) ||
            itemsAddress == 0)
        {
            return new SelectionSnapshot(
                SelectionReadStatus.ItemsUnavailable,
                0,
                Array.Empty<uint>());
        }

        uint currentPlayer = 0;
        if (currentPlayerOnly &&
            (!memory.TryRead(layout.CurrentPlayerAddress, out currentPlayer) ||
             currentPlayer == 0))
        {
            return new SelectionSnapshot(
                SelectionReadStatus.CurrentPlayerUnavailable,
                0,
                Array.Empty<uint>());
        }

        int itemsToRead = scope == SelectionScope.First ? Math.Min(count, 1) : count;
        List<uint> objects = new(itemsToRead);

        for (int index = 0; index < itemsToRead; index++)
        {
            uint itemAddress = checked(itemsAddress + ((uint)index * sizeof(uint)));
            if (!memory.TryRead(itemAddress, out uint objectAddress) ||
                objectAddress == 0)
            {
                continue;
            }

            if (currentPlayerOnly)
            {
                uint ownerAddress = checked(objectAddress + layout.OwnerOffset);
                if (!memory.TryRead(ownerAddress, out uint owner) ||
                    owner != currentPlayer)
                {
                    continue;
                }
            }

            objects.Add(objectAddress);
        }

        return new SelectionSnapshot(
            SelectionReadStatus.Success,
            currentPlayer,
            objects);
    }
}
