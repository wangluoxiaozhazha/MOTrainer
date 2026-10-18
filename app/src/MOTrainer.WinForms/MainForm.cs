using MOTrainer.Core.Actions;
using MOTrainer.Core.Features;
using MOTrainer.Core.Hooks;
using MOTrainer.Core.Patching;
using MOTrainer.Core.Profiles;
using MOTrainer.Core.Session;

namespace MOTrainer.WinForms;

public sealed class MainForm : Form
{
    private readonly IGameSessionProbe sessionProbe;
    private readonly GameActionService actions;
    private readonly Label statusLabel;
    private readonly GroupBox unitGroup;
    private readonly GroupBox globalStateGroup;
    private readonly GroupBox oneTimeGroup;
    private readonly CheckBox editVeterancyControl;
    private readonly ComboBox editVeterancyInput;
    private readonly CheckBox editHealthControl;
    private readonly NumericUpDown editHealthInput;
    private readonly Button applyUnitEditButton;
    private readonly CheckBox autoEditControl;
    private readonly NumericUpDown moneyInput;
    private readonly Button moneyButton;
    private readonly CheckBox moneyAutoControl;
    private readonly Button hotkeySettingsButton;
    private readonly ComboBox languageInput;
    private readonly Button addHealthButton;
    private readonly Button oneHealthButton;
    private readonly Button transferButton;
    private readonly Button zeroEconomyButton;
    private readonly Button defeatOwnerButton;
    private readonly Button grantSelectedSuperweaponsButton;
    private readonly Label superweaponHotkeyDescription;
    private readonly ComboBox techUnlockModeInput;
    private readonly ComboBox autoEditModeInput;
    private readonly CheckBox unitUnlimitedControl;
    private readonly CheckBox queueLimitControl;
    private readonly DelayedBuildPolicyService buildPolicies;
    private readonly AutoApplyPolicyService autoApply;
    private readonly PatchPolicyService patchPolicies;
    private readonly Label resultLabel;
    private DelayedBuildPolicySnapshot? delayedBuildSnapshot;
    private readonly Queue<StatusNotification> notifications = new();
    private StatusNotification? visibleNotification;
    private DateTime notificationExpiresAt;
    private readonly ToolTip hotkeyTips;
    private readonly HotkeyBindings hotkeyBindings;
    private readonly TrainerSettingsStore settingsStore;
    private readonly TrainerSettings initialSettings;
    private readonly string? settingsLoadWarning;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly System.Threading.Timer autoEditTimer;
    private readonly object autoEditGate = new();
    private readonly ManualResetEventSlim autoEditIdle = new(true);
    private readonly Dictionary<string, PatchBinding> patchBindings = [];
    private readonly Dictionary<string, int> ownedPatchProcesses = [];
    private IReadOnlyDictionary<string, PatchPolicyStatus> patchPolicyStatuses =
        new Dictionary<string, PatchPolicyStatus>();
    private readonly Dictionary<uint, CheckBox> superweaponControls = [];
    private readonly Dictionary<Control, HotkeyDisplayBinding>
        hotkeyDisplayBindings = [];
    private GlobalHotkeyManager? hotkeys;
    private bool synchronizingPatchState;
    private bool actionInProgress;
    private bool autoEditTickActive;
    private bool autoEditEnabled;
    private bool matchControlsAvailable;
    private bool patchControlsAvailable;
    private bool languageRefreshRequested;
    private AutoEditMode? activeAutoEditMode;
    private SelectedUnitEditRequest? autoEditRequest;

