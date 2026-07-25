namespace MOTrainer.Core.Selection;

public sealed record SelectionLayout(
    uint ItemsAddress,
    uint CountAddress,
    uint CurrentPlayerAddress,
    uint OwnerOffset,
    uint HealthOffset,
    uint VeterancyOffset,
    int MaximumCount = 512)
{
    public static SelectionLayout ObservedMentalOmega { get; } = new(
        ItemsAddress: 0x00A8ECBC,
        CountAddress: 0x00A8ECC8,
        CurrentPlayerAddress: 0x00A83D4C,
        OwnerOffset: 0x21C,
        HealthOffset: 0x6C,
        VeterancyOffset: 0x150);
}
