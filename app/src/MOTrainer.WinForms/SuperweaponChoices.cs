namespace MOTrainer.WinForms;

internal sealed record SuperweaponChoice(
    uint Id,
    string DisplayName);

internal static class SuperweaponChoices
{
    public static IReadOnlyList<SuperweaponChoice> All =>
    [
        new(0, UiStrings.Get("SuperweaponNuclearMissile")),
        new(2, UiStrings.Get("SuperweaponLightningStorm")),
        new(7, UiStrings.Get("SuperweaponPsychicDominator")),
        new(48, UiStrings.Get("SuperweaponMentalOmega")),
        new(1, UiStrings.Get("SuperweaponIronCurtain")),
        new(3, UiStrings.Get("SuperweaponChronosphere")),
        new(28, UiStrings.Get("SuperweaponGeneticMutator")),
        new(47, UiStrings.Get("SuperweaponBarrier")),
    ];

    public static IReadOnlyCollection<uint> KnownIds { get; } =
        new HashSet<uint>(All.Select(choice => choice.Id));
}
