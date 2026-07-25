using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using MOTrainer.Core.Actions;
using MOTrainer.Core.Hooks;

namespace MOTrainer.WinForms;

internal enum AutoEditMode
{
    Event,
    Polling,
}

internal sealed record TrainerSettings(
    bool ModifyVeterancy,
    UnitVeterancyLevel Veterancy,
    bool ModifyHealth,
    int Health,
    AutoEditMode AutoEditMode,
    uint Money,
    TechUnlockMode TechUnlockMode,
    IReadOnlyList<uint> SelectedSuperweaponIds,
    HotkeyBindings Hotkeys)
{
    public static TrainerSettings CreateDefaults() =>
        new(
            ModifyVeterancy: true,
            Veterancy: UnitVeterancyLevel.Elite,
            ModifyHealth: false,
            Health: 10_000_000,
            AutoEditMode: AutoEditMode.Event,
            Money: 123_456,
            TechUnlockMode: TechUnlockMode.Faction,
            SelectedSuperweaponIds: Array.Empty<uint>(),
            Hotkeys: HotkeyBindings.CreateDefaults());
}

internal sealed record TrainerSettingsLoadResult(
    TrainerSettings Settings,
    string? Warning);

internal sealed class TrainerSettingsStore
{
    private const string SettingsVersionKey = "settings.version";
    private const string HotkeyPrefix = "hotkey.";
    private readonly string path;

    public TrainerSettingsStore(string? path = null)
    {
        this.path = path ??
            ((Assembly.GetEntryAssembly()?.Location ??
                Application.ExecutablePath) + ".config");
    }

    public string Path => path;

    public TrainerSettingsLoadResult Load()
    {
        TrainerSettings defaults = TrainerSettings.CreateDefaults();
        if (!File.Exists(path))
        {
            return new TrainerSettingsLoadResult(defaults, null);
        }

        Dictionary<string, string> values;
        try
        {
            XDocument document = XDocument.Load(path);
            values = ReadValues(document);
        }
        catch (Exception exception)
        {
            return new TrainerSettingsLoadResult(
                defaults,
                $"设置文件读取失败，已使用默认值：{exception.Message}");
        }

        List<string> warnings = [];
        bool modifyVeterancy = ReadBoolean(
            values,
            "unit.modifyVeterancy",
            defaults.ModifyVeterancy,
            warnings);
        UnitVeterancyLevel veterancy = ReadEnum(
            values,
            "unit.veterancy",
            defaults.Veterancy,
            warnings);
        bool modifyHealth = ReadBoolean(
            values,
            "unit.modifyHealth",
            defaults.ModifyHealth,
            warnings);
        int health = ReadInt32(
            values,
            "unit.health",
            defaults.Health,
            1,
            int.MaxValue,
            warnings);
        AutoEditMode autoEditMode = ReadEnum(
            values,
            "unit.autoEditMode",
            defaults.AutoEditMode,
            warnings);
        uint money = ReadUInt32(
            values,
            "faction.money",
            defaults.Money,
            warnings);
        TechUnlockMode techUnlockMode = ReadEnum(
            values,
            "techUnlock.mode",
            defaults.TechUnlockMode,
            warnings);
        IReadOnlyList<uint> selectedSuperweaponIds =
            ReadSuperweaponIds(values, warnings);

        HotkeyBindings hotkeys = defaults.Hotkeys;
        bool migrateLegacyAutoEdit =
            (!values.TryGetValue(
                SettingsVersionKey,
                out string? settingsVersion) ||
             settingsVersion != "2") &&
            values.TryGetValue(
                "hotkey.unit.promote-selected",
                out string? legacyPromote) &&
            string.Equals(
                legacyPromote,
                "Ctrl+A",
                StringComparison.OrdinalIgnoreCase) &&
            values.TryGetValue(
                HotkeyPrefix + HotkeyActionIds.AutoEdit,
                out string? legacyAutoEdit) &&
            string.Equals(
                legacyAutoEdit,
                "Alt+A",
                StringComparison.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, HotkeyGesture> binding in
            hotkeys.Entries)
        {
            string key = HotkeyPrefix + binding.Key;
            if (migrateLegacyAutoEdit &&
                binding.Key == HotkeyActionIds.AutoEdit)
            {
                continue;
            }

            if (!values.TryGetValue(key, out string? text))
            {
                continue;
            }

            if (!HotkeyGesture.TryParse(text, out HotkeyGesture gesture))
            {
                warnings.Add($"{key} 不是有效热键，已使用默认值");
                continue;
            }

            if (!hotkeys.TrySet(
                    binding.Key,
                    gesture,
                    out string? conflictActionId))
            {
                warnings.Add(
                    conflictActionId is null
                        ? $"{key} 对应未知功能，已忽略"
                        : $"{key} 与 {HotkeyPrefix}{conflictActionId} 冲突，已使用默认值");
            }
        }

        TrainerSettings settings = new(
            modifyVeterancy,
            veterancy,
            modifyHealth,
            health,
            autoEditMode,
            money,
            techUnlockMode,
            selectedSuperweaponIds,
            hotkeys);
        return new TrainerSettingsLoadResult(
            settings,
            warnings.Count == 0
                ? null
                : string.Join("；", warnings));
    }

