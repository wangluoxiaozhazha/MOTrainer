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
    private readonly Label resultLabel;
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
    private readonly Dictionary<uint, CheckBox> superweaponControls = [];
    private readonly Dictionary<Control, HotkeyDisplayBinding>
        hotkeyDisplayBindings = [];
    private GlobalHotkeyManager? hotkeys;
    private bool synchronizingPatchState;
    private bool autoEditTickActive;
    private bool autoEditEnabled;
    private bool matchControlsAvailable;
    private bool patchControlsAvailable;
    private AutoEditMode? activeAutoEditMode;
    private SelectedUnitEditRequest? autoEditRequest;

    public MainForm(IGameSessionProbe sessionProbe)
    {
        this.sessionProbe = sessionProbe;
        actions = new GameActionService(sessionProbe);
        buildPolicies = new DelayedBuildPolicyService(sessionProbe);
        settingsStore = new TrainerSettingsStore();
        TrainerSettingsLoadResult settingsLoad = settingsStore.Load();
        initialSettings = settingsLoad.Settings;
        settingsLoadWarning = settingsLoad.Warning;
        hotkeyBindings = initialSettings.Hotkeys;

        Text = UiStrings.Get("AppTitle");
        ClientSize = new Size(880, 598);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        hotkeyTips = new ToolTip();

        statusLabel = new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(16, 16),
            Size = new Size(552, 46),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        languageInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(584, 27),
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
            Location = new Point(744, 16),
            Size = new Size(120, 46),
            Text = UiStrings.Get("HotkeySettings"),
        };
        hotkeySettingsButton.Click += (_, _) =>
            OpenHotkeySettings();

        unitGroup = new GroupBox
        {
            Location = new Point(16, 76),
            Size = new Size(848, 106),
            Text = "选中单位修改",
        };

        unitGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 31),
            Text = "等级",
        });
        editVeterancyControl = new CheckBox
        {
            AutoSize = true,
            Checked = initialSettings.ModifyVeterancy,
            Location = new Point(218, 30),
            Text = "修改",
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
                "新兵"));
        editVeterancyInput.Items.Add(
            new VeterancyOption(
                UnitVeterancyLevel.Veteran,
                "老兵"));
        editVeterancyInput.Items.Add(
            new VeterancyOption(
                UnitVeterancyLevel.Elite,
                "精英 / 三星"));
        editVeterancyInput.SelectedIndex =
            (int)initialSettings.Veterancy;

        editHealthControl = new CheckBox
        {
            AutoSize = true,
            Checked = initialSettings.ModifyHealth,
            Location = new Point(218, 66),
            Text = "修改",
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
            Text = "修改选中",
        };
        applyUnitEditButton.Click += (_, _) =>
            RunAction(() =>
                BuildSelectedUnitEditRequest() is { } request
                    ? actions.EditSelectedUnits(request)
                    : ActionResult.Fail("请至少勾选修改等级或修改生命"));
        editVeterancyControl.CheckedChanged += (_, _) =>
            RefreshUnitEditorInputsAndAutoEdit();
        editHealthControl.CheckedChanged += (_, _) =>
            RefreshUnitEditorInputsAndAutoEdit();
        editVeterancyInput.SelectedIndexChanged += (_, _) =>
            RefreshAutoEditRequest();
        editHealthInput.ValueChanged += (_, _) =>
            RefreshAutoEditRequest();

        transferButton = new Button
        {
            Location = new Point(608, 27),
            Size = new Size(214, 29),
            Text = "占有单位",
        };
        transferButton.Click += (_, _) => RunAction(
            actions.TransferSelectedUnitsToCurrentPlayer);
        SetHotkeyToolTip(
            transferButton,
            HotkeyActionIds.TransferOwner);

        addHealthButton = new Button
        {
            Location = new Point(608, 63),
            Size = new Size(106, 29),
            Text = "加血",
        };
        addHealthButton.Click += (_, _) => RunAction(() =>
            actions.AddHealthToSelectedUnits(2000));
        SetHotkeyToolTip(
            addHealthButton,
            HotkeyActionIds.AddHealth);

        oneHealthButton = new Button
        {
            Location = new Point(716, 63),
            Size = new Size(106, 29),
            Text = "扣血",
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
            Text = "生命",
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
            Location = new Point(590, 24),
            Size = new Size(2, 68),
        });
        globalStateGroup = new GroupBox
        {
            Location = new Point(16, 196),
            Size = new Size(430, 326),
            Text = "全局状态",
        };
        globalStateGroup.Controls.Add(
            CreateSectionLabel("资源与生产", new Point(18, 24)));
        globalStateGroup.Controls.Add(
            CreateSeparator(new Point(18, 160), 394));
        globalStateGroup.Controls.Add(
            CreateSectionLabel("战场信息与控制", new Point(18, 170)));
        globalStateGroup.Controls.Add(
            CreateSeparator(new Point(18, 250), 394));
        globalStateGroup.Controls.Add(
            CreateSectionLabel("科技", new Point(18, 260)));

        AddPatchControl(
            KnownPatchIds.EconomyNoDecrease,
            "锁定经济",
            HotkeyActionIds.Patch(KnownPatchIds.EconomyNoDecrease),
            new Point(18, 48));
        AddPatchControl(
            KnownPatchIds.PowerNoDecrease,
            "锁定电量",
            HotkeyActionIds.Patch(KnownPatchIds.PowerNoDecrease),
            new Point(218, 48));
        AddPatchControl(
            KnownPatchIds.InstantUnitBuild,
            "快速生产",
            HotkeyActionIds.Patch(KnownPatchIds.InstantUnitBuild),
            new Point(18, 76));
        AddPatchControl(
            KnownPatchIds.IgnoreBuildDistance,
            "随处建造",
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreBuildDistance),
            new Point(218, 76));
        AddPatchControl(
            KnownPatchIds.AutoRepair,
            "自动维修",
            HotkeyActionIds.Patch(KnownPatchIds.AutoRepair),
            new Point(18, 104));

        unitUnlimitedControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(218, 104),
            Text = "解锁建造数量 [Off]",
        };
        unitUnlimitedControl.Click += (_, _) =>
            ToggleDelayedBuildFeature(
                DelayedBuildFeature.UnitUnlimited);
        queueLimitControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(18, 132),
            Text = "解锁队列上限 [Off]",
        };
        queueLimitControl.Click += (_, _) =>
            ToggleDelayedBuildFeature(
                DelayedBuildFeature.QueueLimit1000);
        globalStateGroup.Controls.Add(unitUnlimitedControl);
        globalStateGroup.Controls.Add(queueLimitControl);

        AddPatchControl(
            KnownPatchIds.ControlAllUnits,
            "操纵全部",
            HotkeyActionIds.Patch(KnownPatchIds.ControlAllUnits),
            new Point(18, 194));
        AddPatchControl(
            KnownPatchIds.PsychicDetection,
            "心灵探测",
            HotkeyActionIds.Patch(KnownPatchIds.PsychicDetection),
            new Point(218, 194));
        AddPatchControl(
            KnownPatchIds.IgnoreCloak,
            "无视隐形",
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreCloak),
            new Point(18, 222));
        AddPatchControl(
            KnownPatchIds.StableRevealMap,
            "无视黑雾",
            HotkeyActionIds.Patch(KnownPatchIds.StableRevealMap),
            new Point(218, 222));
        AddPatchControl(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
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
                "阵营模式（推荐）"));
        techUnlockModeInput.Items.Add(
            new TechUnlockModeOption(
                TechUnlockMode.Extended,
                "扩展模式"));
        techUnlockModeInput.SelectedIndex =
            initialSettings.TechUnlockMode == TechUnlockMode.Extended
                ? 1
                : 0;
        techUnlockModeInput.SelectedIndexChanged += (_, _) =>
            ChangeTechUnlockModeIfEnabled();
        globalStateGroup.Controls.Add(techUnlockModeInput);

        autoEditModeInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(430, 63),
            Size = new Size(150, 24),
        };
        autoEditModeInput.Items.Add(
            new AutoEditModeOption(
                AutoEditMode.Event,
                "事件模式（推荐）"));
        autoEditModeInput.Items.Add(
            new AutoEditModeOption(
                AutoEditMode.Polling,
                "轮询模式（兼容）"));
        autoEditModeInput.SelectedIndex =
            initialSettings.AutoEditMode == AutoEditMode.Polling
                ? 1
                : 0;
        autoEditModeInput.SelectedIndexChanged += (_, _) =>
            ChangeAutoEditModeIfEnabled();
        unitGroup.Controls.Add(autoEditModeInput);

        autoEditControl = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(318, 67),
            Text = "自动修改",
        };
        autoEditControl.Click += (_, _) =>
            RunAction(ToggleAutoEdit);
        SetHotkeyToolTip(
            autoEditControl,
            HotkeyActionIds.AutoEdit);
        unitGroup.Controls.Add(autoEditControl);

        oneTimeGroup = new GroupBox
        {
            Enabled = false,
            Location = new Point(460, 196),
            Size = new Size(404, 326),
            Text = "一次性功能",
        };
        oneTimeGroup.Controls.Add(
            CreateSectionLabel("资源与阵营", new Point(18, 24)));
        oneTimeGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 53),
            Text = "经济",
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
            Text = "修改",
        };
        moneyButton.Click += (_, _) => RunAction(() =>
            actions.SetMoney(decimal.ToUInt32(moneyInput.Value)));
        zeroEconomyButton = new Button
        {
            Location = new Point(18, 84),
            Size = new Size(176, 29),
            Text = "目标阵营经济归零",
        };
        zeroEconomyButton.Click += (_, _) => RunAction(
            actions.ZeroFirstSelectedOwnerEconomy);
        SetHotkeyToolTip(
            zeroEconomyButton,
            HotkeyActionIds.ZeroEconomy);
        defeatOwnerButton = new Button
        {
            Location = new Point(210, 84),
            Size = new Size(176, 29),
            Text = "目标阵营投降",
        };
        defeatOwnerButton.Click += (_, _) => RunAction(
            actions.DefeatFirstSelectedOwner);
        SetHotkeyToolTip(
            defeatOwnerButton,
            HotkeyActionIds.DefeatOwner);
        oneTimeGroup.Controls.Add(moneyInput);
        oneTimeGroup.Controls.Add(moneyButton);
        oneTimeGroup.Controls.Add(zeroEconomyButton);
        oneTimeGroup.Controls.Add(defeatOwnerButton);
        oneTimeGroup.Controls.Add(
            CreateSeparator(new Point(18, 126), 368));
        oneTimeGroup.Controls.Add(
            CreateSectionLabel("超级武器", new Point(18, 136)));

        Point[] superweaponLocations =
        [
            new(18, 160), new(110, 160),
            new(202, 160), new(294, 160),
            new(18, 188), new(110, 188),
            new(202, 188), new(294, 188),
        ];
        for (int index = 0; index < SuperweaponChoices.All.Count; index++)
        {
            SuperweaponChoice choice = SuperweaponChoices.All[index];
            CheckBox control = new()
            {
                AutoSize = true,
                Checked = initialSettings.SelectedSuperweaponIds.Contains(choice.Id),
                Location = superweaponLocations[index],
                Text = choice.DisplayName,
            };
            superweaponControls.Add(choice.Id, control);
            oneTimeGroup.Controls.Add(control);
        }
        grantSelectedSuperweaponsButton = new Button
        {
            Location = new Point(18, 246),
            Size = new Size(368, 28),
            Text = "发放已勾选超武",
        };
        grantSelectedSuperweaponsButton.Click += (_, _) =>
            RunAction(GrantSelectedSuperweapons);
        SetHotkeyToolTip(
            grantSelectedSuperweaponsButton,
            HotkeyActionIds.GrantSelectedSuperweapons);
        oneTimeGroup.Controls.Add(grantSelectedSuperweaponsButton);
        superweaponHotkeyDescription = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(18, 282),
        };
        oneTimeGroup.Controls.Add(superweaponHotkeyDescription);

        resultLabel = new Label
        {
            AutoEllipsis = true,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(16, 536),
            Size = new Size(848, 46),
            Text = UiStrings.Get("CompatibilityNotice"),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(statusLabel);
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

        RefreshSession();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ReloadHotkeys();
    }

    internal HotkeyBindings ConfiguredHotkeys => hotkeyBindings;

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
            resultLabel.Text = $"以下热键注册失败，可能被其他程序占用：{string.Join("、", failures)}";
            resultLabel.ForeColor = Color.DarkRed;
        }
        else if (warning is not null)
        {
            resultLabel.Text = warning;
            resultLabel.ForeColor = Color.DarkOrange;
        }
        else
        {
            resultLabel.Text = "全局热键已注册；操作时游戏可以保持焦点。";
            resultLabel.ForeColor = Color.DarkGreen;
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
                ? "旧热键已恢复。"
                : $"旧热键恢复也失败：{string.Join("、", restoreFailures)}";
            resultLabel.Text =
                $"新热键被其他程序占用：{string.Join("、", registrationFailures)}；{restoreSuffix}";
            resultLabel.ForeColor = Color.DarkRed;
            return;
        }

        hotkeys = candidateManager;
        hotkeyBindings.ReplaceWith(candidate);
        RefreshHotkeyDisplays();
        string? saveError = settingsStore.Save(CaptureSettings());
        if (saveError is null)
        {
            resultLabel.Text =
                "热键已保存并立即生效。";
            resultLabel.ForeColor = Color.DarkGreen;
            return;
        }

        hotkeys.Dispose();
        hotkeyBindings.ReplaceWith(original);
        hotkeys = CreateHotkeyManager(
            original,
            out List<string> rollbackFailures);
        RefreshHotkeyDisplays();
        resultLabel.Text = rollbackFailures.Count == 0
            ? $"{saveError}；新热键未生效，旧热键已恢复。"
            : $"{saveError}；旧热键恢复失败：{string.Join("、", rollbackFailures)}";
        resultLabel.ForeColor = Color.DarkRed;
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
            StopAutoEdit();
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
        ActionResult autoEditRestore = StopAutoEdit();
        if (!autoEditRestore.Success)
        {
            CancelCloseForRestoreFailure(
                e,
                autoEditRestore.Message);
            return;
        }

        DelayedBuildPolicySnapshot delayedRestore =
            buildPolicies.RestoreOwned();
        if (delayedRestore.UnitUnlimited.State == FeatureState.Failed ||
            delayedRestore.QueueLimit1000.State == FeatureState.Failed)
        {
            string message =
                delayedRestore.UnitUnlimited.State == FeatureState.Failed
                    ? delayedRestore.UnitUnlimited.Message
                    : delayedRestore.QueueLimit1000.Message;
            CancelCloseForRestoreFailure(e, message);
            return;
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
                    CancelCloseForRestoreFailure(e, restore.Message);
                    return;
                }
            }
            else if (inspection.State == PatchRuntimeState.Unknown)
            {
                CancelCloseForRestoreFailure(
                    e,
                    $"{binding.DisplayName}补丁字节异常，已停止退出");
                return;
            }

            ownedPatchProcesses.Remove(patchId);
        }

        string? settingsError = settingsStore.Save(CaptureSettings());
        if (settingsError is not null)
        {
            MessageBox.Show(
                this,
                settingsError,
                "设置未保存",
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
        resultLabel.Text = error ?? UiStrings.Get("LanguageRestartRequired");
        resultLabel.ForeColor = error is null
            ? Color.DarkOrange
            : Color.DarkRed;
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
            ? ActionResult.Fail("请至少勾选一个超级武器。")
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
        string actionId)
    {
        hotkeyDisplayBindings[control] = new(
            actionId,
            control.Text);
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
                $"{gesture.DisplayText} 与按钮一次批量发放全部已勾选项";
        }
        else
        {
            superweaponHotkeyDescription.Text =
                "按钮一次批量发放全部已勾选项";
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
                $"全局热键 {gesture.DisplayText}");
        }
        else
        {
            control.Text = binding.BaseText;
            hotkeyTips.SetToolTip(control, string.Empty);
        }
    }

    private void RefreshSession()
    {
        GameSessionSnapshot snapshot = sessionProbe.Probe();
        statusLabel.Text = snapshot.Message;
        statusLabel.ForeColor = snapshot.State switch
        {
            SessionState.NoProcess => Color.DimGray,
            SessionState.ProcessFound or SessionState.ModulesReady => Color.DarkOrange,
            SessionState.Unsupported or SessionState.Error => Color.DarkRed,
            _ => Color.DarkGreen,
        };

        matchControlsAvailable =
            snapshot.State == SessionState.MatchReady;
        patchControlsAvailable =
            snapshot.State is SessionState.ModulesReady or SessionState.MatchReady;
        SetActionControlsEnabled(matchControlsAvailable);
        if (!matchControlsAvailable && autoEditEnabled)
        {
            StopAutoEdit();
        }

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
    }

    private void RunAction(Func<ActionResult> action)
    {
        SetInteractiveControlsEnabled(false);

        try
        {
            ActionResult result = action();
            resultLabel.Text = result.Message;
            resultLabel.ForeColor = result.Success ? Color.DarkGreen : Color.DarkRed;
        }
        finally
        {
            RefreshSession();
        }
    }

    private void TogglePatch(PatchBinding binding)
    {
        if (synchronizingPatchState)
        {
            return;
        }

        if (binding.Id == KnownPatchIds.SafeTechUnlock)
        {
            ToggleTechUnlock(binding);
            return;
        }

        PatchInspection before = actions.InspectPatch(binding.Id);
        if (before.State is not (PatchRuntimeState.Original or PatchRuntimeState.Enabled))
        {
            resultLabel.Text = before.Message;
            resultLabel.ForeColor = Color.DarkRed;
            RefreshPatchStates();
            return;
        }

        bool enable = before.State == PatchRuntimeState.Original;
        ActionResult result =
            actions.SetPatchEnabled(binding.Id, binding.DisplayName, enable);

        resultLabel.Text = result.Message;
        resultLabel.ForeColor = result.Success ? Color.DarkGreen : Color.DarkRed;

        if (result.Success)
        {
            GameSessionSnapshot snapshot = sessionProbe.Probe();
            if (enable && snapshot.ProcessId is int processId)
            {
                ownedPatchProcesses[binding.Id] = processId;
            }
            else if (!enable)
            {
                ownedPatchProcesses.Remove(binding.Id);
            }
        }

        RefreshPatchStates();
    }

    private void ToggleTechUnlock(PatchBinding binding)
    {
        TechUnlockModeInspection before =
            actions.InspectTechUnlockMode(binding.Id);
        if (before.Mode is null)
        {
            resultLabel.Text = before.Message;
            resultLabel.ForeColor = Color.DarkRed;
            RefreshPatchStates();
            return;
        }

        TechUnlockMode target = before.Mode == TechUnlockMode.Off
            ? SelectedTechUnlockMode
            : TechUnlockMode.Off;
        ActionResult result = actions.SetTechUnlockMode(
            binding.Id,
            binding.DisplayName,
            target);

        resultLabel.Text = result.Message;
        resultLabel.ForeColor =
            result.Success ? Color.DarkGreen : Color.DarkRed;
        if (result.Success)
        {
            GameSessionSnapshot snapshot = sessionProbe.Probe();
            if (target != TechUnlockMode.Off &&
                snapshot.ProcessId is int processId)
            {
                ownedPatchProcesses[binding.Id] = processId;
            }
            else
            {
                ownedPatchProcesses.Remove(binding.Id);
            }
        }

        RefreshPatchStates();
    }

    private void ChangeTechUnlockModeIfEnabled()
    {
        if (synchronizingPatchState)
        {
            return;
        }

        TechUnlockModeInspection before =
            actions.InspectTechUnlockMode(KnownPatchIds.SafeTechUnlock);
        if (before.Mode is not (TechUnlockMode.Faction or TechUnlockMode.Extended) ||
            before.Mode == SelectedTechUnlockMode)
        {
            return;
        }

        ActionResult result = actions.SetTechUnlockMode(
            KnownPatchIds.SafeTechUnlock,
            "科技全开",
            SelectedTechUnlockMode);
        resultLabel.Text = result.Message;
        resultLabel.ForeColor =
            result.Success ? Color.DarkGreen : Color.DarkRed;
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
            return StopAutoEdit();
        }

        return StartAutoEdit(SelectedAutoEditMode);
    }

    private ActionResult StartAutoEdit(AutoEditMode mode)
    {
        if (!matchControlsAvailable)
        {
            return ActionResult.Fail("尚未进入可用战局，无法启动自动修改");
        }

        SelectedUnitEditRequest? request =
            BuildSelectedUnitEditRequest();
        if (request is null)
        {
            return ActionResult.Fail("请至少勾选修改等级或修改生命");
        }

        if (mode == AutoEditMode.Event)
        {
            ActionResult enable =
                actions.SetSelectionEditEventPolicy(request);
            if (!enable.Success)
            {
                return enable;
            }

            GameSessionSnapshot snapshot = sessionProbe.Probe();
            if (snapshot.ProcessId is int processId)
            {
                ownedPatchProcesses[
                    KnownPatchIds.AutoPromoteSelection] = processId;
            }
        }
        else
        {
            PatchInspection eventInspection =
                actions.InspectPatch(
                    KnownPatchIds.AutoPromoteSelection);
            if (eventInspection.State == PatchRuntimeState.Enabled)
            {
                ActionResult disable =
                    actions.SetSelectionEditEventPolicy(null);
                if (!disable.Success)
                {
                    return disable;
                }

                ownedPatchProcesses.Remove(
                    KnownPatchIds.AutoPromoteSelection);
            }

            lock (autoEditGate)
            {
                autoEditRequest = request;
                autoEditTimer.Change(0, 100);
            }
        }

        autoEditRequest = request;
        activeAutoEditMode = mode;
        autoEditEnabled = true;
        autoEditControl.Checked = true;
        return new ActionResult(
            true,
            mode == AutoEditMode.Event
                ? "自动修改事件模式已启用；选择形成时应用当前设置"
                : "自动修改轮询模式已启用；每 100ms 持续应用当前设置");
    }

    private ActionResult StopAutoEdit()
    {
        AutoEditMode? mode = activeAutoEditMode;
        lock (autoEditGate)
        {
            autoEditTimer.Change(
                Timeout.Infinite,
                Timeout.Infinite);
        }

        autoEditIdle.Wait();
        if (mode == AutoEditMode.Event)
        {
            ActionResult disable =
                actions.SetSelectionEditEventPolicy(null);
            if (!disable.Success)
            {
                GameSessionSnapshot snapshot =
                    sessionProbe.Probe();
                if (snapshot.ProcessId is int processId &&
                    ownedPatchProcesses.TryGetValue(
                        KnownPatchIds.AutoPromoteSelection,
                        out int ownedProcessId) &&
                    processId == ownedProcessId)
                {
                    return disable;
                }
            }

            ownedPatchProcesses.Remove(
                KnownPatchIds.AutoPromoteSelection);
        }

        lock (autoEditGate)
        {
            autoEditEnabled = false;
            autoEditRequest = null;
            activeAutoEditMode = null;
        }

        autoEditControl.Checked = false;
        return new ActionResult(true, "选中单位自动修改已关闭");
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

    private void RefreshAutoEditRequest()
    {
        if (!autoEditEnabled)
        {
            return;
        }

        SelectedUnitEditRequest? request =
            BuildSelectedUnitEditRequest();
        if (request is null)
        {
            ActionResult stopped = StopAutoEdit();
            resultLabel.Text = stopped.Success
                ? "等级和生命均未勾选，自动修改已关闭"
                : stopped.Message;
            resultLabel.ForeColor = stopped.Success
                ? Color.DarkOrange
                : Color.DarkRed;
            return;
        }

        if (activeAutoEditMode == AutoEditMode.Event)
        {
            ActionResult updated =
                actions.SetSelectionEditEventPolicy(request);
            resultLabel.Text = updated.Success
                ? "自动修改事件策略已更新"
                : updated.Message;
            resultLabel.ForeColor = updated.Success
                ? Color.DarkGreen
                : Color.DarkRed;
            if (!updated.Success)
            {
                return;
            }
        }

        lock (autoEditGate)
        {
            if (autoEditEnabled)
            {
                autoEditRequest = request;
            }
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
            ActionResult stopped = StopAutoEdit();
            return stopped.Success
                ? StartAutoEdit(SelectedAutoEditMode)
                : stopped;
        });
    }

    private AutoEditMode SelectedAutoEditMode =>
        autoEditModeInput.SelectedItem is AutoEditModeOption option
            ? option.Mode
            : AutoEditMode.Event;

    private void RefreshPatchStates(
        GameSessionSnapshot? session = null)
    {
        GameSessionSnapshot snapshot = session ?? sessionProbe.Probe();
        synchronizingPatchState = true;
        try
        {
            foreach (PatchBinding binding in patchBindings.Values)
            {
                if (binding.Id == KnownPatchIds.SafeTechUnlock)
                {
                    RefreshTechUnlockState(binding, snapshot);
                    continue;
                }

                PatchInspection inspection =
                    actions.InspectPatch(binding.Id, snapshot);
                binding.Control.CheckState = inspection.State switch
                {
                    PatchRuntimeState.Original => CheckState.Unchecked,
                    PatchRuntimeState.Enabled => CheckState.Checked,
                    _ => CheckState.Indeterminate,
                };
                binding.Control.Enabled =
                    patchControlsAvailable &&
                    inspection.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
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
        bool desired = feature == DelayedBuildFeature.UnitUnlimited
            ? !before.UnitUnlimited.Desired
            : !before.QueueLimit1000.Desired;
        DelayedBuildPolicySnapshot after =
            buildPolicies.SetDesired(feature, desired);
        DelayedFeatureStatus status =
            feature == DelayedBuildFeature.UnitUnlimited
                ? after.UnitUnlimited
                : after.QueueLimit1000;

        resultLabel.Text = status.Message;
        resultLabel.ForeColor = status.State == FeatureState.Failed
            ? Color.DarkRed
            : status.State == FeatureState.Pending
                ? Color.DarkOrange
                : Color.DarkGreen;
        ApplyDelayedBuildStates(after);
    }

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
        ApplyDelayedBuildState(
            unitUnlimitedControl,
            "解锁建造数量",
            snapshot.UnitUnlimited);
        ApplyDelayedBuildState(
            queueLimitControl,
            "解锁队列上限",
            snapshot.QueueLimit1000);
    }

    private static void ApplyDelayedBuildState(
        CheckBox control,
        string label,
        DelayedFeatureStatus status)
    {
        control.Enabled = true;
        control.Checked = status.Desired;
        control.Text = $"{label} [{status.State}]";
        control.ForeColor = status.State switch
        {
            FeatureState.Pending => Color.DarkOrange,
            FeatureState.Active => Color.DarkGreen,
            FeatureState.Failed => Color.DarkRed,
            _ => SystemColors.ControlText,
        };
    }

    private void RefreshTechUnlockState(
        PatchBinding binding,
        GameSessionSnapshot session)
    {
        TechUnlockModeInspection inspection =
            actions.InspectTechUnlockMode(binding.Id, session);
        binding.Control.CheckState = inspection.State switch
        {
            PatchRuntimeState.Original => CheckState.Unchecked,
            PatchRuntimeState.Enabled => CheckState.Checked,
            _ => CheckState.Indeterminate,
        };
        binding.Control.Enabled =
            patchControlsAvailable &&
            inspection.State is PatchRuntimeState.Original or PatchRuntimeState.Enabled;
        techUnlockModeInput.Enabled = binding.Control.Enabled;

        if (inspection.Mode is TechUnlockMode.Faction or TechUnlockMode.Extended)
        {
            for (int index = 0; index < techUnlockModeInput.Items.Count; index++)
            {
                if (techUnlockModeInput.Items[index] is TechUnlockModeOption option &&
                    option.Mode == inspection.Mode)
                {
                    techUnlockModeInput.SelectedIndex = index;
                    break;
                }
            }
        }
    }

    private void SetInteractiveControlsEnabled(bool enabled)
    {
        SetActionControlsEnabled(
            enabled && matchControlsAvailable);
        foreach (PatchBinding binding in patchBindings.Values)
        {
            binding.Control.Enabled =
                enabled && patchControlsAvailable;
        }
        techUnlockModeInput.Enabled =
            enabled && patchControlsAvailable;
        autoEditModeInput.Enabled =
            enabled && matchControlsAvailable;
        autoEditControl.Enabled =
            enabled && matchControlsAvailable;
        unitUnlimitedControl.Enabled = enabled;
        queueLimitControl.Enabled = enabled;
    }

    private void SetActionControlsEnabled(bool enabled)
    {
        oneTimeGroup.Enabled = enabled;
        autoEditModeInput.Enabled = enabled;
        editVeterancyControl.Enabled = enabled;
        editVeterancyInput.Enabled = enabled;
        editHealthControl.Enabled = enabled;
        editHealthInput.Enabled = enabled;
        applyUnitEditButton.Enabled =
            enabled &&
            (editVeterancyControl.Checked ||
                editHealthControl.Checked);
        autoEditControl.Enabled = enabled;
        moneyButton.Enabled = enabled;
        addHealthButton.Enabled = enabled;
        oneHealthButton.Enabled = enabled;
        transferButton.Enabled = enabled;
        zeroEconomyButton.Enabled = enabled;
        defeatOwnerButton.Enabled = enabled;
    }

    private void RefreshUnitEditorInputsAndAutoEdit()
    {
        editVeterancyInput.Enabled = matchControlsAvailable;
        editHealthInput.Enabled = matchControlsAvailable;
        applyUnitEditButton.Enabled =
            matchControlsAvailable &&
            (editVeterancyControl.Checked ||
                editHealthControl.Checked);
        RefreshAutoEditRequest();
    }

    private void CancelCloseForRestoreFailure(
        FormClosingEventArgs e,
        string message)
    {
        resultLabel.Text = $"退出前恢复失败：{message}";
        resultLabel.ForeColor = Color.DarkRed;
        MessageBox.Show(
            this,
            resultLabel.Text,
            "MO 离线修改器",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        e.Cancel = true;
        refreshTimer.Start();
    }

    private sealed record PatchBinding(
        string Id,
        string DisplayName,
        CheckBox Control);

    private sealed record HotkeyDisplayBinding(
        string ActionId,
        string BaseText);

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
