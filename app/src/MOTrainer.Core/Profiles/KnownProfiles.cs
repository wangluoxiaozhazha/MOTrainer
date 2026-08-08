using MOTrainer.Core.Selection;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Actions;

namespace MOTrainer.Core.Profiles;

public static class KnownProfiles
{
    public const string ObservedMentalOmegaId = "mo-observed-2026-07-24";

    public static VersionProfile ObservedMentalOmega { get; } = new(
        Id: ObservedMentalOmegaId,
        DisplayName: "Mental Omega 已验证样本",
        Game: new ModuleFingerprint(
            "gamemd.exe",
            "7CD005D263FDE203D9C84548200A057A8DF61D724DA3C6BD1E521EEB61CD0747"),
        Ares: new ModuleFingerprint(
            "Ares.dll",
            "93CE890F387A6C1B3240A2DD7B674F25FEC5A0E96F0B0D0306796B2EDF7B5B83"),
        Selection: SelectionLayout.ObservedMentalOmega,
        CurrentFrameAddress: 0x00A8ED84,
        EconomyOffset: 0x30C,
        DefeatedStateOffset: 0x1F6,
        BuildQueueRootAddress: 0x008871E0,
        BuildQueueLimitOffset: 0xF0,
        AutoRepairOffset: 0x1444,
        SuperweaponExecutor: new SuperweaponExecutorDefinition(
            Template:
            [
                0x60, 0xB9, 0x14, 0x00, 0x00, 0x00,
                0xBA, 0xB0, 0xEE, 0x6C, 0x00, 0xFF, 0xD2, 0x89, 0xC3,
                0xB8, 0x00, 0x00, 0x00, 0x00,
                0x8B, 0x15, 0x4C, 0x3D, 0xA8, 0x00,
                0x8B, 0x8A, 0x58, 0x02, 0x00, 0x00,
                0x6A, 0x00, 0x6A, 0x00, 0x8B, 0x0C, 0x81, 0x6A, 0x01,
                0xB8, 0x60, 0xB5, 0x6C, 0x00, 0xFF, 0xD0,
                0xB8, 0x00, 0x00, 0x00, 0x00,
                0xB9, 0xE8, 0xF7, 0x87, 0x00,
                0x50, 0x6A, 0x1F,
                0xBA, 0x00, 0x63, 0x6A, 0x00, 0xFF, 0xD2, 0x61, 0xC3,
            ],
            FirstParameterOffset: 16,
            SecondParameterOffset: 49,
            MaximumId: 59,
            SafeIds: [0, 1, 2, 3, 5, 7, 28, 47, 48]),
        Patches: new Dictionary<string, PatchDefinition>
        {
            [KnownPatchIds.UnitUnlimited] = new PatchDefinition(
                KnownPatchIds.UnitUnlimited,
                [
                    new PatchSite(
                        "gamemd.exe",
                        0x000F812A,
                        [0x8B, 0x83, 0xB8, 0x03, 0x00, 0x00],
                        [0xB8, 0xFF, 0xFF, 0xFF, 0x7F, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x0010B50A,
                        [0x8B, 0xB6, 0xB8, 0x03, 0x00, 0x00],
                        [0xBE, 0xFF, 0xFF, 0xFF, 0x7F, 0x90]),
                    new PatchSite(
                        "Ares.dll",
                        0x00021BEB,
                        [0x8B, 0xB3, 0xB8, 0x03, 0x00, 0x00],
                        [0xBE, 0xFF, 0xFF, 0xFF, 0x7F, 0x90]),
                    new PatchSite(
                        "Ares.dll",
                        0x00020876,
                        [0x8B, 0xB0, 0xB8, 0x03, 0x00, 0x00],
                        [0xBE, 0xFF, 0xFF, 0xFF, 0x7F, 0x90]),
                ]),
            [KnownPatchIds.EconomyNoDecrease] = new PatchDefinition(
                KnownPatchIds.EconomyNoDecrease,
                [
                    new PatchSite(
                        ModuleName: "gamemd.exe",
                        Rva: 0x000F98B9,
                        OriginalBytes: [0x2B, 0xC7],
                        EnabledBytes: [0x90, 0x90]),
                ]),
            [KnownPatchIds.PowerNoDecrease] = new PatchDefinition(
                KnownPatchIds.PowerNoDecrease,
                [
                    new PatchSite(
                        ModuleName: "gamemd.exe",
                        Rva: 0x00108D18,
                        OriginalBytes: [0x89, 0x96, 0xA8, 0x53, 0x00, 0x00],
                        EnabledBytes: [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),
                ]),
            [KnownPatchIds.ControlAllUnits] = new PatchDefinition(
                KnownPatchIds.ControlAllUnits,
                [
                    new PatchSite(
                        ModuleName: "gamemd.exe",
                        Rva: 0x0010B71E,
                        OriginalBytes: [0x3B, 0xCA],
                        EnabledBytes: [0x90, 0x90]),
                ]),
            [KnownPatchIds.IgnoreBuildDistance] = new PatchDefinition(
                KnownPatchIds.IgnoreBuildDistance,
                [
                    new PatchSite(
                        "gamemd.exe",
                        0x000ABAAC,
                        [0x0F, 0x84, 0xC4, 0x01, 0x00, 0x00],
                        [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x000ABABA,
                        [0x0F, 0x84, 0xB6, 0x01, 0x00, 0x00],
                        [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),
                ]),
            [KnownPatchIds.PsychicDetection] = new PatchDefinition(
                KnownPatchIds.PsychicDetection,
                [
                    new PatchSite(
                        "gamemd.exe",
                        0x000414A0,
                        [0x8B, 0x88, 0x0C, 0x17, 0x00, 0x00],
                        [0xB9, 0xFF, 0x0F, 0x00, 0x00, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x0003B3E9,
                        [0x8B, 0x91, 0x0C, 0x17, 0x00, 0x00],
                        [0xBA, 0xFF, 0x0F, 0x00, 0x00, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x000566BD,
                        [0x8B, 0x81, 0x0C, 0x17, 0x00, 0x00],
                        [0xB8, 0xFF, 0x0F, 0x00, 0x00, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x0003B47C,
                        [0x8B, 0x91, 0x0C, 0x17, 0x00, 0x00],
                        [0xBA, 0xFF, 0x0F, 0x00, 0x00, 0x90]),
                ]),
            [KnownPatchIds.IgnoreCloak] = new PatchDefinition(
                KnownPatchIds.IgnoreCloak,
                [
                    new PatchSite(
                        "gamemd.exe",
                        0x00054550,
                        [0x75, 0xDB],
                        [0xEB, 0xDB]),
                    new PatchSite(
                        "gamemd.exe",
                        0x00057194,
                        [0x74, 0x38],
                        [0xEB, 0x2E]),
                    new PatchSite(
                        "gamemd.exe",
                        0x003039FA,
                        [0x74, 0x0D],
                        [0x90, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x0030D392,
                        [0x74, 0x73],
                        [0x90, 0x90]),
                    new PatchSite(
                        "gamemd.exe",
                        0x0030D3A8,
                        [0x75, 0x23],
                        [0xEB, 0x23]),
                ]),
            [KnownPatchIds.StableRevealMap] = new PatchDefinition(
                KnownPatchIds.StableRevealMap,
                [
                    new PatchSite(
                        "gamemd.exe",
                        0x002D88FC,
                        [0x75, 0x05],
                        [0xEB, 0x05]),
                    new PatchSite(
                        "gamemd.exe",
                        0x00108F43,
                        [0x74, 0x0F, 0x8B, 0x44, 0x24, 0x00],
                        [0x90, 0x90, 0xB0, 0x01, 0x90, 0x90]),
                ]),
        },
        Hooks: new Dictionary<string, ConditionalFactoryHookDefinition>
        {
            [KnownPatchIds.InstantUnitBuild] =
                new ConditionalFactoryHookDefinition(
                    Id: KnownPatchIds.InstantUnitBuild,
                    ModuleName: "gamemd.exe",
                    Rva: 0x000C9B7B,
                    OriginalBytes:
                    [
                        0x8B, 0x56, 0x24, 0x03, 0xD1,
                        0xB3, 0x01, 0x89, 0x56, 0x24,
                    ],
                    CurrentPlayerAddress: 0x00A83D4C,
                    OwnerOffset: 0x6C,
                    ProgressOffset: 0x24,
                    CompletedProgress: 0x36),
        },
        SelectionHooks:
            new Dictionary<string, SelectionVeterancyHookDefinition>
            {
                [KnownPatchIds.AutoPromoteSelection] =
                new SelectionVeterancyHookDefinition(
                    Id: KnownPatchIds.AutoPromoteSelection,
                    ModuleName: "gamemd.exe",
                    Sites:
                    [
                        new SelectionHookSite(
                            Rva: 0x001F46AE,
                            OriginalBytes:
                            [
                                0xC6, 0x86, 0x83, 0x00,
                                0x00, 0x00, 0x01,
                            ]),
                        new SelectionHookSite(
                            Rva: 0x001F4718,
                            OriginalBytes:
                            [
                                0xC6, 0x86, 0x83, 0x00,
                                0x00, 0x00, 0x01,
                            ]),
                    ],
                    CurrentPlayerAddress: 0x00A83D4C,
                    OwnerOffset: 0x21C,
                    VeterancyOffset: 0x150,
                    HealthOffset: 0x6C),
            },
        TechUnlockHooks: new Dictionary<string, PlayerTechUnlockHookDefinition>
        {
            [KnownPatchIds.SafeTechUnlock] =
                new PlayerTechUnlockHookDefinition(
                    Id: KnownPatchIds.SafeTechUnlock,
                    ModuleName: "Ares.dll",
                    Rva: 0x0002204E,
                    OriginalBytes:
                    [
                        0x8B, 0x47, 0x34,
                        0x8B, 0x4F, 0x38,
                    ],
                    CurrentPlayerAddress: 0x00A83D4C,
                    RecheckTechTreeOffset: 0x1FC,
                    OverriddenReturnRva: 0x00022332,
                    FactoryOwnerRva: 0x00020E18,
                    FactoryOwnerOriginalBytes:
                    [
                        0x85, 0x44, 0x24, 0x10,
                        0x0F, 0x84, 0x84, 0x00, 0x00, 0x00,
                    ],
                    FactoryObjectOwnerOffset: 0x21C,
                    FactoryOwnerAllowedRva: 0x00020E22,
                    FactoryOwnerRejectedRva: 0x00020EA6),
        });

    public static IReadOnlyList<VersionProfile> All { get; } =
        [ObservedMentalOmega];
}