    public string? Save(TrainerSettings settings)
    {
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".bak";
        try
        {
            XDocument document = File.Exists(path)
                ? XDocument.Load(path)
                : new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement("configuration"));
            XElement root = document.Root ??
                throw new InvalidOperationException(
                    "设置文件缺少 configuration 根节点");
            if (root.Name != "configuration")
            {
                throw new InvalidOperationException(
                    "设置文件根节点不是 configuration");
            }

            XElement appSettings =
                root.Element("appSettings") ??
                new XElement("appSettings");
            if (appSettings.Parent is null)
            {
                XElement? firstNonSection =
                    root.Elements().FirstOrDefault(
                        element => element.Name != "configSections");
                if (firstNonSection is null)
                {
                    root.Add(appSettings);
                }
                else
                {
                    firstNonSection.AddBeforeSelf(appSettings);
                }
            }

            SetValue(appSettings, SettingsVersionKey, "2");
            SetValue(
                appSettings,
                "unit.modifyVeterancy",
                settings.ModifyVeterancy.ToString());
            SetValue(
                appSettings,
                "unit.veterancy",
                settings.Veterancy.ToString());
            SetValue(
                appSettings,
                "unit.modifyHealth",
                settings.ModifyHealth.ToString());
            SetValue(
                appSettings,
                "unit.health",
                settings.Health.ToString(CultureInfo.InvariantCulture));
            SetValue(
                appSettings,
                "unit.autoEditMode",
                settings.AutoEditMode.ToString());
            SetValue(
                appSettings,
                "faction.money",
                settings.Money.ToString(CultureInfo.InvariantCulture));
            SetValue(
                appSettings,
                "techUnlock.mode",
                settings.TechUnlockMode.ToString());
            SetValue(
                appSettings,
                "superweapon.selectedIds",
                string.Join(
                    ",",
                    SuperweaponChoices.All
                        .Where(choice =>
                            settings.SelectedSuperweaponIds.Contains(
                                choice.Id))
                        .Select(choice =>
                            choice.Id.ToString(
                                CultureInfo.InvariantCulture))));

            HashSet<string> knownHotkeyKeys = new(
                settings.Hotkeys.Entries.Select(
                    binding => HotkeyPrefix + binding.Key),
                StringComparer.Ordinal);
            foreach (XElement obsolete in appSettings
                .Elements("add")
                .Where(element =>
                    ((string?)element.Attribute("key")) is string key &&
                    key.StartsWith(
                        HotkeyPrefix,
                        StringComparison.Ordinal) &&
                    !knownHotkeyKeys.Contains(key))
                .ToArray())
            {
                obsolete.Remove();
            }

            foreach (KeyValuePair<string, HotkeyGesture> binding in
                settings.Hotkeys.Entries)
            {
                SetValue(
                    appSettings,
                    HotkeyPrefix + binding.Key,
                    binding.Value.DisplayText);
            }

            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            document.Save(temporaryPath);
            if (File.Exists(path))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(
                    temporaryPath,
                    path,
                    backupPath,
                    ignoreMetadataErrors: true);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            return null;
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            return $"设置保存失败：{exception.Message}";
        }
    }

    private static Dictionary<string, string> ReadValues(
        XDocument document)
    {
        return document.Root?
            .Element("appSettings")?
            .Elements("add")
            .Where(element =>
                element.Attribute("key") is not null &&
                element.Attribute("value") is not null)
            .GroupBy(
                element => (string)element.Attribute("key")!,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (string)group.Last().Attribute("value")!,
                StringComparer.Ordinal) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void SetValue(
        XElement appSettings,
        string key,
        string value)
    {
        XElement[] existing = appSettings
            .Elements("add")
            .Where(element =>
                string.Equals(
                    (string?)element.Attribute("key"),
                    key,
                    StringComparison.Ordinal))
            .ToArray();
        XElement element = existing.FirstOrDefault() ??
            new XElement("add");
        element.SetAttributeValue("key", key);
        element.SetAttributeValue("value", value);
        if (element.Parent is null)
        {
            appSettings.Add(element);
        }

        foreach (XElement duplicate in existing.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback,
        ICollection<string> warnings)
    {
        if (!values.TryGetValue(key, out string? text))
        {
            return fallback;
        }
        if (bool.TryParse(text, out bool value))
        {
            return value;
        }

        warnings.Add($"{key} 不是有效布尔值，已使用默认值");
        return fallback;
    }

    private static T ReadEnum<T>(
        IReadOnlyDictionary<string, string> values,
        string key,
        T fallback,
        ICollection<string> warnings)
        where T : struct
    {
        if (!values.TryGetValue(key, out string? text))
        {
            return fallback;
        }
        if (Enum.TryParse(text, true, out T value) &&
            Enum.IsDefined(typeof(T), value))
        {
            return value;
        }

        warnings.Add($"{key} 不是有效选项，已使用默认值");
        return fallback;
    }

    private static int ReadInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int minimum,
        int maximum,
        ICollection<string> warnings)
    {
        if (!values.TryGetValue(key, out string? text))
        {
            return fallback;
        }
        if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value) &&
            value >= minimum &&
            value <= maximum)
        {
            return value;
        }

        warnings.Add($"{key} 超出有效范围，已使用默认值");
        return fallback;
    }

    private static uint ReadUInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        uint fallback,
        ICollection<string> warnings)
    {
        if (!values.TryGetValue(key, out string? text))
        {
            return fallback;
        }
        if (uint.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
        {
            return value;
        }

        warnings.Add($"{key} 不是有效无符号整数，已使用默认值");
        return fallback;
    }

    private static IReadOnlyList<uint> ReadSuperweaponIds(
        IReadOnlyDictionary<string, string> values,
        ICollection<string> warnings)
    {
        if (!values.TryGetValue(
                "superweapon.selectedIds",
                out string? text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<uint>();
        }

        List<uint> result = [];
        foreach (string part in text.Split(','))
        {
            if (!uint.TryParse(
                    part.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint id) ||
                !SuperweaponChoices.KnownIds.Contains(id))
            {
                warnings.Add(
                    $"superweapon.selectedIds 包含未知 ID，已忽略 {part.Trim()}");
                continue;
            }

            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }

        return SuperweaponChoices.All
            .Where(choice => result.Contains(choice.Id))
            .Select(choice => choice.Id)
            .ToArray();
    }

    private static void TryDelete(string target)
    {
        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch
        {
            // Preserve the primary settings error.
        }
    }
}
