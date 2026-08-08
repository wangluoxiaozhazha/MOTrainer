using MOTrainer.Core.Actions;
using MOTrainer.Core.Features;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Selection;
using MOTrainer.Core.Session;
using MOTrainer.WinForms;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Resources;
using System.Xml.Linq;

namespace MOTrainer.Core.SmokeTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--live-probe", StringComparer.OrdinalIgnoreCase))
        {
            return RunLiveProbe();
        }

        (string Name, Action Test)[] tests =
        [
            ("SelectionService filters final vector by current player", SelectionServiceFiltersByOwner),
            ("SelectionService rejects unreasonable count", SelectionServiceRejectsLargeCount),
            ("Patch byte classifier is fail-closed", PatchClassifierIsFailClosed),
            ("Module fingerprint matching is normalized", FingerprintMatchingIsNormalized),
            ("Display language resolution is backward compatible", DisplayLanguageResolutionIsBackwardCompatible),
            ("WinForms English resources cover the visible surface", WinFormsEnglishResourcesCoverSurface),
            ("Core resources cover runtime messages in both languages", CoreResourcesCoverRuntimeMessages),
            ("Session probe localizes startup and access messages", SessionProbeLocalizesMessages),
            ("Session probe skips inaccessible process candidates", SessionProbeSkipsInaccessibleCandidate),
            ("Session probe explains access-level mismatch", SessionProbeExplainsAccessDenied),
            ("Session probe distinguishes module enumeration failure", SessionProbeExplainsModuleFailure),
            ("Read-only patch inspection reuses a supplied session snapshot", PatchInspectionReusesSnapshot),
            ("Money action follows CurrentPlayer economy chain", MoneyActionUsesVerifiedChain),
            ("Promotion action traverses every selected friendly unit", PromoteActionTraversesFinalVector),
            ("Selected-unit editor combines rank and health for friendly units", SelectedUnitEditorCombinesAttributes),
            ("Selected-unit editor reapplies values for automatic mode", SelectedUnitEditorReappliesValues),
            ("Selected-unit editor rolls back a failed combined write", SelectedUnitEditorRollsBack),
            ("CTRL+S action adds 2000 health to all selected units", AddHealthUsesSelectedUnits),
            ("CTRL+D action sets all selected health to one", SetHealthUsesSelectedUnits),
            ("CTRL+F action transfers all selected unit ownership", TransferActionUsesSelectedOwnerChain),
            ("CTRL+O action zeros the selected owner's economy", ZeroEconomyUsesSelectedOwnerChain),
            ("CTRL+SPACE action marks the selected owner defeated", DefeatActionUsesSelectedOwnerChain),
            ("Batch 2 data writes fail without changing the target", BatchTwoWriteFailureKeepsOriginal),
            ("CTRL+T automatic repair enables and restores the persistent field", AutoRepairRoundTrips),
            ("Economy patch enables and restores original bytes", EconomyPatchRoundTrips),
            ("Batch 1 static patches enable and restore original bytes", BatchOnePatchesRoundTrip),
            ("Batch 3 multi-site patches enable and restore every site", BatchThreePatchesRoundTrip),
            ("Player-only instant build hook installs, reattaches and restores", PlayerInstantBuildHookRoundTrips),
            ("Selection edit event hook installs configured fields, reattaches and restores", SelectionVeterancyEventHookRoundTrips),
            ("Player-only safe tech unlock hook installs, reattaches and restores", PlayerTechUnlockHookRoundTrips),
            ("Delayed unit unlimited policy applies and restores", DelayedUnitUnlimitedRoundTrips),
            ("Delayed queue policy waits, applies and restores", DelayedQueueLimitRoundTrips),
            ("Superweapon executor parameterizes, serializes and frees", SuperweaponExecutorRoundTrips),
            ("Economy patch rejects unknown bytes", EconomyPatchRejectsUnknownBytes),
            ("Patch transaction rolls back earlier sites", PatchTransactionRollsBack),
            ("Hotkey gestures parse and reject conflicts", HotkeyGesturesRoundTrip),
            ("Trainer settings persist and fall back safely", TrainerSettingsRoundTrip),
        ];

        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL  {name}");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        Console.WriteLine($"All {tests.Length} smoke tests passed.");
        return 0;
    }

    private static int RunLiveProbe()
    {
        GameSessionProbe probe = new();
        GameSessionSnapshot snapshot = probe.Probe();
        Console.WriteLine($"SessionState={snapshot.State}");
        Console.WriteLine($"ProcessId={snapshot.ProcessId?.ToString() ?? "none"}");
        Console.WriteLine($"Profile={snapshot.Profile?.Id ?? "none"}");
        Console.WriteLine($"Message={snapshot.Message}");

        if (snapshot.State is not (SessionState.ModulesReady or SessionState.MatchReady))
        {
            return 1;
        }

        PatchInspection patch =
            new GameActionService(probe).InspectEconomyNoDecrease();
        Console.WriteLine($"EconomyPatchState={patch.State}");
        Console.WriteLine($"EconomyPatchMessage={patch.Message}");

        PatchInspection instantBuild =
            new GameActionService(probe).InspectPatch(
                KnownPatchIds.InstantUnitBuild);
        Console.WriteLine($"InstantBuildHookState={instantBuild.State}");
        Console.WriteLine($"InstantBuildHookMessage={instantBuild.Message}");

        PatchInspection safeTechUnlock =
            new GameActionService(probe).InspectPatch(
                KnownPatchIds.SafeTechUnlock);
        Console.WriteLine($"SafeTechUnlockHookState={safeTechUnlock.State}");
        Console.WriteLine($"SafeTechUnlockHookMessage={safeTechUnlock.Message}");

        bool economyKnown =
            patch.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
        bool instantBuildKnown =
            instantBuild.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
        bool safeTechUnlockKnown =
            safeTechUnlock.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
        bool batchThreeKnown = true;
        foreach (string patchId in new[]
        {
            KnownPatchIds.IgnoreBuildDistance,
            KnownPatchIds.PsychicDetection,
            KnownPatchIds.IgnoreCloak,
            KnownPatchIds.StableRevealMap,
        })
        {
            PatchInspection inspection =
                new GameActionService(probe).InspectPatch(patchId);
            Console.WriteLine($"{patchId}={inspection.State}: {inspection.Message}");
            batchThreeKnown &=
                inspection.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
        }

        return economyKnown &&
            instantBuildKnown &&
            safeTechUnlockKnown &&
            batchThreeKnown
            ? 0
            : 1;
    }

    private static void PatchInspectionReusesSnapshot()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        GameSessionSnapshot snapshot = new(
            SessionState.MatchReady,
            1234,
            "ready",
            profile);
        FakeSessionProbe probe = new(snapshot);
        using FakeMemoryAccessor memory = new();
        GameActionService actions = new(
            probe,
            new FakeMemoryAccessorFactory(memory));

        PatchInspection shared =
            actions.InspectPatch("missing-read-only-patch", snapshot);
        Assert(
            shared.State == PatchRuntimeState.Unknown,
            "The supplied snapshot should reach patch configuration lookup.");
        Assert(
            probe.ProbeCount == 0,
            "Supplying a verified snapshot must not probe the process again.");

        actions.InspectPatch("missing-read-only-patch");
        Assert(
            probe.ProbeCount == 1,
            "The standalone inspection entry point must still verify its own session.");
    }

    private static void SessionProbeSkipsInaccessibleCandidate()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();
        memory.SetUInt32(
            profile.Selection.CurrentPlayerAddress,
            0x00200000);
        memory.SetInt32(profile.CurrentFrameAddress, 123);

        GameSessionProbe probe = CreateSessionProbe(
            memory,
            [100, 200],
            processId => processId == 100
                ? throw new UnauthorizedAccessException("denied")
                : @"C:\game\gamemd.exe",
            (_, moduleName) => @"C:\game\" + moduleName);

        GameSessionSnapshot snapshot = probe.Probe();
        Assert(
            snapshot.State == SessionState.MatchReady,
            "An inaccessible lower-PID process must not hide an accessible supported game.");
        Assert(
            snapshot.ProcessId == 200,
            "The probe should select the accessible supported candidate.");
    }

    private static void DisplayLanguageResolutionIsBackwardCompatible()
    {
        Assert(
            DisplayCulture.Resolve(
                DisplayLanguage.Auto,
                CultureInfo.GetCultureInfo("zh-TW")).Name == "zh-Hans",
            "Automatic language should use Simplified Chinese on Chinese Windows.");
        Assert(
            DisplayCulture.Resolve(
                DisplayLanguage.Auto,
                CultureInfo.GetCultureInfo("en-AU")).Name == "en",
            "Automatic language should use English on non-Chinese Windows.");
        Assert(
            DisplayCulture.Resolve(
                DisplayLanguage.English,
                CultureInfo.GetCultureInfo("zh-CN")).Name == "en",
            "An explicit language should override the installed UI culture.");

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "motrainer-language-" + Guid.NewGuid().ToString("N") + ".config");
        try
        {
            File.WriteAllText(
                path,
                "<configuration><appSettings>" +
                "<add key=\"ui.language\" value=\"English\" />" +
                "</appSettings></configuration>");
            Assert(
                DisplayCulture.ReadConfiguredLanguage(path) ==
                    DisplayLanguage.English,
                "The bootstrap reader should honor a persisted explicit language.");

            File.WriteAllText(path, "<configuration>");
            Assert(
                DisplayCulture.ReadConfiguredLanguage(path) ==
                    DisplayLanguage.Auto,
                "A malformed legacy config should fail safely to Automatic.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void SessionProbeLocalizesMessages()
    {
        RunWithUiCulture("en", () =>
        {
            Assert(
                GameSessionSnapshot.NotRunning.Message ==
                    "Game is not running",
                "The invariant startup status should be English.");

            using FakeMemoryAccessor memory = new();
            GameSessionProbe deniedProbe = CreateSessionProbe(
                memory,
                [321],
                _ => throw new UnauthorizedAccessException("denied"),
                (_, _) => null);
            Assert(
                deniedProbe.Probe().Message.Contains(
                    "insufficient permission",
                    StringComparison.Ordinal),
                "The access refusal should be understandable in English.");
        });

        RunWithUiCulture("zh-Hans", () =>
        {
            Assert(
                GameSessionSnapshot.NotRunning.Message == "游戏未启动",
                "The startup status should use the Simplified Chinese satellite resource.");

            using FakeMemoryAccessor memory = new();
            GameSessionProbe deniedProbe = CreateSessionProbe(
                memory,
                [321],
                _ => throw new UnauthorizedAccessException("denied"),
                (_, _) => null);
            Assert(
                deniedProbe.Probe().Message.Contains(
                    "权限不足",
                    StringComparison.Ordinal),
                "The access refusal should remain available in Simplified Chinese.");
        });
    }

    private static void WinFormsEnglishResourcesCoverSurface()
    {
        RunWithUiCulture("en", () =>
        {
            string[] visibleText =
            [
                UiStrings.Get("AppTitle"),
                UiStrings.Get("SelectedUnitsGroup"),
                UiStrings.Get("GlobalStateGroup"),
                UiStrings.Get("OneTimeGroup"),
                UiStrings.Get("HotkeySettingsTitle"),
                UiStrings.Get("SettingsReadFailed"),
                .. HotkeyDefinitions.All.Select(item => item.DisplayName),
                .. SuperweaponChoices.All.Select(item => item.DisplayName),
            ];
            Assert(
                visibleText.All(text =>
                    !Regex.IsMatch(text, @"\p{IsCJKUnifiedIdeographs}")),
                "The English WinForms surface should not contain Chinese labels.");
        });
    }

    private static void CoreResourcesCoverRuntimeMessages()
    {
        ResourceManager resources = new(
            "MOTrainer.Core.Resources.CoreStrings",
            typeof(CoreStrings).Assembly);
        ResourceSet english = resources.GetResourceSet(
            CultureInfo.GetCultureInfo("en"),
            createIfNotExists: true,
            tryParents: true) ?? throw new InvalidOperationException(
                "English Core resource set is unavailable.");
        ResourceSet chinese = resources.GetResourceSet(
            CultureInfo.GetCultureInfo("zh-Hans"),
            createIfNotExists: true,
            tryParents: false) ?? throw new InvalidOperationException(
                "Simplified Chinese Core resource set is unavailable.");

        Dictionary<string, string> englishEntries = english
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(
                entry => (string)entry.Key,
                entry => entry.Value as string ??
                    throw new InvalidOperationException(
                        $"English Core resource {entry.Key} is not text."));
        Dictionary<string, string> chineseEntries = chinese
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(
                entry => (string)entry.Key,
                entry => entry.Value as string ??
                    throw new InvalidOperationException(
                        $"Simplified Chinese Core resource {entry.Key} is not text."));

        Assert(
            englishEntries.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(chineseEntries.Keys),
            "English and Simplified Chinese Core resources must contain the same keys.");
        Assert(
            englishEntries.Values.All(text =>
                !Regex.IsMatch(text, @"\p{IsCJKUnifiedIdeographs}")),
            "English Core runtime messages should not contain Chinese text.");
        Assert(
            englishEntries.All(entry =>
                PlaceholderIndexes(entry.Value).SetEquals(
                    PlaceholderIndexes(chineseEntries[entry.Key]))),
            "Core resource placeholders must match between English and Simplified Chinese.");
    }

    private static HashSet<string> PlaceholderIndexes(string template) =>
        Regex.Matches(template, @"\{(\d+)(?:[^}]*)\}")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static void SessionProbeExplainsAccessDenied()
    {
        RunWithUiCulture("zh-Hans", () =>
        {
            using FakeMemoryAccessor memory = new();
            GameSessionProbe probe = CreateSessionProbe(
                memory,
                [321],
                _ => throw new UnauthorizedAccessException("denied"),
                (_, _) => null);

            GameSessionSnapshot snapshot = probe.Probe();
            Assert(
                snapshot.State == SessionState.Error &&
                snapshot.ProcessId == 321,
                "An inaccessible process should remain identified in the error snapshot.");
            Assert(
                snapshot.Message.Contains("权限不足", StringComparison.Ordinal) &&
                snapshot.Message.Contains("相同权限级别", StringComparison.Ordinal),
                "Access denied should explain the integrity-level mismatch and recovery action.");
        });
    }

    private static void SessionProbeExplainsModuleFailure()
    {
        using FakeMemoryAccessor memory = new();
        GameSessionProbe probe = CreateSessionProbe(
            memory,
            [456],
            _ => @"C:\game\gamemd.exe",
            (_, _) => throw new InvalidOperationException("module read failed"));

        GameSessionSnapshot snapshot = probe.Probe();
        Assert(
            snapshot.State == SessionState.Error &&
            snapshot.Message.Contains("模块列表", StringComparison.Ordinal),
            "A module enumeration failure should not be reported as a generic probe failure.");
    }

    private static GameSessionProbe CreateSessionProbe(
        FakeMemoryAccessor memory,
        IReadOnlyList<int> processIds,
        Func<int, string?> mainModulePathResolver,
        Func<int, string, string?> modulePathResolver)
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        return new GameSessionProbe(
            "gamemd",
            [profile],
            new FakeMemoryAccessorFactory(memory),
            _ => processIds,
            mainModulePathResolver,
            modulePathResolver,
            path => string.Equals(
                    Path.GetFileName(path),
                    profile.Game.FileName,
                    StringComparison.OrdinalIgnoreCase)
                ? profile.Game.Sha256
                : profile.Ares.Sha256);
    }

    private static void SelectionServiceFiltersByOwner()
    {
        SelectionLayout layout = SelectionLayout.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint player = 0x00200000;
        const uint friendly = 0x00300000;
        const uint enemy = 0x00400000;

        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 3);
        memory.SetUInt32(layout.CurrentPlayerAddress, player);
        memory.SetUInt32(items, friendly);
        memory.SetUInt32(items + 4, enemy);
        memory.SetUInt32(items + 8, 0);
        memory.SetUInt32(friendly + layout.OwnerOffset, player);
        memory.SetUInt32(enemy + layout.OwnerOffset, 0x00250000);

        SelectionService service = new(memory, layout);

        SelectionSnapshot owned = service.Read(SelectionScope.All, currentPlayerOnly: true);
        Assert(owned.IsSuccess, "Owned selection read should succeed.");
        Assert(owned.CurrentPlayer == player, "Current player pointer should be preserved.");
        Assert(owned.Objects.SequenceEqual([friendly]), "Only the friendly object should remain.");

        SelectionSnapshot all = service.Read(SelectionScope.All, currentPlayerOnly: false);
        Assert(all.IsSuccess, "Unfiltered selection read should succeed.");
        Assert(all.Objects.SequenceEqual([friendly, enemy]), "Null entries should be skipped.");

        SelectionSnapshot first = service.Read(SelectionScope.First, currentPlayerOnly: false);
        Assert(first.Objects.SequenceEqual([friendly]), "First scope should preserve legacy behavior.");
    }

    private static void SelectionServiceRejectsLargeCount()
    {
        SelectionLayout layout = SelectionLayout.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();
        memory.SetInt32(layout.CountAddress, layout.MaximumCount + 1);

        SelectionSnapshot result =
            new SelectionService(memory, layout).Read(SelectionScope.All, currentPlayerOnly: true);

        Assert(
            result.Status == SelectionReadStatus.InvalidCount,
            "Counts above the configured bound must fail before pointer traversal.");
        Assert(result.Objects.Count == 0, "Invalid reads must return no object addresses.");
    }

    private static void PatchClassifierIsFailClosed()
    {
        PatchSite site = new(
            "gamemd.exe",
            0x000F98B9,
            [0x2B, 0xC7],
            [0x90, 0x90]);

        Assert(
            PatchByteClassifier.Classify(site, [0x2B, 0xC7]) == PatchByteState.Original,
            "Original bytes should be recognized.");
        Assert(
            PatchByteClassifier.Classify(site, [0x90, 0x90]) == PatchByteState.Enabled,
            "Enabled bytes should be recognized.");
        Assert(
            PatchByteClassifier.Classify(site, [0x90, 0xC7]) == PatchByteState.Unknown,
            "Unexpected bytes must not be treated as writable state.");
    }

    private static void FingerprintMatchingIsNormalized()
    {
        ModuleFingerprint fingerprint = new("gamemd.exe", "AA BB CC");
        Assert(
            fingerprint.Matches("GAMEMD.EXE", "aabbcc"),
            "File names and hashes should match case-insensitively after normalization.");
        Assert(
            !fingerprint.Matches("Ares.dll", "aabbcc"),
            "A different module name must not match.");
    }

    private static void MoneyActionUsesVerifiedChain()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();
        const uint player = 0x00200000;
        uint economy = player + profile.EconomyOffset;

        memory.SetUInt32(profile.Selection.CurrentPlayerAddress, player);
        memory.SetUInt32(economy, 5000);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.SetMoney(123456);

        Assert(result.Success, result.Message);
        Assert(memory.TryRead(economy, out uint value), "Money readback should succeed.");
        Assert(value == 123456, "Money should be written through CurrentPlayer+0x30C.");
    }

    private static void PromoteActionTraversesFinalVector()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint player = 0x00200000;
        const uint friendly1 = 0x00300000;
        const uint friendly2 = 0x00400000;
        const uint enemy = 0x00500000;

        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 3);
        memory.SetUInt32(layout.CurrentPlayerAddress, player);
        memory.SetUInt32(items, friendly1);
        memory.SetUInt32(items + 4, friendly2);
        memory.SetUInt32(items + 8, enemy);

        memory.SetUInt32(friendly1 + layout.OwnerOffset, player);
        memory.SetUInt32(friendly2 + layout.OwnerOffset, player);
        memory.SetUInt32(enemy + layout.OwnerOffset, 0x00250000);
        memory.SetBytes(friendly1 + layout.VeterancyOffset, BitConverter.GetBytes(0.0f));
        memory.SetBytes(friendly2 + layout.VeterancyOffset, BitConverter.GetBytes(1.0f));
        memory.SetBytes(enemy + layout.VeterancyOffset, BitConverter.GetBytes(0.0f));

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.PromoteSelectedUnits();

        Assert(result.Success, result.Message);
        Assert(result.AffectedCount == 2, "Both friendly units should be affected.");
        Assert(memory.TryRead(friendly1 + layout.VeterancyOffset, out float first), "First readback failed.");
        Assert(memory.TryRead(friendly2 + layout.VeterancyOffset, out float second), "Second readback failed.");
        Assert(memory.TryRead(enemy + layout.VeterancyOffset, out float third), "Enemy readback failed.");
        Assert(first == 2.0f && second == 2.0f, "Every friendly unit should become elite.");
        Assert(third == 0.0f, "Enemy veterancy must remain unchanged.");
    }

    private static GameActionService CreateReadyActions(
        FakeMemoryAccessor memory,
        VersionProfile profile)
    {
        GameSessionSnapshot snapshot = new(
            SessionState.MatchReady,
            memory.ProcessId,
            "ready",
            profile);

        return new GameActionService(
            new FakeSessionProbe(snapshot),
            new FakeMemoryAccessorFactory(memory));
    }

    private static void AutoRepairRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();
        const uint construction = 0x00500000;
        uint address = construction + profile.AutoRepairOffset;
        memory.SetUInt32(profile.BuildQueueRootAddress, construction);
        memory.SetBytes(address, [1]);

        GameActionService actions = CreateReadyActions(memory, profile);
        PatchInspection before =
            actions.InspectPatch(KnownPatchIds.AutoRepair);
        Assert(
            before.State == PatchRuntimeState.Original,
            "Automatic repair value 1 must be classified as disabled.");

        ActionResult enable = actions.SetPatchEnabled(
            KnownPatchIds.AutoRepair,
            "自动维修",
            true);
        Assert(enable.Success, enable.Message);
        Assert(
            memory.TryRead(address, out byte enabled) && enabled == 0,
            "Enabling automatic repair must write zero.");
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoRepair).State ==
                PatchRuntimeState.Enabled,
            "Automatic repair value 0 must be classified as enabled.");

        ActionResult disable = actions.SetPatchEnabled(
            KnownPatchIds.AutoRepair,
            "自动维修",
            false);
        Assert(disable.Success, disable.Message);
        Assert(
            memory.TryRead(address, out byte disabled) && disabled == 1,
            "Disabling automatic repair must restore one.");

        memory.SetBytes(address, [2]);
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoRepair).State ==
                PatchRuntimeState.Unknown,
            "Unknown automatic repair values must fail closed.");
    }

    private static void SelectedUnitEditorCombinesAttributes()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();
        const uint items = 0x00110000;
        const uint player = 0x00210000;
        const uint friendly1 = 0x00310000;
        const uint friendly2 = 0x00410000;
        const uint enemy = 0x00510000;

        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 3);
        memory.SetUInt32(layout.CurrentPlayerAddress, player);
        memory.SetUInt32(items, friendly1);
        memory.SetUInt32(items + 4, friendly2);
        memory.SetUInt32(items + 8, enemy);
        memory.SetUInt32(friendly1 + layout.OwnerOffset, player);
        memory.SetUInt32(friendly2 + layout.OwnerOffset, player);
        memory.SetUInt32(enemy + layout.OwnerOffset, 0x00220000);
        foreach (uint unit in new[] { friendly1, friendly2, enemy })
        {
            memory.SetBytes(
                unit + layout.VeterancyOffset,
                BitConverter.GetBytes(0.0f));
            memory.SetInt32(unit + layout.HealthOffset, 500);
        }

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.EditSelectedUnits(
            new SelectedUnitEditRequest(
                SelectedUnitEditScope.AllCurrentPlayer,
                UnitVeterancyLevel.Elite,
                10_000_000));

        Assert(result.Success, result.Message);
        Assert(result.AffectedCount == 2, "Both friendly units should be edited.");
        foreach (uint unit in new[] { friendly1, friendly2 })
        {
            Assert(
                memory.TryRead(
                    unit + layout.VeterancyOffset,
                    out float veterancy) &&
                veterancy == 2.0f,
                "Friendly unit veterancy should be elite.");
            Assert(
                memory.TryRead(
                    unit + layout.HealthOffset,
                    out int health) &&
                health == 10_000_000,
                "Friendly unit health should use the requested absolute value.");
        }
        Assert(
            memory.TryRead(
                enemy + layout.VeterancyOffset,
                out float enemyVeterancy) &&
            enemyVeterancy == 0.0f,
            "Enemy veterancy must remain unchanged.");
        Assert(
            memory.TryRead(
                enemy + layout.HealthOffset,
                out int enemyHealth) &&
            enemyHealth == 500,
            "Enemy health must remain unchanged.");
    }

    private static void SelectedUnitEditorRollsBack()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();
        const uint items = 0x00120000;
        const uint unit = 0x00320000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(items, unit);
        memory.SetBytes(
            unit + layout.VeterancyOffset,
            BitConverter.GetBytes(1.0f));
        memory.SetInt32(unit + layout.HealthOffset, 750);
        memory.FailNextDataWriteAddress =
            unit + layout.HealthOffset;

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.EditSelectedUnits(
            new SelectedUnitEditRequest(
                SelectedUnitEditScope.FirstSelected,
                UnitVeterancyLevel.Elite,
                9000));

        Assert(!result.Success, "The injected health write failure must fail.");
        Assert(
            memory.TryRead(
                unit + layout.VeterancyOffset,
                out float veterancy) &&
            veterancy == 1.0f,
            "The earlier veterancy write must be rolled back.");
        Assert(
            memory.TryRead(
                unit + layout.HealthOffset,
                out int health) &&
            health == 750,
            "The failed health target must retain its original value.");
    }

    private static void SelectedUnitEditorReappliesValues()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();
        const uint items = 0x00125000;
        const uint unit = 0x00325000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(items, unit);
        memory.SetBytes(
            unit + layout.VeterancyOffset,
            BitConverter.GetBytes(0.0f));
        memory.SetInt32(unit + layout.HealthOffset, 500);

        GameActionService actions = CreateReadyActions(memory, profile);
        SelectedUnitEditRequest request = new(
            SelectedUnitEditScope.FirstSelected,
            UnitVeterancyLevel.Elite,
            9000);
        ActionResult first = actions.EditSelectedUnits(request);
        Assert(first.Success, first.Message);

        memory.SetBytes(
            unit + layout.VeterancyOffset,
            BitConverter.GetBytes(1.0f));
        memory.SetInt32(unit + layout.HealthOffset, 8000);
        ActionResult second = actions.EditSelectedUnits(request);

        Assert(second.Success, second.Message);
        Assert(
            memory.TryRead(
                unit + layout.VeterancyOffset,
                out float veterancy) &&
            veterancy == 2.0f,
            "Automatic editing must restore the requested veterancy.");
        Assert(
            memory.TryRead(
                unit + layout.HealthOffset,
                out int health) &&
            health == 9000,
            "Automatic editing must restore the requested health.");
    }

    private static void AddHealthUsesSelectedUnits()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint first = 0x00300000;
        const uint second = 0x00400000;
        const uint third = 0x00500000;
        const uint currentPlayer = 0x00200000;
        const uint alliedPlayer = 0x00210000;
        const uint enemyPlayer = 0x00220000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 3);
        memory.SetUInt32(
            layout.CurrentPlayerAddress,
            currentPlayer);
        memory.SetUInt32(items, first);
        memory.SetUInt32(items + 4, second);
        memory.SetUInt32(items + 8, third);
        memory.SetUInt32(
            first + layout.OwnerOffset,
            currentPlayer);
        memory.SetUInt32(
            second + layout.OwnerOffset,
            alliedPlayer);
        memory.SetUInt32(
            third + layout.OwnerOffset,
            enemyPlayer);
        memory.SetInt32(first + layout.HealthOffset, 135);
        memory.SetInt32(second + layout.HealthOffset, 250);
        memory.SetInt32(third + layout.HealthOffset, 375);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.AddHealthToSelectedUnits(2000);

        Assert(result.Success, result.Message);
        Assert(memory.TryRead(first + layout.HealthOffset, out int firstHealth), "First health readback failed.");
        Assert(memory.TryRead(second + layout.HealthOffset, out int secondHealth), "Second health readback failed.");
        Assert(memory.TryRead(third + layout.HealthOffset, out int thirdHealth), "Third health readback failed.");
        Assert(firstHealth == 2135, "The first selected unit should gain 2000 health.");
        Assert(secondHealth == 2250, "The selected allied unit should gain 2000 health.");
        Assert(thirdHealth == 2375, "The selected enemy unit should gain 2000 health.");
    }

    private static void SetHealthUsesSelectedUnits()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint first = 0x00300000;
        const uint second = 0x00400000;
        const uint third = 0x00500000;
        const uint currentPlayer = 0x00200000;
        const uint alliedPlayer = 0x00210000;
        const uint enemyPlayer = 0x00220000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 3);
        memory.SetUInt32(
            layout.CurrentPlayerAddress,
            currentPlayer);
        memory.SetUInt32(items, first);
        memory.SetUInt32(items + 4, second);
        memory.SetUInt32(items + 8, third);
        memory.SetUInt32(
            first + layout.OwnerOffset,
            currentPlayer);
        memory.SetUInt32(
            second + layout.OwnerOffset,
            alliedPlayer);
        memory.SetUInt32(
            third + layout.OwnerOffset,
            enemyPlayer);
        memory.SetInt32(first + layout.HealthOffset, 900);
        memory.SetInt32(second + layout.HealthOffset, 800);
        memory.SetInt32(third + layout.HealthOffset, 700);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.SetSelectedUnitsHealth(1);

        Assert(result.Success, result.Message);
        Assert(memory.TryRead(first + layout.HealthOffset, out int firstHealth), "First health readback failed.");
        Assert(memory.TryRead(second + layout.HealthOffset, out int secondHealth), "Second health readback failed.");
        Assert(memory.TryRead(third + layout.HealthOffset, out int thirdHealth), "Third health readback failed.");
        Assert(firstHealth == 1, "The selected current-player unit health should become one.");
        Assert(secondHealth == 1, "The selected allied unit health should become one.");
        Assert(thirdHealth == 1, "The selected enemy unit health should become one.");
    }

    private static void TransferActionUsesSelectedOwnerChain()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint currentPlayer = 0x00200000;
        const uint target = 0x00300000;
        const uint enemyOwner = 0x00400000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(layout.CurrentPlayerAddress, currentPlayer);
        memory.SetUInt32(items, target);
        memory.SetUInt32(target + layout.OwnerOffset, enemyOwner);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result =
            actions.TransferSelectedUnitsToCurrentPlayer();

        Assert(result.Success, result.Message);
        Assert(
            memory.TryRead(target + layout.OwnerOffset, out uint owner),
            "Owner readback failed.");
        Assert(owner == currentPlayer, "Selected unit should belong to CurrentPlayer.");
    }

    private static void ZeroEconomyUsesSelectedOwnerChain()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint target = 0x00300000;
        const uint owner = 0x00400000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(items, target);
        memory.SetUInt32(target + layout.OwnerOffset, owner);
        memory.SetUInt32(owner + profile.EconomyOffset, 98765);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.ZeroFirstSelectedOwnerEconomy();

        Assert(result.Success, result.Message);
        Assert(
            memory.TryRead(owner + profile.EconomyOffset, out uint economy),
            "Economy readback failed.");
        Assert(economy == 0, "Selected owner economy should become zero.");
    }

    private static void DefeatActionUsesSelectedOwnerChain()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint target = 0x00300000;
        const uint owner = 0x00400000;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(items, target);
        memory.SetUInt32(target + layout.OwnerOffset, owner);
        memory.SetBytes(owner + profile.DefeatedStateOffset, [0x00]);

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result = actions.DefeatFirstSelectedOwner();

        Assert(result.Success, result.Message);
        Assert(
            memory.TryRead(owner + profile.DefeatedStateOffset, out byte defeated),
            "Defeated flag readback failed.");
        Assert(defeated == 1, "Selected owner should be marked defeated.");
    }

    private static void BatchTwoWriteFailureKeepsOriginal()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionLayout layout = profile.Selection;
        using FakeMemoryAccessor memory = new();

        const uint items = 0x00100000;
        const uint currentPlayer = 0x00200000;
        const uint target = 0x00300000;
        const uint enemyOwner = 0x00400000;
        uint ownerAddress = target + layout.OwnerOffset;
        memory.SetUInt32(layout.ItemsAddress, items);
        memory.SetInt32(layout.CountAddress, 1);
        memory.SetUInt32(layout.CurrentPlayerAddress, currentPlayer);
        memory.SetUInt32(items, target);
        memory.SetUInt32(ownerAddress, enemyOwner);
        memory.FailNextDataWriteAddress = ownerAddress;

        GameActionService actions = CreateReadyActions(memory, profile);
        ActionResult result =
            actions.TransferSelectedUnitsToCurrentPlayer();

        Assert(!result.Success, "A failed owner write must fail the action.");
        Assert(
            memory.TryRead(ownerAddress, out uint owner),
            "Owner readback failed.");
        Assert(owner == enemyOwner, "Failed action must retain the original owner.");
    }

    private static void EconomyPatchRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        PatchDefinition patch = profile.Patches[KnownPatchIds.EconomyNoDecrease];
        PatchSite site = patch.Sites[0];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        modules.Add("gamemd.exe", 0x00400000);
        uint address = 0x00400000 + site.Rva;
        memory.SetBytes(address, site.OriginalBytes);

        GameActionService actions = CreatePatchActions(memory, modules, profile);

        Assert(
            actions.InspectEconomyNoDecrease().State == PatchRuntimeState.Original,
            "Patch should begin in original state.");

        ActionResult enabled = actions.SetEconomyNoDecrease(true);
        Assert(enabled.Success, enabled.Message);
        Assert(
            actions.InspectEconomyNoDecrease().State == PatchRuntimeState.Enabled,
            "Enabled bytes should be observable.");

        ActionResult disabled = actions.SetEconomyNoDecrease(false);
        Assert(disabled.Success, disabled.Message);
        Assert(
            actions.InspectEconomyNoDecrease().State == PatchRuntimeState.Original,
            "Disable should restore the exact original bytes.");
    }

    private static void EconomyPatchRejectsUnknownBytes()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        PatchDefinition patch = profile.Patches[KnownPatchIds.EconomyNoDecrease];
        PatchSite site = patch.Sites[0];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        modules.Add("gamemd.exe", 0x00400000);
        uint address = 0x00400000 + site.Rva;
        memory.SetBytes(address, [0x2B, 0x90]);

        GameActionService actions = CreatePatchActions(memory, modules, profile);
        ActionResult result = actions.SetEconomyNoDecrease(true);

        Assert(!result.Success, "Unknown bytes must reject patch activation.");
        Assert(
            actions.InspectEconomyNoDecrease().State == PatchRuntimeState.Unknown,
            "Unknown state should remain unchanged.");
    }

    private static void BatchOnePatchesRoundTrip()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        string[] patchIds =
        [
            KnownPatchIds.PowerNoDecrease,
            KnownPatchIds.ControlAllUnits,
        ];

        foreach (string patchId in patchIds)
        {
            PatchDefinition patch = profile.Patches[patchId];
            using FakeMemoryAccessor memory = new();
            FakeModuleResolver modules = new();
            modules.Add("gamemd.exe", 0x00400000);

            foreach (PatchSite site in patch.Sites)
            {
                memory.SetBytes(0x00400000 + site.Rva, site.OriginalBytes);
            }

            GameActionService actions = CreatePatchActions(memory, modules, profile);
            Assert(
                actions.InspectPatch(patchId).State == PatchRuntimeState.Original,
                $"{patchId} should begin in original state.");

            ActionResult enabled = actions.SetPatchEnabled(patchId, patchId, true);
            Assert(enabled.Success, enabled.Message);
            Assert(
                actions.InspectPatch(patchId).State == PatchRuntimeState.Enabled,
                $"{patchId} should report enabled.");

            ActionResult disabled = actions.SetPatchEnabled(patchId, patchId, false);
            Assert(disabled.Success, disabled.Message);
            Assert(
                actions.InspectPatch(patchId).State == PatchRuntimeState.Original,
                $"{patchId} should restore exact original bytes.");
        }
    }

    private static void BatchThreePatchesRoundTrip()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        string[] patchIds =
        [
            KnownPatchIds.IgnoreBuildDistance,
            KnownPatchIds.PsychicDetection,
            KnownPatchIds.IgnoreCloak,
            KnownPatchIds.StableRevealMap,
        ];

        foreach (string patchId in patchIds)
        {
            PatchDefinition patch = profile.Patches[patchId];
            using FakeMemoryAccessor memory = new();
            FakeModuleResolver modules = new();
            modules.Add("gamemd.exe", 0x00400000);

            foreach (PatchSite site in patch.Sites)
            {
                memory.SetBytes(0x00400000 + site.Rva, site.OriginalBytes);
            }

            GameActionService actions = CreatePatchActions(memory, modules, profile);
            ActionResult enabled = actions.SetPatchEnabled(patchId, patchId, true);
            Assert(enabled.Success, enabled.Message);

            foreach (PatchSite site in patch.Sites)
            {
                byte[] actual = new byte[site.EnabledBytes.Length];
                Assert(
                    memory.TryReadBytes(0x00400000 + site.Rva, actual),
                    $"{patchId} enabled readback failed.");
                Assert(
                    actual.AsSpan().SequenceEqual(site.EnabledBytes),
                    $"{patchId} must enable every site.");
            }

            ActionResult disabled = actions.SetPatchEnabled(patchId, patchId, false);
            Assert(disabled.Success, disabled.Message);

            foreach (PatchSite site in patch.Sites)
            {
                byte[] actual = new byte[site.OriginalBytes.Length];
                Assert(
                    memory.TryReadBytes(0x00400000 + site.Rva, actual),
                    $"{patchId} restored readback failed.");
                Assert(
                    actual.AsSpan().SequenceEqual(site.OriginalBytes),
                    $"{patchId} must restore every site.");
            }
        }
    }

    private static void PlayerInstantBuildHookRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        var hook = profile.Hooks[KnownPatchIds.InstantUnitBuild];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        modules.Add("gamemd.exe", 0x00400000);
        uint entryAddress = 0x00400000 + hook.Rva;
        memory.SetBytes(entryAddress, hook.OriginalBytes);

        GameActionService actions = CreatePatchActions(memory, modules, profile);
        Assert(
            actions.InspectPatch(KnownPatchIds.InstantUnitBuild).State ==
                PatchRuntimeState.Original,
            "The hook should begin in original state.");

        ActionResult enabled = actions.SetPatchEnabled(
            KnownPatchIds.InstantUnitBuild,
            "单位秒建",
            true);
        Assert(enabled.Success, enabled.Message);
        Assert(
            actions.InspectPatch(KnownPatchIds.InstantUnitBuild).State ==
                PatchRuntimeState.Enabled,
            "The installed hook should be recognized.");

        byte[] entry = new byte[hook.OriginalBytes.Length];
        Assert(memory.TryReadBytes(entryAddress, entry), "Hook entry read failed.");
        Assert(entry[0] == 0xE9, "Hook entry must begin with a relative jump.");
        Assert(entry[5..].All(value => value == 0x90), "Covered entry tail must be NOP-filled.");

        int relative = BitConverter.ToInt32(entry, 1);
        uint remoteAddress = unchecked((uint)(entryAddress + 5 + relative));
        Assert(memory.IsRemotePageAllocated(remoteAddress), "Remote hook page should be allocated.");

        byte[] stub = new byte[39];
        Assert(memory.TryReadBytes(remoteAddress, stub), "Remote stub read failed.");
        Assert(
            stub.AsSpan(0, 4).SequenceEqual(
                new byte[] { 0x50, 0x8B, 0x46, hook.OwnerOffset }),
            "Stub must read Factory.Owner.");
        Assert(
            BitConverter.ToUInt32(stub, 6) == hook.CurrentPlayerAddress,
            "Stub must compare against CurrentPlayer.");
        Assert(
            stub[10] == 0x58,
            "Stub must restore the original EAX before either branch.");
        Assert(
            stub[11] == 0x75 &&
            stub[12] == 0x0B &&
            stub[22] == 0xEB &&
            stub[23] == 0x0A,
            "Owner and completion branches must land on the expected paths.");
        Assert(
            stub.AsSpan(24, hook.OriginalBytes.Length)
                .SequenceEqual(hook.OriginalBytes),
            "Enemy path must preserve the original progress update.");

        GameActionService reattached =
            CreatePatchActions(memory, modules, profile);
        Assert(
            reattached.InspectPatch(KnownPatchIds.InstantUnitBuild).State ==
                PatchRuntimeState.Enabled,
            "A fresh service must recognize the existing owned stub.");

        ActionResult disabled = reattached.SetPatchEnabled(
            KnownPatchIds.InstantUnitBuild,
            "单位秒建",
            false);
        Assert(disabled.Success, disabled.Message);
        Assert(
            reattached.InspectPatch(KnownPatchIds.InstantUnitBuild).State ==
                PatchRuntimeState.Original,
            "Disable must restore the exact original entry bytes.");
        Assert(
            !memory.IsRemotePageAllocated(remoteAddress),
            "Disable must release the remote hook page.");
    }

    private static GameActionService CreatePatchActions(
        FakeMemoryAccessor memory,
        FakeModuleResolver modules,
        VersionProfile profile)
    {
        GameSessionSnapshot snapshot = new(
            SessionState.ModulesReady,
            memory.ProcessId,
            "modules ready",
            profile);

        return new GameActionService(
            new FakeSessionProbe(snapshot),
            new FakeMemoryAccessorFactory(memory),
            modules);
    }

    private static void SelectionVeterancyEventHookRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        SelectionVeterancyHookDefinition hook =
            profile.SelectionHooks[KnownPatchIds.AutoPromoteSelection];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        modules.Add("gamemd.exe", 0x00400000);
        uint[] entryAddresses = hook.Sites
            .Select(site => checked(0x00400000 + site.Rva))
            .ToArray();
        for (int index = 0; index < hook.Sites.Count; index++)
        {
            memory.SetBytes(
                entryAddresses[index],
                hook.Sites[index].OriginalBytes);
        }

        GameActionService actions =
            CreatePatchActions(memory, modules, profile);
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Original,
            "Selection hook should begin in original state.");

        ActionResult enabled =
            actions.SetSelectionEditEventPolicy(
                new SelectedUnitEditRequest(
                    SelectedUnitEditScope.AllCurrentPlayer,
                    UnitVeterancyLevel.Veteran,
                    9000));
        Assert(enabled.Success, enabled.Message);
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Enabled,
            "Installed selection hook should be recognized.");

        byte[][] entries = new byte[hook.Sites.Count][];
        uint remoteAddress = 0;
        for (int index = 0; index < hook.Sites.Count; index++)
        {
            entries[index] =
                new byte[hook.Sites[index].OriginalBytes.Length];
            Assert(
                memory.TryReadBytes(entryAddresses[index], entries[index]),
                $"Selection hook entry {index} read failed.");
            Assert(
                entries[index][0] == 0xE9 &&
                entries[index].Skip(5).All(value => value == 0x90),
                $"Selection hook entry {index} must be E9 plus NOP padding.");

            int relative = BitConverter.ToInt32(entries[index], 1);
            uint wrapperAddress = unchecked(
                (uint)(entryAddresses[index] + 5 + relative));
            uint candidateRemoteAddress = checked(
                wrapperAddress - (index == 0 ? 0u : 96u));
            if (index == 0)
            {
                remoteAddress = candidateRemoteAddress;
            }
            else
            {
                Assert(
                    candidateRemoteAddress == remoteAddress,
                    "Both selection entries must share one remote page.");
            }
        }

        Assert(
            memory.IsRemotePageAllocated(remoteAddress),
            "Selection hook remote page should be allocated.");

        byte[] payload = new byte[204];
        Assert(
            memory.TryReadBytes(remoteAddress, payload),
            "Selection hook payload read failed.");

        for (int index = 0; index < hook.Sites.Count; index++)
        {
            int wrapperOffset = index == 0 ? 0 : 96;
            uint wrapperAddress =
                checked(remoteAddress + (uint)wrapperOffset);
            Assert(
                payload[wrapperOffset] == 0x9C &&
                payload[wrapperOffset + 1] == 0x60 &&
                BitConverter.ToUInt32(payload, wrapperOffset + 4) ==
                    hook.CurrentPlayerAddress,
                $"Wrapper {index} must preserve state and read CurrentPlayer.");
            Assert(
                BitConverter.ToUInt32(payload, wrapperOffset + 18) ==
                    hook.OwnerOffset &&
                payload[wrapperOffset + 28] == 0xC7 &&
                payload[wrapperOffset + 29] == 0x86 &&
                BitConverter.ToUInt32(payload, wrapperOffset + 30) ==
                    hook.VeterancyOffset &&
                BitConverter.ToSingle(payload, wrapperOffset + 34) == 1.0f &&
                payload[wrapperOffset + 38] == 0xC7 &&
                payload[wrapperOffset + 39] == 0x86 &&
                BitConverter.ToUInt32(payload, wrapperOffset + 40) ==
                    hook.HealthOffset &&
                BitConverter.ToInt32(payload, wrapperOffset + 44) == 9000,
                $"Wrapper {index} must filter ESI owner and embed direct writes.");
            Assert(
                RelativeTarget(
                    wrapperAddress,
                    payload.AsSpan(wrapperOffset).ToArray(),
                    10,
                    6) == wrapperAddress + 48 &&
                RelativeTarget(
                    wrapperAddress,
                    payload.AsSpan(wrapperOffset).ToArray(),
                    22,
                    6) == wrapperAddress + 48,
                $"Wrapper {index} owner guards must skip all writes.");
            Assert(
                payload.AsSpan(
                        wrapperOffset + 50,
                        hook.Sites[index].OriginalBytes.Length)
                    .SequenceEqual(hook.Sites[index].OriginalBytes) &&
                RelativeTarget(
                    wrapperAddress,
                    payload.AsSpan(wrapperOffset).ToArray(),
                    57,
                    5) ==
                    entryAddresses[index] +
                    (uint)hook.Sites[index].OriginalBytes.Length,
                $"Wrapper {index} must replay its site and return after entry.");
        }

        Assert(
            hook.HealthOffset == 0x6C,
            "Event health writes must use the verified ObjectClass health field.");
        Assert(
            BitConverter.ToInt32(payload, 192) == 3 &&
            BitConverter.ToSingle(payload, 196) == 1.0f &&
            BitConverter.ToInt32(payload, 200) == 9000,
            "Remote policy must contain the requested rank and health.");

        GameActionService reattached =
            CreatePatchActions(memory, modules, profile);
        Assert(
            reattached.InspectPatch(
                KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Enabled,
            "Fresh service must recognize the selection hook.");

        ActionResult disabled =
            reattached.SetSelectionEditEventPolicy(null);
        Assert(disabled.Success, disabled.Message);
        Assert(
            reattached.InspectPatch(
                KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Original,
            "Disable must restore both exact ObjectClass::Select entries.");
        for (int index = 0; index < hook.Sites.Count; index++)
        {
            byte[] restored =
                new byte[hook.Sites[index].OriginalBytes.Length];
            Assert(
                memory.TryReadBytes(entryAddresses[index], restored) &&
                restored.SequenceEqual(hook.Sites[index].OriginalBytes),
                $"Disable must restore selection entry {index}.");
        }
        Assert(
            !memory.IsRemotePageAllocated(remoteAddress),
            "Disable must release the selection hook page.");

        memory.FailNextCodeWriteAddress = entryAddresses[1];
        ActionResult failedInstall =
            actions.SetSelectionEditEventPolicy(
                new SelectedUnitEditRequest(
                    SelectedUnitEditScope.AllCurrentPlayer,
                    UnitVeterancyLevel.Elite,
                    null));
        Assert(
            !failedInstall.Success,
            "A failed second entry installation must fail the transition.");
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Original,
            "A failed second entry installation must restore the first entry.");

        ActionResult reenabled =
            actions.SetSelectionEditEventPolicy(
                new SelectedUnitEditRequest(
                    SelectedUnitEditScope.AllCurrentPlayer,
                    UnitVeterancyLevel.Elite,
                    null));
        Assert(reenabled.Success, reenabled.Message);
        memory.FailNextCodeWriteAddress = entryAddresses[1];
        ActionResult failedDisable =
            actions.SetSelectionEditEventPolicy(null);
        Assert(
            !failedDisable.Success,
            "A failed second entry restore must fail the transition.");
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Enabled,
            "A failed second entry restore must roll the first entry back to enabled.");

        ActionResult finalDisable =
            actions.SetSelectionEditEventPolicy(null);
        Assert(finalDisable.Success, finalDisable.Message);
        Assert(
            actions.InspectPatch(KnownPatchIds.AutoPromoteSelection).State ==
                PatchRuntimeState.Original,
            "Final disable must restore both entries after rollback testing.");
    }

    private static uint RelativeTarget(
        uint baseAddress,
        byte[] bytes,
        int instructionOffset,
        int instructionLength)
    {
        int displacement = BitConverter.ToInt32(
            bytes,
            instructionOffset + instructionLength - 4);
        return unchecked(
            baseAddress +
            (uint)instructionOffset +
            (uint)instructionLength +
            (uint)displacement);
    }

    private static void PlayerTechUnlockHookRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        var hook = profile.TechUnlockHooks[KnownPatchIds.SafeTechUnlock];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        const uint aresBase = 0x519A0000;
        const uint currentPlayer = 0x00480000;
        modules.Add("Ares.dll", aresBase);
        uint entryAddress = aresBase + hook.Rva;
        uint factoryEntryAddress =
            aresBase + hook.FactoryOwnerRva;
        memory.SetBytes(entryAddress, hook.OriginalBytes);
        memory.SetBytes(
            factoryEntryAddress,
            hook.FactoryOwnerOriginalBytes);
        memory.SetUInt32(hook.CurrentPlayerAddress, currentPlayer);
        memory.SetBytes(
            currentPlayer + hook.RecheckTechTreeOffset,
            [0x00]);

        GameActionService actions = CreatePatchActions(memory, modules, profile);
        Assert(
            actions.InspectPatch(KnownPatchIds.SafeTechUnlock).State ==
                PatchRuntimeState.Original,
            "The safe tech unlock hook should begin in original state.");
        Assert(
            actions.InspectTechUnlockMode(KnownPatchIds.SafeTechUnlock).Mode ==
                TechUnlockMode.Off,
            "The tech unlock mode should begin off.");

        ActionResult factionEnabled = actions.SetTechUnlockMode(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
            TechUnlockMode.Faction);
        Assert(factionEnabled.Success, factionEnabled.Message);
        Assert(
            actions.InspectTechUnlockMode(KnownPatchIds.SafeTechUnlock).Mode ==
                TechUnlockMode.Faction,
            "Faction mode should be recognized.");
        byte[] factionFactoryEntry =
            new byte[hook.FactoryOwnerOriginalBytes.Length];
        Assert(
            memory.TryReadBytes(factoryEntryAddress, factionFactoryEntry) &&
            factionFactoryEntry.SequenceEqual(
                hook.FactoryOwnerOriginalBytes),
            "Faction mode must retain the factory Owners intersection.");

        ActionResult extendedEnabled = actions.SetTechUnlockMode(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
            TechUnlockMode.Extended);
        Assert(extendedEnabled.Success, extendedEnabled.Message);
        Assert(
            actions.InspectPatch(KnownPatchIds.SafeTechUnlock).State ==
                PatchRuntimeState.Enabled,
            "The installed safe tech unlock hook should be recognized.");
        Assert(
            actions.InspectTechUnlockMode(KnownPatchIds.SafeTechUnlock).Mode ==
                TechUnlockMode.Extended,
            "Extended mode should be recognized.");
        byte[] recheck = new byte[1];
        Assert(
            memory.TryReadBytes(
                currentPlayer + hook.RecheckTechTreeOffset,
                recheck) &&
            recheck[0] == 1,
            "Enable must request an immediate tech-tree rebuild.");

        byte[] entry = new byte[hook.OriginalBytes.Length];
        Assert(memory.TryReadBytes(entryAddress, entry), "Hook entry read failed.");
        Assert(entry[0] == 0xE9, "Hook entry must begin with a relative jump.");
        Assert(entry[5] == 0x90, "Covered entry tail must be NOP-filled.");

        int relative = BitConverter.ToInt32(entry, 1);
        uint remoteAddress = unchecked((uint)(entryAddress + 5 + relative));
        Assert(memory.IsRemotePageAllocated(remoteAddress), "Remote hook page should be allocated.");

        byte[] stub = new byte[24];
        Assert(memory.TryReadBytes(remoteAddress, stub), "Remote stub read failed.");
        Assert(
            stub.AsSpan(0, 2).SequenceEqual(new byte[] { 0x3B, 0x15 }),
            "Requirement stub must compare EDX HouseClass against CurrentPlayer.");
        Assert(
            BitConverter.ToUInt32(stub, 2) == hook.CurrentPlayerAddress,
            "Stub must use the verified CurrentPlayer address.");
        Assert(
            stub[6] == 0x75 &&
            stub[7] == 0x05 &&
            stub[8] == 0xE9,
            "Only the current-player path may jump to Ares Overridden.");
        uint overriddenTarget = unchecked(
            (uint)(remoteAddress + 13 + BitConverter.ToInt32(stub, 9)));
        Assert(
            overriddenTarget == aresBase + hook.OverriddenReturnRva,
            "Player path must use Ares' native Overridden return.");
        Assert(
            stub.AsSpan(13, hook.OriginalBytes.Length)
                .SequenceEqual(hook.OriginalBytes),
            "Non-player path must replay the original Ares instructions.");

        byte[] factoryEntry =
            new byte[hook.FactoryOwnerOriginalBytes.Length];
        Assert(
            memory.TryReadBytes(factoryEntryAddress, factoryEntry),
            "Factory Hook entry read failed.");
        Assert(
            factoryEntry[0] == 0xE9 &&
            factoryEntry[5..].All(value => value == 0x90),
            "Factory Hook entry must be a NOP-filled relative jump.");
        uint factoryRemoteAddress = unchecked(
            (uint)(factoryEntryAddress + 5 +
                BitConverter.ToInt32(factoryEntry, 1)));
        Assert(
            factoryRemoteAddress == remoteAddress + 64,
            "Both Hook stubs must share one owned remote page.");

        byte[] factoryStub = new byte[33];
        Assert(
            memory.TryReadBytes(factoryRemoteAddress, factoryStub),
            "Factory Hook stub read failed.");
        Assert(
            factoryStub.AsSpan(0, 3)
                .SequenceEqual(new byte[] { 0x50, 0x8B, 0x06 }) &&
            factoryStub.AsSpan(3, 2)
                .SequenceEqual(new byte[] { 0x8B, 0x80 }) &&
            BitConverter.ToUInt32(factoryStub, 5) ==
                hook.FactoryObjectOwnerOffset &&
            factoryStub.AsSpan(9, 2)
                .SequenceEqual(new byte[] { 0x3B, 0x05 }) &&
            BitConverter.ToUInt32(factoryStub, 11) ==
                hook.CurrentPlayerAddress,
            "Factory Hook must resolve Building.Owner and preserve EAX.");
        Assert(
            factoryStub[15] == 0x58 &&
            factoryStub.AsSpan(18, 4)
                .SequenceEqual(new byte[] { 0x85, 0x44, 0x24, 0x10 }),
            "Non-player path must preserve the owner-bit intersection test.");
        uint factoryRejectedTarget = unchecked(
            (uint)(factoryRemoteAddress + 28 +
                BitConverter.ToInt32(factoryStub, 24)));
        uint factoryAllowedTarget = unchecked(
            (uint)(factoryRemoteAddress + 33 +
                BitConverter.ToInt32(factoryStub, 29)));
        Assert(
            factoryRejectedTarget ==
                aresBase + hook.FactoryOwnerRejectedRva &&
            factoryAllowedTarget ==
                aresBase + hook.FactoryOwnerAllowedRva,
            "Factory Hook branches must rejoin the verified Ares paths.");

        GameActionService reattached =
            CreatePatchActions(memory, modules, profile);
        Assert(
            reattached.InspectPatch(KnownPatchIds.SafeTechUnlock).State ==
                PatchRuntimeState.Enabled,
            "A fresh service must recognize the existing owned stub.");

        byte[] factoryRestored =
            new byte[hook.FactoryOwnerOriginalBytes.Length];
        ActionResult factionRestored = reattached.SetTechUnlockMode(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
            TechUnlockMode.Faction);
        Assert(factionRestored.Success, factionRestored.Message);
        Assert(
            reattached.InspectTechUnlockMode(
                KnownPatchIds.SafeTechUnlock).Mode ==
                TechUnlockMode.Faction,
            "Extended mode should switch back to faction mode.");
        Assert(
            memory.TryReadBytes(factoryEntryAddress, factoryRestored) &&
            factoryRestored.SequenceEqual(
                hook.FactoryOwnerOriginalBytes),
            "Returning to faction mode must restore the factory-owner entry.");

        ActionResult disabled = reattached.SetPatchEnabled(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
            false);
        Assert(disabled.Success, disabled.Message);
        Assert(
            reattached.InspectPatch(KnownPatchIds.SafeTechUnlock).State ==
                PatchRuntimeState.Original,
            "Disable must restore both exact original Ares entries.");
        Assert(
            memory.TryReadBytes(factoryEntryAddress, factoryRestored) &&
            factoryRestored.SequenceEqual(
                hook.FactoryOwnerOriginalBytes),
            "Disable must restore the factory-owner entry.");
        Assert(
            !memory.IsRemotePageAllocated(remoteAddress),
            "Disable must release the remote tech unlock page.");
        Assert(
            memory.TryReadBytes(
                currentPlayer + hook.RecheckTechTreeOffset,
                recheck) &&
            recheck[0] == 1,
            "Disable must request another tech-tree rebuild.");
    }

    private static void DelayedUnitUnlimitedRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        PatchDefinition patch =
            profile.Patches[KnownPatchIds.UnitUnlimited];
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        const uint gameBase = 0x00400000;
        const uint aresBase = 0x519A0000;
        modules.Add("gamemd.exe", gameBase);
        modules.Add("Ares.dll", aresBase);
        foreach (PatchSite site in patch.Sites)
        {
            uint moduleBase = site.ModuleName == "gamemd.exe"
                ? gameBase
                : aresBase;
            memory.SetBytes(
                moduleBase + site.Rva,
                site.OriginalBytes);
        }

        GameSessionSnapshot session = new(
            SessionState.ModulesReady,
            memory.ProcessId,
            "modules ready",
            profile);
        DelayedBuildPolicyService policies = new(
            new FakeSessionProbe(session),
            new FakeMemoryAccessorFactory(memory),
            modules);

        DelayedBuildPolicySnapshot active = policies.SetDesired(
            DelayedBuildFeature.UnitUnlimited,
            true);
        Assert(
            active.UnitUnlimited.State == FeatureState.Active,
            active.UnitUnlimited.Message);
        foreach (PatchSite site in patch.Sites)
        {
            uint moduleBase = site.ModuleName == "gamemd.exe"
                ? gameBase
                : aresBase;
            byte[] actual = new byte[site.EnabledBytes.Length];
            Assert(
                memory.TryReadBytes(moduleBase + site.Rva, actual) &&
                actual.SequenceEqual(site.EnabledBytes),
                "Unit-unlimited must apply every BuildLimit site.");
        }

        DelayedBuildPolicySnapshot restored = policies.RestoreOwned();
        Assert(
            restored.UnitUnlimited.State == FeatureState.Off,
            restored.UnitUnlimited.Message);
        foreach (PatchSite site in patch.Sites)
        {
            uint moduleBase = site.ModuleName == "gamemd.exe"
                ? gameBase
                : aresBase;
            byte[] actual = new byte[site.OriginalBytes.Length];
            Assert(
                memory.TryReadBytes(moduleBase + site.Rva, actual) &&
                actual.SequenceEqual(site.OriginalBytes),
                "Unit-unlimited must restore every original site.");
        }
    }

    private static void DelayedQueueLimitRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        GameSessionSnapshot session = new(
            SessionState.ModulesReady,
            memory.ProcessId,
            "modules ready",
            profile);
        DelayedBuildPolicyService policies = new(
            new FakeSessionProbe(session),
            new FakeMemoryAccessorFactory(memory),
            modules);

        DelayedBuildPolicySnapshot pending = policies.SetDesired(
            DelayedBuildFeature.QueueLimit1000,
            true);
        Assert(
            pending.QueueLimit1000.State == FeatureState.Pending,
            "Queue policy should remain pending before the root exists.");

        const uint queueRoot = 0x084ABCB0;
        uint queueAddress = queueRoot + profile.BuildQueueLimitOffset;
        memory.SetUInt32(profile.BuildQueueRootAddress, queueRoot);
        memory.SetUInt32(queueAddress, 29);
        DelayedBuildPolicySnapshot active = policies.Reconcile();
        Assert(
            active.QueueLimit1000.State == FeatureState.Active,
            active.QueueLimit1000.Message);
        byte[] current = new byte[4];
        Assert(
            memory.TryReadBytes(queueAddress, current) &&
            BitConverter.ToUInt32(current, 0) == 999,
            "Queue policy must write the zero-based limit 999.");

        DelayedBuildPolicySnapshot restored = policies.RestoreOwned();
        Assert(
            restored.QueueLimit1000.State == FeatureState.Off,
            restored.QueueLimit1000.Message);
        Assert(
            memory.TryReadBytes(queueAddress, current) &&
            BitConverter.ToUInt32(current, 0) == 29,
            "Queue policy must restore the captured original value.");
    }

    private static void SuperweaponExecutorRoundTrips()
    {
        VersionProfile profile = KnownProfiles.ObservedMentalOmega;
        string templateHash = Convert.ToHexString(
            SHA256.HashData(profile.SuperweaponExecutor.Template));
        Assert(
            templateHash ==
                "6BE0EE158B2D647BBEAF9FA85C1D836520AA27B0B960D5864F9A3752DDB78EC1",
            "The executor template must match the captured ID 0 payload.");
        using FakeMemoryAccessor memory = new();
        GameSessionSnapshot session = new(
            SessionState.MatchReady,
            memory.ProcessId,
            "match ready",
            profile);
        GameActionService actions = new(
            new FakeSessionProbe(session),
            new FakeMemoryAccessorFactory(memory),
            new FakeModuleResolver());

        ActionResult single = actions.GrantSuperweapon(28);
        Assert(single.Success, single.Message);
        Assert(
            memory.ExecutedPayloads.Count == 1,
            "Single grant must execute exactly one payload.");
        byte[] payload = memory.ExecutedPayloads[0];
        Assert(
            payload.Length == 70 &&
            BitConverter.ToUInt32(payload, 16) == 28 &&
            BitConverter.ToUInt32(payload, 49) == 28,
            "Both captured parameter slots must contain the requested ID.");
        Assert(
            memory.RemoteAllocationCount == 0,
            "Completed single grant must release its remote page.");

        memory.ExecutedPayloads.Clear();
        ActionResult selected = actions.GrantSuperweapons([0, 28, 47]);
        Assert(selected.Success, selected.Message);
        Assert(
            memory.ExecutedPayloads.Count == 1,
            "Selected grants must execute in one batch thread.");
        byte[] selectedPayload = memory.ExecutedPayloads[0];
        uint[] expectedSelectedIds = [0, 28, 47];
        Assert(
            selectedPayload.Length == 69 * expectedSelectedIds.Length + 1,
            "Selected batch must contain exactly the requested bodies.");
        for (int index = 0; index < expectedSelectedIds.Length; index++)
        {
            int bodyOffset = index * 69;
            Assert(
                BitConverter.ToUInt32(
                    selectedPayload,
                    bodyOffset + 16) == expectedSelectedIds[index] &&
                BitConverter.ToUInt32(
                    selectedPayload,
                    bodyOffset + 49) == expectedSelectedIds[index],
                "Selected batch must preserve the requested ID order.");
        }
        Assert(
            memory.RemoteAllocationCount == 0,
            "Selected batch must release its single remote page.");

        memory.ExecutedPayloads.Clear();
        ActionResult safe = actions.GrantSafeSuperweapons();
        Assert(safe.Success, safe.Message);
        Assert(
            memory.ExecutedPayloads.Count == 1,
            "Safe grant must use one batch thread.");
        byte[] safePayload = memory.ExecutedPayloads[0];
        Assert(
            safePayload.Length == 69 * 9 + 1,
            "Safe batch must contain nine joined executor bodies.");
        uint[] expectedSafeIds = [0, 1, 2, 3, 5, 7, 28, 47, 48];
        for (int index = 0; index < expectedSafeIds.Length; index++)
        {
            int bodyOffset = index * 69;
            Assert(
                BitConverter.ToUInt32(safePayload, bodyOffset + 16) ==
                    expectedSafeIds[index] &&
                BitConverter.ToUInt32(safePayload, bodyOffset + 49) ==
                    expectedSafeIds[index],
                "Safe batch must use only the curated IDs.");
        }
        Assert(
            memory.RemoteAllocationCount == 0,
            "Safe batch must release its single remote page.");

        memory.ExecutedPayloads.Clear();
        ActionResult all = actions.GrantAllSuperweapons();
        Assert(all.Success, all.Message);
        Assert(
            memory.ExecutedPayloads.Count == 1,
            "Extended grant-all must use one batch thread.");
        byte[] allPayload = memory.ExecutedPayloads[0];
        Assert(
            allPayload.Length == 69 * 60 + 1,
            "Extended batch must contain 60 joined executor bodies.");
        for (int index = 0; index < 60; index++)
        {
            int bodyOffset = index * 69;
            Assert(
                BitConverter.ToUInt32(allPayload, bodyOffset + 16) ==
                    (uint)index &&
                BitConverter.ToUInt32(allPayload, bodyOffset + 49) ==
                    (uint)index,
                "Grant-all IDs must be strictly serialized from 0 through 59.");
        }
        Assert(
            memory.RemoteAllocationCount == 0,
            "Grant-all must release its single completed remote page.");

        memory.ExecutedPayloads.Clear();
        memory.CompleteRemoteThreads = false;
        ActionResult timeout = actions.GrantSuperweapon(0);
        Assert(
            !timeout.Success && memory.RemoteAllocationCount == 1,
            "Timed-out execution must retain the page used by the live thread.");
    }

    private static void PatchTransactionRollsBack()
    {
        using FakeMemoryAccessor memory = new();
        FakeModuleResolver modules = new();
        modules.Add("gamemd.exe", 0x00400000);

        PatchDefinition patch = new(
            "transaction-test",
            [
                new PatchSite("gamemd.exe", 0x100, [0x11], [0x91]),
                new PatchSite("gamemd.exe", 0x200, [0x22], [0x92]),
            ]);

        const uint first = 0x00400100;
        const uint second = 0x00400200;
        memory.SetBytes(first, [0x11]);
        memory.SetBytes(second, [0x22]);
        memory.FailNextCodeWriteAddress = second;

        PatchTransitionResult result =
            new PatchManager(memory.ProcessId, memory, modules)
                .SetEnabled(patch, enabled: true);

        Assert(!result.Success, "A failed second site should fail the transaction.");

        byte[] firstReadback = new byte[1];
        byte[] secondReadback = new byte[1];
        Assert(memory.TryReadBytes(first, firstReadback), "First rollback read failed.");
        Assert(memory.TryReadBytes(second, secondReadback), "Second rollback read failed.");
        Assert(firstReadback[0] == 0x11, "The first site must be restored.");
        Assert(secondReadback[0] == 0x22, "The failed site must retain its original byte.");
    }

    private static void HotkeyGesturesRoundTrip()
    {
        HotkeyGesture expected = new(
            Keys.B,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        Assert(
            HotkeyGesture.TryParse(
                expected.DisplayText,
                out HotkeyGesture parsed) &&
            parsed == expected,
            "A displayed hotkey must parse back to the same gesture.");
        Assert(
            HotkeyGesture.TryParse("Ctrl+0", out HotkeyGesture digit) &&
            digit.Key == Keys.D0 &&
            digit.Modifiers == HotkeyModifiers.Control,
            "Digit hotkeys must round-trip through D0-D9.");
        Assert(
            !HotkeyGesture.TryParse("Ctrl+Ctrl+A", out _),
            "Duplicate modifiers must be rejected.");

        HotkeyBindings bindings = HotkeyBindings.CreateDefaults();
        Assert(
            !bindings.TrySet(
                HotkeyActionIds.TransferOwner,
                HotkeyGesture.Ctrl(Keys.A),
                out string? conflict) &&
            conflict == HotkeyActionIds.AutoEdit,
            "A duplicate configured gesture must identify its conflict.");
        Assert(
            bindings.TrySet(
                HotkeyActionIds.TransferOwner,
                expected,
                out conflict) &&
            conflict is null,
            "A unique gesture must be accepted.");
    }

    private static void TrainerSettingsRoundTrip()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "motrainer-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(
            directory,
            "MOTrainer.exe.config");
        try
        {
            File.WriteAllText(
                path,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <appSettings>
                    <add key="unrelated.keep" value="yes" />
                  </appSettings>
                  <startup>
                    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
                  </startup>
                </configuration>
                """);

            TrainerSettings defaults = TrainerSettings.CreateDefaults();
            HotkeyGesture custom = new(
                Keys.B,
                HotkeyModifiers.Control | HotkeyModifiers.Shift);
            Assert(
                defaults.Hotkeys.TrySet(
                    HotkeyActionIds.TransferOwner,
                    custom,
                    out _),
                "Unique test hotkey must be accepted.");
            TrainerSettings expected = defaults with
            {
                DisplayLanguage = DisplayLanguage.English,
                ModifyVeterancy = false,
                Veterancy = UnitVeterancyLevel.Veteran,
                ModifyHealth = true,
                Health = 54321,
                AutoEditMode = AutoEditMode.Polling,
                Money = 7654321,
                TechUnlockMode = TechUnlockMode.Extended,
                SelectedSuperweaponIds = [0, 28, 47],
            };

            TrainerSettingsStore store = new(path);
            string? saveError = store.Save(expected);
            Assert(saveError is null, saveError ?? "Settings save failed.");
            TrainerSettingsLoadResult loaded = store.Load();
            Assert(
                loaded.Warning is null &&
                loaded.Settings.DisplayLanguage == expected.DisplayLanguage &&
                loaded.Settings.ModifyVeterancy == expected.ModifyVeterancy &&
                loaded.Settings.Veterancy == expected.Veterancy &&
                loaded.Settings.ModifyHealth == expected.ModifyHealth &&
                loaded.Settings.Health == expected.Health &&
                loaded.Settings.AutoEditMode == expected.AutoEditMode &&
                loaded.Settings.Money == expected.Money &&
                loaded.Settings.TechUnlockMode == expected.TechUnlockMode &&
                loaded.Settings.SelectedSuperweaponIds.SequenceEqual(
                    expected.SelectedSuperweaponIds) &&
                loaded.Settings.Hotkeys[
                    HotkeyActionIds.TransferOwner] == custom,
                loaded.Warning ?? "Saved settings did not round-trip.");

            XDocument document = XDocument.Load(path);
            XElement appSettings = document.Root!.Element("appSettings")!;
            Assert(
                appSettings.Elements("add").Any(element =>
                    (string?)element.Attribute("key") == "unrelated.keep" &&
                    (string?)element.Attribute("value") == "yes") &&
                document.Root.Element("startup") is not null,
                "Saving settings must preserve unrelated keys and startup.");
            appSettings.Elements("add")
                .Single(element =>
                    (string?)element.Attribute("key") == "ui.language")
                .SetAttributeValue("value", "Klingon");
            appSettings.Elements("add")
                .Single(element =>
                    (string?)element.Attribute("key") == "unit.health")
                .SetAttributeValue("value", "-3");
            appSettings.Elements("add")
                .Single(element =>
                    (string?)element.Attribute("key") ==
                        "hotkey.unit.auto-edit")
                .SetAttributeValue("value", "Ctrl+Ctrl+A");
            appSettings.Elements("add")
                .Single(element =>
                    (string?)element.Attribute("key") ==
                        "superweapon.selectedIds")
                .SetAttributeValue("value", "0,999,28");
            document.Save(path);

            TrainerSettingsLoadResult fallback = store.Load();
            Assert(
                fallback.Warning is not null &&
                fallback.Settings.DisplayLanguage == DisplayLanguage.Auto &&
                fallback.Settings.Health ==
                    TrainerSettings.CreateDefaults().Health &&
                fallback.Settings.Hotkeys[
                    HotkeyActionIds.AutoEdit] ==
                    HotkeyGesture.Ctrl(Keys.A) &&
                fallback.Settings.SelectedSuperweaponIds.SequenceEqual(
                    new uint[] { 0, 28 }),
                "Invalid persisted values must fall back with a warning.");

            File.WriteAllText(
                path,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <appSettings>
                    <add key="settings.version" value="1" />
                    <add key="hotkey.unit.auto-edit" value="Alt+A" />
                    <add key="hotkey.unit.promote-selected" value="Ctrl+A" />
                  </appSettings>
                  <startup>
                    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
                  </startup>
                </configuration>
                """);
            TrainerSettingsLoadResult migrated = store.Load();
            Assert(
                migrated.Settings.DisplayLanguage == DisplayLanguage.Auto &&
                migrated.Settings.Hotkeys[
                    HotkeyActionIds.AutoEdit] ==
                    HotkeyGesture.Ctrl(Keys.A),
                "Legacy Alt+A auto edit must migrate to the new Ctrl+A default.");
            Assert(
                store.Save(migrated.Settings) is null,
                "Migrated settings must save successfully.");
            XDocument migratedDocument = XDocument.Load(path);
            XElement migratedAppSettings =
                migratedDocument.Root!.Element("appSettings")!;
            Assert(
                migratedAppSettings.Elements("add").Any(element =>
                    (string?)element.Attribute("key") ==
                        "settings.version" &&
                    (string?)element.Attribute("value") == "2") &&
                !migratedAppSettings.Elements("add").Any(element =>
                    (string?)element.Attribute("key") ==
                        "hotkey.unit.promote-selected"),
                "Saving migrated settings must remove the obsolete promotion hotkey.");
            Assert(
                !File.Exists(path + ".tmp") &&
                !File.Exists(path + ".bak"),
                "Atomic settings save must not leave temporary files.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RunWithUiCulture(
        string cultureName,
        Action action)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture =
                CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
