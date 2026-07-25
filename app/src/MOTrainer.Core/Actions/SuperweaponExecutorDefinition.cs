namespace MOTrainer.Core.Actions;

public sealed record SuperweaponExecutorDefinition(
    byte[] Template,
    int FirstParameterOffset,
    int SecondParameterOffset,
    uint MaximumId,
    uint[] SafeIds)
{
    public void Validate()
    {
        if (Template.Length == 0 ||
            FirstParameterOffset < 0 ||
            SecondParameterOffset < 0 ||
            FirstParameterOffset + sizeof(uint) > Template.Length ||
            SecondParameterOffset + sizeof(uint) > Template.Length ||
            MaximumId == 0 ||
            SafeIds.Length == 0 ||
            SafeIds.Any(id => id > MaximumId) ||
            SafeIds.Distinct().Count() != SafeIds.Length)
        {
            throw new InvalidOperationException(
                "超武执行器定义无效");
        }
    }
}
