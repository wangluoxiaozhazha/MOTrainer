namespace MOTrainer.WinForms;

internal sealed class HotkeySettingsForm : Form
{
    private readonly Dictionary<string, TextBox> inputs = [];
    private readonly Label statusLabel;
    private HotkeyBindings draft;

    public HotkeySettingsForm(HotkeyBindings current)
    {
        draft = current.Clone();

        Text = "热键设置";
        ClientSize = new Size(600, 594);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        Label instruction = new()
        {
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(568, 42),
            Text =
                "点击右侧录入框后直接按组合键。至少包含 Ctrl、Alt 或 Shift；" +
                "修改只在点击“保存”后生效。",
        };
        Controls.Add(instruction);

        TableLayoutPanel rows = new()
        {
            AutoScroll = true,
            ColumnCount = 2,
            Location = new Point(16, 62),
            RowCount = HotkeyDefinitions.All.Count,
            Size = new Size(568, 446),
        };
        rows.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 52));
        rows.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 48));

        for (int index = 0;
            index < HotkeyDefinitions.All.Count;
            index++)
        {
            HotkeyDefinition definition =
                HotkeyDefinitions.All[index];
            rows.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 30));

            Label label = new()
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Margin = new Padding(4, 5, 4, 4),
                Text = definition.DisplayName,
            };
            TextBox input = new()
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                BackColor = SystemColors.Window,
                Margin = new Padding(4, 3, 8, 3),
                ReadOnly = true,
                ShortcutsEnabled = false,
                TabStop = true,
                Text = draft[definition.ActionId].DisplayText,
                TextAlign = HorizontalAlignment.Center,
            };
            input.Enter += (_, _) =>
                input.BackColor = Color.LightYellow;
            input.Leave += (_, _) =>
                input.BackColor = SystemColors.Window;
            input.PreviewKeyDown += (_, e) =>
                e.IsInputKey = true;
            input.KeyDown += (_, e) =>
                CaptureGesture(definition, input, e);

            inputs.Add(definition.ActionId, input);
            rows.Controls.Add(label, 0, index);
            rows.Controls.Add(input, 1, index);
        }

        Controls.Add(rows);

        statusLabel = new Label
        {
            AutoEllipsis = true,
            Location = new Point(16, 516),
            Size = new Size(340, 62),
            Text = "当前修改尚未保存。",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(statusLabel);

        Button resetButton = new()
        {
            Location = new Point(364, 532),
            Size = new Size(100, 32),
            Text = "恢复默认",
        };
        resetButton.Click += (_, _) => ResetDefaults();
        Controls.Add(resetButton);

        Button cancelButton = new()
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(472, 532),
            Size = new Size(52, 32),
            Text = "取消",
        };
        Controls.Add(cancelButton);

        Button saveButton = new()
        {
            DialogResult = DialogResult.OK,
            Location = new Point(532, 532),
            Size = new Size(52, 32),
            Text = "保存",
        };
        saveButton.Click += (_, _) =>
            ResultBindings = draft.Clone();
        Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    public HotkeyBindings? ResultBindings { get; private set; }

    private void CaptureGesture(
        HotkeyDefinition definition,
        TextBox input,
        KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys key = e.KeyCode & Keys.KeyCode;
        if (IsModifierKey(key))
        {
            SetStatus(
                "请在按住修饰键的同时再按一个主键。",
                Color.DarkOrange);
            return;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (e.Control)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (e.Alt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (e.Shift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            SetStatus(
                "热键至少需要 Ctrl、Alt 或 Shift 中的一个修饰键。",
                Color.DarkRed);
            return;
        }

        HotkeyGesture gesture = new(key, modifiers);
        if (IsReservedSystemGesture(gesture))
        {
            SetStatus(
                $"{gesture.DisplayText} 是系统保留组合，请使用其他热键。",
                Color.DarkRed);
            return;
        }

        if (!draft.TrySet(
                definition.ActionId,
                gesture,
                out string? conflictActionId))
        {
            string conflictName = conflictActionId is null
                ? "未知功能"
                : HotkeyDefinitions.DisplayName(conflictActionId);
            SetStatus(
                $"{gesture.DisplayText} 已用于“{conflictName}”。",
                Color.DarkRed);
            return;
        }

        input.Text = gesture.DisplayText;
        SetStatus(
            $"“{definition.DisplayName}”已改为 {gesture.DisplayText}，等待保存。",
            Color.DarkGreen);
    }

    private void ResetDefaults()
    {
        draft = HotkeyBindings.CreateDefaults();
        foreach (HotkeyDefinition definition in
            HotkeyDefinitions.All)
        {
            inputs[definition.ActionId].Text =
                draft[definition.ActionId].DisplayText;
        }

        SetStatus(
            "已在当前窗口恢复默认值；点击“保存”后生效。",
            Color.DarkOrange);
    }

    private void SetStatus(string message, Color color)
    {
        statusLabel.Text = message;
        statusLabel.ForeColor = color;
    }

    private static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
            Keys.LWin or Keys.RWin;

    private static bool IsReservedSystemGesture(
        HotkeyGesture gesture) =>
        gesture.Modifiers.HasFlag(HotkeyModifiers.Alt) &&
            gesture.Key is Keys.Tab or Keys.F4 ||
        gesture.Modifiers.HasFlag(HotkeyModifiers.Control) &&
            gesture.Key == Keys.Escape ||
        gesture.Modifiers.HasFlag(
            HotkeyModifiers.Control | HotkeyModifiers.Alt) &&
            gesture.Key == Keys.Delete;
}
