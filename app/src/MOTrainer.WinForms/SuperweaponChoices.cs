namespace MOTrainer.WinForms;

internal sealed record SuperweaponChoice(
    uint Id,
    string DisplayName);

internal static class SuperweaponChoices
{
    public static IReadOnlyList<SuperweaponChoice> All { get; } =
    [
        new(0, "核弹"),
        new(2, "闪电"),
        new(7, "心控"),
        new(48, "顶点"),
        new(1, "铁幕"),
        new(3, "超时空"),
        new(28, "基因"),
        new(47, "屏障"),
    ];

    public static IReadOnlyCollection<uint> KnownIds { get; } =
        new HashSet<uint>(All.Select(choice => choice.Id));
}
