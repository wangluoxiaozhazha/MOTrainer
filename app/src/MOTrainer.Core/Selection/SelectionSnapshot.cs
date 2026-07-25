namespace MOTrainer.Core.Selection;

public sealed record SelectionSnapshot(
    SelectionReadStatus Status,
    uint CurrentPlayer,
    IReadOnlyList<uint> Objects)
{
    public bool IsSuccess => Status == SelectionReadStatus.Success;
}