    public MainForm(IGameSessionProbe sessionProbe)
    {
        this.sessionProbe = sessionProbe;
        actions = new GameActionService(sessionProbe);
        buildPolicies = new DelayedBuildPolicyService(sessionProbe);
        autoApply = new AutoApplyPolicyService(sessionProbe);
        patchPolicies = new PatchPolicyService(sessionProbe);
        settingsStore = new TrainerSettingsStore();
        TrainerSettingsLoadResult settingsLoad = settingsStore.Load();
        initialSettings = settingsLoad.Settings;
        settingsLoadWarning = settingsLoad.Warning;
        hotkeyBindings = initialSettings.Hotkeys;

        Text = UiStrings.Get("AppTitle");
        ClientSize = new Size(1040, 598);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        hotkeyTips = new ToolTip();

        statusLabel = new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(16, 16),
            Size = new Size(680, 46),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        Label languageLabel = new()
        {
            AutoSize = false,
            Location = new Point(712, 14),
            Size = new Size(144, 17),
            Text = UiStrings.Get("DisplayLanguageLabel"),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        languageInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(712, 34),
            Size = new Size(144, 24),
        };
        languageInput.Items.AddRange(
        [
            new DisplayLanguageOption(
                DisplayLanguage.Auto,
                UiStrings.Get("LanguageAuto")),
            new DisplayLanguageOption(
                DisplayLanguage.SimplifiedChinese,
                UiStrings.Get("LanguageSimplifiedChinese")),
            new DisplayLanguageOption(
                DisplayLanguage.English,
                UiStrings.Get("LanguageEnglish")),
        ]);
        languageInput.SelectedIndex = initialSettings.DisplayLanguage switch
        {
            DisplayLanguage.SimplifiedChinese => 1,
            DisplayLanguage.English => 2,
            _ => 0,
        };
        hotkeySettingsButton = new Button
        {
            Location = new Point(872, 16),
            Size = new Size(152, 46),
            Text = UiStrings.Get("HotkeySettings"),
        };
        hotkeySettingsButton.Click += (_, _) =>
            OpenHotkeySettings();

        unitGroup = new GroupBox
        {
            Location = new Point(16, 76),
            Size = new Size(1008, 106),
            Text = UiStrings.Get("SelectedUnitsGroup"),
        };

        unitGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 31),
            Text = UiStrings.Get("Rank"),
        });
        editVeterancyControl = new CheckBox
        {
            AutoSize = true,
            Checked = initialSettings.ModifyVeterancy,
            Location = new Point(218, 30),
            Text = UiStrings.Get("ButtonEdit"),
        };
        editVeterancyInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(68, 27),
            Size = new Size(138, 24),
        };
        editVeterancyInput.Items.Add(
            new VeterancyOption(
                UnitVeterancyLevel.Rookie,
                UiStrings.Get("RankRookie")));
        editVeterancyInput.Items.Add(
            new VeterancyOption(
                UnitVeterancyLevel.Veteran,
                UiStrings.Get("RankVeteran")));
        editVeterancyInput.Items.Add(
            new VeterancyOption(
                UnitVeterancyLevel.Elite,
                UiStrings.Get("RankElite")));
        editVeterancyInput.SelectedIndex =
            (int)initialSettings.Veterancy;

        editHealthControl = new CheckBox
        {
            AutoSize = true,
            Checked = initialSettings.ModifyHealth,
            Location = new Point(218, 66),
            Text = UiStrings.Get("ButtonEdit"),
        };
        editHealthInput = new NumericUpDown
        {
            Location = new Point(68, 63),
            Maximum = int.MaxValue,
            Minimum = 1,
            Size = new Size(138, 23),
            ThousandsSeparator = true,
            Value = initialSettings.Health,
        };
        applyUnitEditButton = new Button
        {
            Location = new Point(318, 27),
            Size = new Size(250, 29),
            Text = UiStrings.Get("ButtonEditSelected"),
        };
        applyUnitEditButton.Click += (_, _) =>
            RunAction(() =>
                BuildSelectedUnitEditRequest() is { } request
                    ? actions.EditSelectedUnits(request)
                    : ActionResult.Fail(UiStrings.Get("SelectUnitAttribute")));
        editVeterancyControl.CheckedChanged += (_, _) =>
            RefreshUnitEditorInputsAndAutoEdit();
        editHealthControl.CheckedChanged += (_, _) =>
            RefreshUnitEditorInputsAndAutoEdit();
        editVeterancyInput.SelectedIndexChanged += (_, _) =>
            RefreshAutoApplyRequest();
        editHealthInput.ValueChanged += (_, _) =>
            RefreshAutoApplyRequest();

        transferButton = new Button
        {
            Location = new Point(748, 27),
            Size = new Size(214, 29),
            Text = UiStrings.Get("ButtonTakeOwnership"),
        };
        transferButton.Click += (_, _) => RunAction(
            actions.TransferSelectedUnitsToCurrentPlayer);
        SetHotkeyToolTip(
            transferButton,
            HotkeyActionIds.TransferOwner);

        addHealthButton = new Button
        {
            Location = new Point(748, 63),
            Size = new Size(106, 29),
            Text = UiStrings.Get("ButtonHeal"),
        };
        addHealthButton.Click += (_, _) => RunAction(() =>
            actions.AddHealthToSelectedUnits(2000));
        SetHotkeyToolTip(
            addHealthButton,
            HotkeyActionIds.AddHealth);

        oneHealthButton = new Button
        {
            Location = new Point(856, 63),
            Size = new Size(106, 29),
            Text = UiStrings.Get("ButtonSetHealthOne"),
        };
        oneHealthButton.Click += (_, _) => RunAction(() =>
            actions.SetSelectedUnitsHealth(1));
        SetHotkeyToolTip(
            oneHealthButton,
            HotkeyActionIds.SetOneHealth);

        unitGroup.Controls.Add(editVeterancyControl);
        unitGroup.Controls.Add(editVeterancyInput);
        unitGroup.Controls.Add(editHealthControl);
        unitGroup.Controls.Add(editHealthInput);
        unitGroup.Controls.Add(applyUnitEditButton);
        unitGroup.Controls.Add(transferButton);
        unitGroup.Controls.Add(addHealthButton);
        unitGroup.Controls.Add(oneHealthButton);
        unitGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 67),
            Text = UiStrings.Get("Health"),
        });
        unitGroup.Controls.Add(new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(298, 24),
            Size = new Size(2, 68),
        });
        unitGroup.Controls.Add(new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(730, 24),
            Size = new Size(2, 68),
        });
        globalStateGroup = new GroupBox
        {
            Location = new Point(16, 196),
            Size = new Size(494, 326),
            Text = UiStrings.Get("GlobalStateGroup"),
        };
        globalStateGroup.Controls.Add(
            CreateSectionLabel(
                UiStrings.Get("EconomyProductionSection"),
                new Point(18, 24)));
        globalStateGroup.Controls.Add(
            CreateSeparator(new Point(18, 160), 394));
        globalStateGroup.Controls.Add(
            CreateSectionLabel(
                UiStrings.Get("BattlefieldSection"),
                new Point(18, 170)));
        globalStateGroup.Controls.Add(
            CreateSeparator(new Point(18, 250), 394));
        globalStateGroup.Controls.Add(
            CreateSectionLabel(
                UiStrings.Get("TechnologySection"),
                new Point(18, 260)));

        AddPatchControl(
            KnownPatchIds.EconomyNoDecrease,
            UiStrings.Get("FeatureLockEconomy"),
            HotkeyActionIds.Patch(KnownPatchIds.EconomyNoDecrease),
            new Point(18, 48));
        AddPatchControl(
            KnownPatchIds.PowerNoDecrease,
            UiStrings.Get("FeatureLockPower"),
            HotkeyActionIds.Patch(KnownPatchIds.PowerNoDecrease),
            new Point(218, 48));
        AddPatchControl(
            KnownPatchIds.InstantUnitBuild,
            UiStrings.Get("FeatureInstantBuild"),
            HotkeyActionIds.Patch(KnownPatchIds.InstantUnitBuild),
            new Point(18, 76));
        AddPatchControl(
            KnownPatchIds.IgnoreBuildDistance,
            UiStrings.Get("FeatureBuildAnywhere"),
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreBuildDistance),
            new Point(218, 76));
        AddPatchControl(
            KnownPatchIds.AutoRepair,
            UiStrings.Get("FeatureAutoRepair"),
            HotkeyActionIds.Patch(KnownPatchIds.AutoRepair),
            new Point(18, 104));

        unitUnlimitedControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(218, 104),
            Text = UiStrings.Get("UnitUnlimitedOff"),
        };
        unitUnlimitedControl.Click += (_, _) =>
            ToggleDelayedBuildFeature(
                DelayedBuildFeature.UnitUnlimited);
        queueLimitControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(18, 132),
            Text = UiStrings.Get("QueueLimitOff"),
        };
        queueLimitControl.Click += (_, _) =>
            ToggleDelayedBuildFeature(
                DelayedBuildFeature.QueueLimit1000);
        globalStateGroup.Controls.Add(unitUnlimitedControl);
        globalStateGroup.Controls.Add(queueLimitControl);

        AddPatchControl(
            KnownPatchIds.ControlAllUnits,
            UiStrings.Get("FeatureControlAll"),
            HotkeyActionIds.Patch(KnownPatchIds.ControlAllUnits),
            new Point(18, 194));
        AddPatchControl(
            KnownPatchIds.PsychicDetection,
            UiStrings.Get("FeaturePsychicDetection"),
            HotkeyActionIds.Patch(KnownPatchIds.PsychicDetection),
            new Point(218, 194));
        AddPatchControl(
            KnownPatchIds.IgnoreCloak,
            UiStrings.Get("FeatureIgnoreCloak"),
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreCloak),
            new Point(18, 222));
        AddPatchControl(
            KnownPatchIds.StableRevealMap,
            UiStrings.Get("FeatureRevealMap"),
            HotkeyActionIds.Patch(KnownPatchIds.StableRevealMap),
            new Point(218, 222));
        AddPatchControl(
            KnownPatchIds.SafeTechUnlock,
            UiStrings.Get("FeatureTechUnlock"),
            HotkeyActionIds.Patch(KnownPatchIds.SafeTechUnlock),
            new Point(18, 286));

        techUnlockModeInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(180, 282),
            Size = new Size(232, 24),
        };
        techUnlockModeInput.Items.Add(
            new TechUnlockModeOption(
                TechUnlockMode.Faction,
                UiStrings.Get("ModeFactionRecommended")));
        techUnlockModeInput.Items.Add(
            new TechUnlockModeOption(
                TechUnlockMode.Extended,
                UiStrings.Get("ModeExtended")));
        techUnlockModeInput.SelectedIndex =
            initialSettings.TechUnlockMode == TechUnlockMode.Extended
                ? 1
                : 0;
        techUnlockModeInput.SelectedIndexChanged += (_, _) =>
            ChangeTechUnlockModeIfEnabled();
        globalStateGroup.Controls.Add(techUnlockModeInput);

        Label autoEditModeLabel = new()
        {
            AutoSize = true,
            Location = new Point(430, 67),
            Text = UiStrings.Get("AutoModeLabel"),
        };
        unitGroup.Controls.Add(autoEditModeLabel);

        autoEditModeInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(478, 63),
            Size = new Size(230, 24),
        };
        autoEditModeInput.Items.Add(
            new AutoEditModeOption(
                AutoEditMode.Event,
                UiStrings.Get("ModeEventRecommended")));
        autoEditModeInput.Items.Add(
            new AutoEditModeOption(
                AutoEditMode.Polling,
                UiStrings.Get("ModePollingCompatibility")));
        autoEditModeInput.SelectedIndex =
            initialSettings.AutoEditMode == AutoEditMode.Polling
                ? 1
                : 0;
        autoEditModeInput.SelectedIndexChanged += (_, _) =>
            ChangeAutoEditModeIfEnabled();
        unitGroup.Controls.Add(autoEditModeInput);
        hotkeyTips.SetToolTip(
            autoEditModeInput,
            UiStrings.Get("AutoModeTooltip"));

        autoEditControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(318, 67),
            Text = UiStrings.Get("ActionAutoEdit"),
        };
        autoEditControl.Click += (_, _) =>
            RunAction(ToggleAutoEdit);
        SetHotkeyToolTip(
            autoEditControl,
            HotkeyActionIds.AutoEdit,
            UiStrings.Get("AutoApplyTooltip"));
        unitGroup.Controls.Add(autoEditControl);

        oneTimeGroup = new GroupBox
        {
            Enabled = true,
            Location = new Point(526, 196),
            Size = new Size(498, 326),
            Text = UiStrings.Get("OneTimeGroup"),
        };
        oneTimeGroup.Controls.Add(
            CreateSectionLabel(
                UiStrings.Get("EconomyFactionSection"),
                new Point(18, 24)));
        oneTimeGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 53),
            Text = UiStrings.Get("Credits"),
        });
        moneyInput = new NumericUpDown
        {
            Location = new Point(62, 49),
            Maximum = uint.MaxValue,
            Size = new Size(190, 23),
            ThousandsSeparator = true,
            Value = initialSettings.Money,
        };
        moneyButton = new Button
        {
            Location = new Point(270, 48),
            Size = new Size(116, 25),
            Text = UiStrings.Get("ButtonEdit"),
        };
        moneyButton.Click += (_, _) => RunAction(() =>
            actions.SetMoney(decimal.ToUInt32(moneyInput.Value)));
        moneyAutoControl = new CheckBox
        {
            AutoSize = true,
            Checked = initialSettings.AutoApplyMoney,
            Location = new Point(392, 51),
            Text = UiStrings.Get("ActionAutoMoney"),
        };
        moneyAutoControl.Click += (_, _) =>
            ApplyAutoMoney();
        moneyInput.ValueChanged += (_, _) =>
        {
            if (moneyAutoControl.Checked)
            {
                ApplyAutoMoney();
            }
        };
        hotkeyTips.SetToolTip(
            moneyAutoControl,
            UiStrings.Get("AutoMoneyTooltip"));
        zeroEconomyButton = new Button
        {
            Location = new Point(18, 82),
            Size = new Size(368, 28),
            Text = UiStrings.Get("ActionZeroEconomy"),
        };
        zeroEconomyButton.Click += (_, _) => RunAction(
            actions.ZeroFirstSelectedOwnerEconomy);
        SetHotkeyToolTip(
            zeroEconomyButton,
            HotkeyActionIds.ZeroEconomy);
        defeatOwnerButton = new Button
        {
            Location = new Point(18, 114),
            Size = new Size(368, 28),
            Text = UiStrings.Get("ActionDefeatOwner"),
        };
        defeatOwnerButton.Click += (_, _) => RunAction(
            actions.DefeatFirstSelectedOwner);
        SetHotkeyToolTip(
            defeatOwnerButton,
            HotkeyActionIds.DefeatOwner);
        oneTimeGroup.Controls.Add(moneyInput);
        oneTimeGroup.Controls.Add(moneyButton);
        oneTimeGroup.Controls.Add(moneyAutoControl);
        oneTimeGroup.Controls.Add(zeroEconomyButton);
        oneTimeGroup.Controls.Add(defeatOwnerButton);
        oneTimeGroup.Controls.Add(
            CreateSeparator(new Point(18, 151), 368));
        oneTimeGroup.Controls.Add(
            CreateSectionLabel(
                UiStrings.Get("SuperweaponsSection"),
                new Point(18, 159)));

        (string Title, Point Location, uint[] Ids)[] superweaponFactions =
        [
            (
                UiStrings.Get("FactionAllies"),
                new Point(18, 178),
                [2, 3]),
            (
                UiStrings.Get("FactionSoviets"),
                new Point(156, 178),
                [0, 1]),
            (
                UiStrings.Get("FactionEpsilon"),
                new Point(18, 242),
                [7, 28]),
            (
                UiStrings.Get("FactionFoehn"),
                new Point(156, 242),
                [48, 47]),
        ];
        IReadOnlyDictionary<uint, SuperweaponChoice> choicesById =
            SuperweaponChoices.All.ToDictionary(choice => choice.Id);
        foreach ((string title, Point location, uint[] ids) in
            superweaponFactions)
        {
            GroupBox factionGroup = new()
            {
                Location = location,
                Size = new Size(132, 60),
                Text = title,
            };
            for (int index = 0; index < ids.Length; index++)
            {
                SuperweaponChoice choice = choicesById[ids[index]];
                CheckBox control = new()
                {
                    AutoSize = true,
                    Checked = initialSettings.SelectedSuperweaponIds.Contains(
                        choice.Id),
                    Location = new Point(10, 17 + index * 20),
                    Text = choice.DisplayName,
                };
                superweaponControls.Add(choice.Id, control);
                factionGroup.Controls.Add(control);
            }

            oneTimeGroup.Controls.Add(factionGroup);
        }
        grantSelectedSuperweaponsButton = new Button
        {
            Location = new Point(300, 181),
            Size = new Size(180, 52),
            Text = UiStrings.Get("ButtonGrantSelectedSuperweapons"),
        };
        grantSelectedSuperweaponsButton.Click += (_, _) =>
            RunAction(GrantSelectedSuperweapons);
        SetHotkeyToolTip(
            grantSelectedSuperweaponsButton,
            HotkeyActionIds.GrantSelectedSuperweapons);
        oneTimeGroup.Controls.Add(grantSelectedSuperweaponsButton);
        superweaponHotkeyDescription = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            ForeColor = Color.DimGray,
            Location = new Point(300, 241),
            Size = new Size(180, 62),
        };
        oneTimeGroup.Controls.Add(superweaponHotkeyDescription);

        resultLabel = new Label
        {
            AutoEllipsis = true,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(16, 536),
            Size = new Size(1008, 46),
            Text = UiStrings.Get("CompatibilityNotice"),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(statusLabel);
        Controls.Add(languageLabel);
        Controls.Add(languageInput);
        Controls.Add(hotkeySettingsButton);
        Controls.Add(unitGroup);
        Controls.Add(globalStateGroup);
        Controls.Add(oneTimeGroup);
        Controls.Add(resultLabel);
        hotkeyTips.SetToolTip(
            languageInput,
            UiStrings.Get("DisplayLanguageTooltip"));
        languageInput.SelectedIndexChanged += (_, _) =>
            SaveDisplayLanguage();

        refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 500,
        };
        refreshTimer.Tick += (_, _) => RefreshSession();
        refreshTimer.Start();
        autoEditTimer = new System.Threading.Timer(
            _ => RunAutoEditTick(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        // Seed the persistent policy from the saved settings so a remembered
        // automatic-credits choice keeps working without touching the control.
        autoApply.SetMoney(
            initialSettings.AutoApplyMoney,
            decimal.ToUInt32(moneyInput.Value));
        RefreshSession();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ReloadHotkeys();
    }

    internal HotkeyBindings ConfiguredHotkeys => hotkeyBindings;

    internal bool LanguageRefreshRequested => languageRefreshRequested;

    internal void ReloadHotkeys()
    {
        hotkeys?.Dispose();
        hotkeys = CreateHotkeyManager(
            hotkeyBindings,
            out List<string> failures);
        RefreshHotkeyDisplays();

        ShowHotkeyRegistrationResult(
            failures,
            settingsLoadWarning);
    }

    private GlobalHotkeyManager CreateHotkeyManager(
        HotkeyBindings bindings,
        out List<string> failures)
    {
        GlobalHotkeyManager manager = new(Handle);
        failures = [];
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.AddHealth,
            () => actions.AddHealthToSelectedUnits(2000),
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.SetOneHealth,
            () => actions.SetSelectedUnitsHealth(1),
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.TransferOwner,
            actions.TransferSelectedUnitsToCurrentPlayer,
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.ZeroEconomy,
            actions.ZeroFirstSelectedOwnerEconomy,
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.DefeatOwner,
            actions.DefeatFirstSelectedOwner,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.EconomyNoDecrease,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.PowerNoDecrease,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.InstantUnitBuild,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.ControlAllUnits,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.IgnoreBuildDistance,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.PsychicDetection,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.IgnoreCloak,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.StableRevealMap,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.AutoRepair,
            failures);
        RegisterPatchHotkey(
            manager,
            bindings,
            KnownPatchIds.SafeTechUnlock,
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.AutoEdit,
            ToggleAutoEdit,
            failures);
        RegisterHotkey(
            manager,
            bindings,
            HotkeyActionIds.GrantSelectedSuperweapons,
            GrantSelectedSuperweapons,
            failures);

        return manager;
    }

    private void ShowHotkeyRegistrationResult(
        IReadOnlyCollection<string> failures,
        string? warning = null)
    {
        if (failures.Count > 0)
        {
            ShowNotification(UiStrings.Format(
                "HotkeyRegistrationFailed",
                string.Join(", ", failures)), Color.DarkRed);
        }
        else if (warning is not null)
        {
            ShowNotification(warning, Color.DarkOrange);
        }
        else
        {
            ShowNotification(UiStrings.Get("GlobalHotkeyRegistered"), Color.DarkGreen);
        }
    }

    private void OpenHotkeySettings()
    {
        HotkeyBindings original = hotkeyBindings.Clone();
        hotkeys?.Dispose();
        hotkeys = null;

        using HotkeySettingsForm dialog = new(original);
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            dialog.ResultBindings is not HotkeyBindings candidate)
        {
            hotkeys = CreateHotkeyManager(
                original,
                out List<string> restoreFailures);
            ShowHotkeyRegistrationResult(restoreFailures);
            return;
        }

        GlobalHotkeyManager candidateManager =
            CreateHotkeyManager(
                candidate,
                out List<string> registrationFailures);
        if (registrationFailures.Count > 0)
        {
            candidateManager.Dispose();
            hotkeys = CreateHotkeyManager(
                original,
                out List<string> restoreFailures);
            string restoreSuffix = restoreFailures.Count == 0
                ? UiStrings.Get("HotkeyRestored")
                : UiStrings.Format(
                    "HotkeyRestoreFailed",
                    string.Join(", ", restoreFailures));
            ShowNotification(UiStrings.Format(
                "HotkeyNewRegistrationFailed",
                string.Join(", ", registrationFailures),
                restoreSuffix), Color.DarkRed);
            return;
        }

        hotkeys = candidateManager;
        hotkeyBindings.ReplaceWith(candidate);
        RefreshHotkeyDisplays();
        string? saveError = settingsStore.Save(CaptureSettings());
        if (saveError is null)
        {
            ShowNotification(UiStrings.Get("HotkeySaved"), Color.DarkGreen);
            return;
        }

        hotkeys.Dispose();
        hotkeyBindings.ReplaceWith(original);
        hotkeys = CreateHotkeyManager(
            original,
            out List<string> rollbackFailures);
        RefreshHotkeyDisplays();
        ShowNotification(rollbackFailures.Count == 0
            ? UiStrings.Format("HotkeyRollbackSucceeded", saveError)
            : UiStrings.Format(
                "HotkeyRollbackFailed",
                saveError,
                string.Join(", ", rollbackFailures)), Color.DarkRed);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        hotkeys?.Dispose();
        hotkeys = null;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (hotkeys?.ProcessMessage(ref message) == true)
        {
            return;
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
            DisarmAutoEdit();
            autoEditTimer.Dispose();
            autoEditIdle.Dispose();
            hotkeyTips.Dispose();
            hotkeys?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        refreshTimer.Stop();
        List<string> restoreFailures = [];
        ActionResult autoEditRestore = DisarmAutoEdit();
        if (!autoEditRestore.Success)
        {
            restoreFailures.Add(autoEditRestore.Message);
        }

        GameSessionSnapshot closingSession = sessionProbe.Probe();
        AutoApplySnapshot autoApplyRestore =
            autoApply.RestoreOwned(closingSession);
        patchPolicies.RestoreOwned(closingSession);
        foreach (AutoApplyStatus status in new[]
        {
            autoApplyRestore.Money,
            autoApplyRestore.SelectionEdit,
        })
        {
            if (status.State == FeatureState.Failed)
            {
                restoreFailures.Add(status.Message);
            }
        }

        DelayedBuildPolicySnapshot delayedRestore =
            buildPolicies.RestoreOwned();
        foreach (DelayedFeatureStatus status in new[]
        {
            delayedRestore.UnitUnlimited,
            delayedRestore.QueueLimit1000,
        })
        {
            if (status.State == FeatureState.Failed)
            {
                restoreFailures.Add(status.Message);
            }
        }

        foreach (KeyValuePair<string, int> ownedPatch in
            ownedPatchProcesses.ToArray())
        {
            string patchId = ownedPatch.Key;
            PatchBinding binding = patchBindings[patchId];
            PatchInspection inspection = actions.InspectPatch(patchId);

            if (inspection.State == PatchRuntimeState.Enabled)
            {
                ActionResult restore =
                    actions.SetPatchEnabled(patchId, binding.DisplayName, false);
                if (!restore.Success)
                {
                    restoreFailures.Add(restore.Message);
                    continue;
                }
            }
            else if (inspection.State == PatchRuntimeState.Unknown)
            {
                restoreFailures.Add(UiStrings.Format(
                    "PatchRestoreBlocked", binding.DisplayName));
                continue;
            }

            ownedPatchProcesses.Remove(patchId);
        }

        if (restoreFailures.Count > 0 &&
            CancelCloseForRestoreFailure(e, string.Join(Environment.NewLine, restoreFailures)))
        {
            return;
        }

        string? settingsError = settingsStore.Save(CaptureSettings());
        if (settingsError is not null)
        {
            MessageBox.Show(
                this,
                settingsError,
                UiStrings.Get("SettingsNotSavedTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        base.OnFormClosing(e);
    }

    private TrainerSettings CaptureSettings()
    {
        UnitVeterancyLevel veterancy =
            editVeterancyInput.SelectedItem is VeterancyOption option
                ? option.Level
                : UnitVeterancyLevel.Elite;
        TechUnlockMode techUnlockMode =
            techUnlockModeInput.SelectedItem is TechUnlockModeOption techOption
                ? techOption.Mode
                : TechUnlockMode.Faction;
        return new TrainerSettings(
            SelectedDisplayLanguage,
            editVeterancyControl.Checked,
            veterancy,
            editHealthControl.Checked,
            decimal.ToInt32(editHealthInput.Value),
            SelectedAutoEditMode,
            decimal.ToUInt32(moneyInput.Value),
            moneyAutoControl.Checked,
            techUnlockMode,
            SuperweaponChoices.All
                .Where(choice => superweaponControls[choice.Id].Checked)
                .Select(choice => choice.Id)
                .ToArray(),
            hotkeyBindings);
    }

    private DisplayLanguage SelectedDisplayLanguage =>
        languageInput.SelectedItem is DisplayLanguageOption option
            ? option.Language
            : DisplayLanguage.Auto;

    private void SaveDisplayLanguage()
    {
        string? error = settingsStore.Save(CaptureSettings());
        if (error is not null)
        {
            languageRefreshRequested = false;
            ShowNotification(error, Color.DarkRed);
            return;
        }

        languageRefreshRequested = true;
        ShowNotification(UiStrings.Get("LanguageRefreshing"), Color.DarkOrange);
        BeginInvoke(new Action(Close));
    }

    private void AddPatchControl(
        string patchId,
        string displayName,
        string hotkeyActionId,
        Point location,
        Control? parent = null)
    {
        CheckBox control = new()
        {
            AutoCheck = false,
            AutoSize = true,
            Location = location,
            ThreeState = true,
            Text = displayName,
        };

        PatchBinding binding = new(patchId, displayName, control);
        control.Click += (_, _) => TogglePatch(binding);
        patchBindings.Add(patchId, binding);
        SetHotkeyToolTip(control, hotkeyActionId);
        (parent ?? globalStateGroup).Controls.Add(control);
    }

    private ActionResult GrantSelectedSuperweapons()
    {
        uint[] ids = SuperweaponChoices.All
            .Where(choice => superweaponControls[choice.Id].Checked)
            .Select(choice => choice.Id)
            .ToArray();
        return ids.Length == 0
            ? ActionResult.Fail(UiStrings.Get("SelectOneSuperweapon"))
            : actions.GrantSuperweapons(ids);
    }

    private static Label CreateSectionLabel(
        string text,
        Point location) =>
        new()
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = location,
            Text = text,
        };

    private static Label CreateSeparator(
        Point location,
        int width) =>
        new()
        {
            AutoSize = false,
            BorderStyle = BorderStyle.Fixed3D,
            Location = location,
            Size = new Size(width, 2),
        };

    private void RegisterHotkey(
        GlobalHotkeyManager manager,
        HotkeyBindings bindings,
        string actionId,
        Func<ActionResult> action,
        ICollection<string> failures)
    {
        HotkeyGesture gesture = bindings[actionId];
        if (!manager.Register(
                gesture,
                () => RunAction(action),
                out _))
        {
            failures.Add(gesture.DisplayText);
        }
    }

    private void RegisterPatchHotkey(
        GlobalHotkeyManager manager,
        HotkeyBindings bindings,
        string patchId,
        ICollection<string> failures)
    {
        string actionId = HotkeyActionIds.Patch(patchId);
        HotkeyGesture gesture = bindings[actionId];
        if (!manager.Register(
                gesture,
                () => TogglePatch(patchBindings[patchId]),
                out _))
        {
            failures.Add(gesture.DisplayText);
        }
    }

    private void SetHotkeyToolTip(
        Control control,
        string actionId,
        string? description = null)
    {
        hotkeyDisplayBindings[control] = new(
            actionId,
            control.Text,
            description);
        RefreshHotkeyDisplay(control);
    }

    private void RefreshHotkeyDisplays()
    {
        foreach (Control control in hotkeyDisplayBindings.Keys)
        {
            RefreshHotkeyDisplay(control);
        }

        if (hotkeyBindings.TryGet(
                HotkeyActionIds.GrantSelectedSuperweapons,
                out HotkeyGesture gesture))
        {
            superweaponHotkeyDescription.Text =
                UiStrings.Format(
                    "HotkeyBatchDescriptionWithGesture",
                    gesture.DisplayText);
        }
        else
        {
            superweaponHotkeyDescription.Text =
                UiStrings.Get("HotkeyBatchDescription");
        }
    }

    private void RefreshHotkeyDisplay(Control control)
    {
        HotkeyDisplayBinding binding =
            hotkeyDisplayBindings[control];
        if (hotkeyBindings.TryGet(
                binding.ActionId,
                out HotkeyGesture gesture))
        {
            control.Text =
                $"{binding.BaseText}  {gesture.DisplayText}";
            hotkeyTips.SetToolTip(
                control,
                string.IsNullOrWhiteSpace(binding.Description)
                    ? UiStrings.Format(
                        "HotkeySystemLabel",
                        gesture.DisplayText)
                    : binding.Description + Environment.NewLine +
                        UiStrings.Format(
                            "HotkeySystemLabel",
                            gesture.DisplayText));
        }
        else
        {
            control.Text = binding.BaseText;
            hotkeyTips.SetToolTip(
                control,
                binding.Description ?? string.Empty);
        }
    }

    private void RefreshSession()
    {
        GameSessionSnapshot snapshot = sessionProbe.Probe();
        SetTextIfChanged(statusLabel, snapshot.Message);
        SetForeColorIfChanged(
            statusLabel,
            snapshot.State switch
            {
                SessionState.NoProcess => Color.DimGray,
                SessionState.ProcessFound or SessionState.ModulesReady => Color.DarkOrange,
                SessionState.Unsupported or SessionState.Error => Color.DarkRed,
                _ => Color.DarkGreen,
            });

        matchControlsAvailable =
            snapshot.State == SessionState.MatchReady;
        patchControlsAvailable =
            snapshot.State is SessionState.ModulesReady or SessionState.MatchReady;
        SetActionControlsEnabled(matchControlsAvailable);

        if (snapshot.ProcessId is not int currentProcessId)
        {
            ownedPatchProcesses.Clear();
        }
        else
        {
            foreach (KeyValuePair<string, int> ownedPatch in
                ownedPatchProcesses.ToArray())
            {
                string patchId = ownedPatch.Key;
                int processId = ownedPatch.Value;
                if (processId != currentProcessId)
                {
                    ownedPatchProcesses.Remove(patchId);
                }
            }
        }

        RefreshPatchStates(snapshot);
        RefreshDelayedBuildStates(snapshot);
        // Keep the configured money and selected-unit policy applied for the
        // whole session; nothing is torn down until the trainer closes.
        RefreshAutoApplyStates(autoApply.Reconcile(snapshot));
        AdvanceNotificationQueue();
    }

    private void RunAction(Func<ActionResult> action)
    {
        if (actionInProgress)
        {
            return;
        }

        actionInProgress = true;
        try
        {
            ActionResult result = action();
            ShowNotification(result.Message,
                result.Success ? Color.DarkGreen : Color.DarkRed);
        }
        finally
        {
            actionInProgress = false;
            RefreshSession();
        }
    }

    private void TogglePatch(PatchBinding binding)
    {
        bool desired = !patchPolicies.IsDesired(binding.Id);
        patchPolicies.SetDesired(
            binding.Id,
            desired,
            binding.Id == KnownPatchIds.SafeTechUnlock
                ? SelectedTechUnlockMode
                : null);
        IReadOnlyDictionary<string, PatchPolicyStatus> statuses =
            patchPolicies.Reconcile();
        PatchPolicyStatus status =
            statuses.TryGetValue(binding.Id, out PatchPolicyStatus? current)
                ? current
                : new PatchPolicyStatus(
                    desired,
                    FeatureState.Pending,
                    UiStrings.Get("PatchPolicyWaitingForGame"));
        ShowNotification(
            status.Message,
            status.State switch
            {
                FeatureState.Active => Color.DarkGreen,
                FeatureState.Failed => Color.DarkRed,
                _ => Color.DarkOrange,
            });
        RefreshPatchStates();
    }

    private void ToggleTechUnlock(PatchBinding binding)
    {
        TogglePatch(binding);
    }

    private void ChangeTechUnlockModeIfEnabled()
    {
        if (synchronizingPatchState)
        {
            return;
        }

        patchPolicies.SetDesired(
            KnownPatchIds.SafeTechUnlock,
            true,
            SelectedTechUnlockMode);
        RefreshPatchStates();
    }

    private TechUnlockMode SelectedTechUnlockMode =>
        techUnlockModeInput.SelectedItem is TechUnlockModeOption option
            ? option.Mode
            : TechUnlockMode.Faction;

    private UnitVeterancyLevel? SelectedVeterancy =>
        editVeterancyControl.Checked &&
        editVeterancyInput.SelectedItem is VeterancyOption option
            ? option.Level
            : null;

    private SelectedUnitEditRequest? BuildSelectedUnitEditRequest()
    {
        UnitVeterancyLevel? veterancy = SelectedVeterancy;
        int? health = editHealthControl.Checked
            ? decimal.ToInt32(editHealthInput.Value)
            : null;
        return veterancy is null && health is null
            ? null
            : new SelectedUnitEditRequest(
                SelectedUnitEditScope.AllCurrentPlayer,
                veterancy,
                health);
    }

    private ActionResult ToggleAutoEdit()
    {
        if (autoEditEnabled)
        {
            return DisarmAutoEdit();
        }

        return ArmAutoEdit();
    }

    private ActionResult ArmAutoEdit()
    {
        SelectedUnitEditRequest? request =
            BuildSelectedUnitEditRequest();
        if (request is null)
        {
            return ActionResult.Fail(UiStrings.Get("SelectUnitAttribute"));
        }

        AutoEditMode mode = SelectedAutoEditMode;
        lock (autoEditGate)
        {
            autoEditRequest = request;
            activeAutoEditMode = mode;
            autoEditEnabled = true;
            if (mode == AutoEditMode.Polling)
            {
                autoEditTimer.Change(0, 100);
            }
        }
        autoEditControl.Checked = true;

        AutoApplySnapshot snapshot = mode == AutoEditMode.Event
            ? autoApply.SetSelectionEdit(request)
            : autoApply.SetSelectionEdit(null);
        RefreshAutoApplyStates(snapshot);
        return new ActionResult(
            snapshot.SelectionEdit.State != FeatureState.Failed,
            mode == AutoEditMode.Event
                ? snapshot.SelectionEdit.Message
                : UiStrings.Get("AutoEditArmed"));
    }

    private ActionResult DisarmAutoEdit()
    {
        lock (autoEditGate)
        {
            autoEditTimer.Change(
                Timeout.Infinite,
                Timeout.Infinite);
        }

        autoEditIdle.Wait();

        lock (autoEditGate)
        {
            autoEditEnabled = false;
            autoEditRequest = null;
            activeAutoEditMode = null;
        }

        autoEditControl.Checked = false;
        AutoApplySnapshot snapshot = autoApply.SetSelectionEdit(null);
        RefreshAutoApplyStates(snapshot);
        return new ActionResult(
            snapshot.SelectionEdit.State != FeatureState.Failed,
            UiStrings.Get("AutoEditDisabled"));
    }

    private void RunAutoEditTick()
    {
        SelectedUnitEditRequest? request;
        lock (autoEditGate)
        {
            if (!autoEditEnabled ||
                activeAutoEditMode != AutoEditMode.Polling ||
                autoEditTickActive ||
                autoEditRequest is null)
            {
                return;
            }

            autoEditTickActive = true;
            autoEditIdle.Reset();
            request = autoEditRequest;
        }

        try
        {
            actions.EditSelectedUnits(request);
        }
        finally
        {
            lock (autoEditGate)
            {
                autoEditTickActive = false;
                autoEditIdle.Set();
            }
        }
    }

    private void RefreshAutoApplyRequest()
    {
        if (!autoEditEnabled)
        {
            return;
        }

        SelectedUnitEditRequest? request =
            BuildSelectedUnitEditRequest();
        if (request is null)
        {
            ActionResult disarmed = DisarmAutoEdit();
            ShowNotification(disarmed.Success
                ? UiStrings.Get("AutoEditNoAttributesDisabled")
                : disarmed.Message, disarmed.Success
                ? Color.DarkOrange
                : Color.DarkRed);
            return;
        }

        lock (autoEditGate)
        {
            if (autoEditEnabled)
            {
                autoEditRequest = request;
            }
        }

        if (activeAutoEditMode != AutoEditMode.Event)
        {
            return;
        }

        AutoApplySnapshot snapshot = autoApply.SetSelectionEdit(request);
        RefreshAutoApplyStates(snapshot);
        if (snapshot.SelectionEdit.State == FeatureState.Failed)
        {
            ShowNotification(snapshot.SelectionEdit.Message, Color.DarkRed);
        }
    }

    private void ChangeAutoEditModeIfEnabled()
    {
        if (!autoEditEnabled ||
            activeAutoEditMode == SelectedAutoEditMode)
        {
            return;
        }

        RunAction(() =>
        {
            ActionResult disarmed = DisarmAutoEdit();
            return disarmed.Success
                ? ArmAutoEdit()
                : disarmed;
        });
    }

    private void ApplyAutoMoney()
    {
        AutoApplySnapshot snapshot = autoApply.SetMoney(
            moneyAutoControl.Checked,
            decimal.ToUInt32(moneyInput.Value));
        RefreshAutoApplyStates(snapshot);
        ShowNotification(
            snapshot.Money.Message,
            snapshot.Money.State == FeatureState.Failed
                ? Color.DarkRed
                : snapshot.Money.State == FeatureState.Active
                    ? Color.DarkGreen
                    : Color.DarkOrange);
    }

    private AutoEditMode SelectedAutoEditMode =>
        autoEditModeInput.SelectedItem is AutoEditModeOption option
            ? option.Mode
            : AutoEditMode.Event;

    private void RefreshPatchStates(
        GameSessionSnapshot? session = null)
    {
        GameSessionSnapshot snapshot = session ?? sessionProbe.Probe();
        patchPolicyStatuses = patchPolicies.Reconcile(snapshot);
        synchronizingPatchState = true;
        try
        {
            foreach (PatchBinding binding in patchBindings.Values)
            {
                if (patchPolicyStatuses.TryGetValue(
                        binding.Id,
                        out PatchPolicyStatus? status))
                {
                    SetCheckStateIfChanged(
                        binding.Control,
                        status.Desired
                            ? CheckState.Checked
                            : CheckState.Unchecked);
                    SetEnabledIfChanged(binding.Control, true);
                    SetForeColorIfChanged(
                        binding.Control,
                        status.State switch
                        {
                            FeatureState.Pending => Color.DarkOrange,
                            FeatureState.Active => Color.DarkGreen,
                            FeatureState.Failed => Color.DarkRed,
                            _ => SystemColors.ControlText,
                        });
                    continue;
                }

                // A patch without a saved preference is inspectable only once
                // the verified game modules are available.
                PatchInspection inspection = actions.InspectPatch(
                    binding.Id,
                    snapshot);
                SetCheckStateIfChanged(
                    binding.Control,
                    inspection.State switch
                    {
                        PatchRuntimeState.Original => CheckState.Unchecked,
                        PatchRuntimeState.Enabled => CheckState.Checked,
                        _ => CheckState.Indeterminate,
                    });
                SetEnabledIfChanged(binding.Control, true);
            }
        }
        finally
        {
            synchronizingPatchState = false;
        }
    }

    private void ToggleDelayedBuildFeature(
        DelayedBuildFeature feature)
    {
        DelayedBuildPolicySnapshot before =
            buildPolicies.Reconcile();
        ApplyDelayedBuildStates(before);
        bool desired = feature == DelayedBuildFeature.UnitUnlimited
            ? !before.UnitUnlimited.Desired
            : !before.QueueLimit1000.Desired;
        DelayedBuildPolicySnapshot after =
            buildPolicies.SetDesired(feature, desired);
        ApplyDelayedBuildStates(after);
    }

    private void ShowNotification(string message, Color color)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        StatusNotification notification = new(message, color);
        StatusNotification? last = notifications.Count > 0
            ? notifications.Last()
            : visibleNotification;
        if (notification == last)
        {
            return;
        }
        notifications.Enqueue(notification);
        AdvanceNotificationQueue();
    }

    private void AdvanceNotificationQueue()
    {
        DateTime now = DateTime.UtcNow;
        if (visibleNotification is not null && now < notificationExpiresAt)
        {
            return;
        }
        if (notifications.Count == 0)
        {
            return;
        }
        visibleNotification = notifications.Dequeue();
        notificationExpiresAt = now.AddSeconds(0.5);
        SetTextIfChanged(resultLabel, visibleNotification.Message);
        SetForeColorIfChanged(resultLabel, visibleNotification.Color);
        hotkeyTips.SetToolTip(resultLabel, visibleNotification.Message);
    }

    private void NotifyDelayedBuildTransition(
        string label, DelayedFeatureStatus? previous, DelayedFeatureStatus current)
    {
        if (current.State == FeatureState.Active && previous?.State != FeatureState.Active)
        {
            ShowNotification(UiStrings.Format("FeatureActivatedNotification", label),
                Color.DarkGreen);
        }
        else if (current.State == FeatureState.Failed &&
            (previous?.State != FeatureState.Failed || previous.Message != current.Message))
        {
            ShowNotification(label + ": " + current.Message, Color.DarkRed);
        }
        else if (current.State == FeatureState.Off && previous?.Desired == true)
        {
            ShowNotification(UiStrings.Format("FeatureDeactivatedNotification", label),
                SystemColors.ControlText);
        }
    }

    private sealed record StatusNotification(string Message, Color Color);

    private void RefreshDelayedBuildStates(
        GameSessionSnapshot? session = null)
    {
        ApplyDelayedBuildStates(
            session is null
                ? buildPolicies.Reconcile()
                : buildPolicies.Reconcile(session));
    }

    private void ApplyDelayedBuildStates(
        DelayedBuildPolicySnapshot snapshot)
    {
        DelayedBuildPolicySnapshot? previous = delayedBuildSnapshot;
        delayedBuildSnapshot = snapshot;
        NotifyDelayedBuildTransition(UiStrings.Get("UnitUnlimitedLabel"),
            previous?.UnitUnlimited, snapshot.UnitUnlimited);
        NotifyDelayedBuildTransition(UiStrings.Get("QueueLimitLabel"),
            previous?.QueueLimit1000, snapshot.QueueLimit1000);
        ApplyDelayedBuildState(
            unitUnlimitedControl,
            UiStrings.Get("UnitUnlimitedLabel"),
            snapshot.UnitUnlimited);
        ApplyDelayedBuildState(
            queueLimitControl,
            UiStrings.Get("QueueLimitLabel"),
            snapshot.QueueLimit1000);
    }

    private static void ApplyDelayedBuildState(
        CheckBox control,
        string label,
        DelayedFeatureStatus status)
    {
        SetEnabledIfChanged(control, true);
        SetCheckedIfChanged(control, status.Desired);
        SetTextIfChanged(control, $"{label} [{status.State}]");
        SetForeColorIfChanged(
            control,
            status.State switch
            {
                FeatureState.Pending => Color.DarkOrange,
                FeatureState.Active => Color.DarkGreen,
                FeatureState.Failed => Color.DarkRed,
                _ => SystemColors.ControlText,
            });
    }

    private void RefreshAutoApplyStates(AutoApplySnapshot snapshot)
    {
        ApplyAutoApplyState(
            moneyAutoControl,
            UiStrings.Get("ActionAutoMoney"),
            snapshot.Money);
    }

    private static void ApplyAutoApplyState(
        CheckBox control,
        string label,
        AutoApplyStatus status)
    {
        SetEnabledIfChanged(control, true);
        SetCheckedIfChanged(control, status.Desired);
        SetTextIfChanged(control, $"{label} [{status.State}]");
        SetForeColorIfChanged(
            control,
            status.State switch
            {
                FeatureState.Pending => Color.DarkOrange,
                FeatureState.Active => Color.DarkGreen,
                FeatureState.Failed => Color.DarkRed,
                _ => SystemColors.ControlText,
            });
    }

    private void RefreshTechUnlockState(
        PatchBinding binding,
        GameSessionSnapshot session)
    {
        TechUnlockMode desiredMode = patchPolicies.DesiredTechUnlockMode;
        bool desired = patchPolicies.IsDesired(binding.Id);
        if (!desired)
        {
            SetCheckStateIfChanged(binding.Control, CheckState.Unchecked);
            SetEnabledIfChanged(binding.Control, true);
            SetEnabledIfChanged(techUnlockModeInput, true);
            SetForeColorIfChanged(binding.Control, SystemColors.ControlText);
            return;
        }

        if (!patchPolicyStatuses.TryGetValue(
                binding.Id,
                out PatchPolicyStatus? status))
        {
            return;
        }

        if (status.State is FeatureState.Pending or FeatureState.Failed)
        {
            SetCheckStateIfChanged(binding.Control, CheckState.Checked);
            SetEnabledIfChanged(binding.Control, true);
            SetEnabledIfChanged(techUnlockModeInput, status.State != FeatureState.Failed);
            SetForeColorIfChanged(
                binding.Control,
                status.State == FeatureState.Failed
                    ? Color.DarkRed
                    : Color.DarkOrange);
            return;
        }

        TechUnlockModeInspection inspection =
            actions.InspectTechUnlockMode(binding.Id, session);
        SetCheckStateIfChanged(
            binding.Control,
            CheckState.Checked);
        SetEnabledIfChanged(binding.Control, true);
        SetEnabledIfChanged(techUnlockModeInput, true);
        SetForeColorIfChanged(
            binding.Control,
            inspection.Mode == desiredMode
                ? Color.DarkGreen
                : Color.DarkOrange);

        if (inspection.Mode is TechUnlockMode.Faction or TechUnlockMode.Extended)
        {
            for (int index = 0; index < techUnlockModeInput.Items.Count; index++)
            {
                if (techUnlockModeInput.Items[index] is TechUnlockModeOption option &&
                    option.Mode == inspection.Mode)
                {
                    if (techUnlockModeInput.SelectedIndex != index)
                    {
                        techUnlockModeInput.SelectedIndex = index;
                    }
                    break;
                }
            }
        }
    }

    private void SetActionControlsEnabled(bool enabled)
    {
        // Configuration inputs stay editable before a match so the trainer can
        // be set up first; only the one-shot action buttons wait for a match.
        SetEnabledIfChanged(globalStateGroup, true);
        SetEnabledIfChanged(techUnlockModeInput, true);
        SetEnabledIfChanged(oneTimeGroup, true);
        SetEnabledIfChanged(autoEditModeInput, true);
        SetEnabledIfChanged(editVeterancyControl, true);
        SetEnabledIfChanged(editVeterancyInput, true);
        SetEnabledIfChanged(editHealthControl, true);
        SetEnabledIfChanged(editHealthInput, true);
        SetEnabledIfChanged(moneyInput, true);
        SetEnabledIfChanged(moneyAutoControl, true);
        SetEnabledIfChanged(
            applyUnitEditButton,
            enabled &&
            (editVeterancyControl.Checked || editHealthControl.Checked));
        SetEnabledIfChanged(autoEditControl, true);
        SetEnabledIfChanged(moneyButton, enabled);
        SetEnabledIfChanged(addHealthButton, enabled);
        SetEnabledIfChanged(oneHealthButton, enabled);
        SetEnabledIfChanged(transferButton, enabled);
        SetEnabledIfChanged(zeroEconomyButton, enabled);
        SetEnabledIfChanged(defeatOwnerButton, enabled);
        SetEnabledIfChanged(grantSelectedSuperweaponsButton, enabled);
    }

    private void RefreshUnitEditorInputsAndAutoEdit()
    {
        SetEnabledIfChanged(editVeterancyInput, true);
        SetEnabledIfChanged(editHealthInput, true);
        SetEnabledIfChanged(
            applyUnitEditButton,
            matchControlsAvailable &&
            (editVeterancyControl.Checked || editHealthControl.Checked));
        RefreshAutoApplyRequest();
    }

    private static void SetEnabledIfChanged(Control control, bool enabled)
    {
        if (control.Enabled != enabled)
        {
            control.Enabled = enabled;
        }
    }

    private static void SetTextIfChanged(Control control, string text)
    {
        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
        {
            control.Text = text;
        }
    }

    private static void SetForeColorIfChanged(Control control, Color color)
    {
        if (control.ForeColor != color)
        {
            control.ForeColor = color;
        }
    }

    private static void SetCheckStateIfChanged(
        CheckBox control,
        CheckState checkState)
    {
        if (control.CheckState != checkState)
        {
            control.CheckState = checkState;
        }
    }

    private static void SetCheckedIfChanged(CheckBox control, bool value)
    {
        if (control.Checked != value)
        {
            control.Checked = value;
        }
    }

    private bool CancelCloseForRestoreFailure(
        FormClosingEventArgs e,
        string message)
    {
        languageRefreshRequested = false;
        ShowNotification(UiStrings.Format(
            "RestoreBeforeExitFailed",
            message), Color.DarkRed);
        DialogResult choice = MessageBox.Show(
            this,
            UiStrings.Format("RestoreBeforeExitFailed", message) +
                Environment.NewLine + Environment.NewLine +
                UiStrings.Get("ExitWithoutRestorePrompt"),
            UiStrings.Get("AppTitle"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        e.Cancel = choice != DialogResult.Yes;
        if (e.Cancel)
        {
            refreshTimer.Start();
        }
        return e.Cancel;
    }

    private sealed record PatchBinding(
        string Id,
        string DisplayName,
        CheckBox Control);

    private sealed record HotkeyDisplayBinding(
        string ActionId,
        string BaseText,
        string? Description);

    private sealed record AutoEditModeOption(
        AutoEditMode Mode,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record TechUnlockModeOption(
        TechUnlockMode Mode,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record VeterancyOption(
        UnitVeterancyLevel Level,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record DisplayLanguageOption(
        DisplayLanguage Language,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

}
