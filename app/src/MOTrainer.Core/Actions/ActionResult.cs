namespace MOTrainer.Core.Actions;

public sealed record ActionResult(
    bool Success,
    string Message,
    int AffectedCount = 0)
{
    public static ActionResult Fail(string message) => new(false, message);
}
