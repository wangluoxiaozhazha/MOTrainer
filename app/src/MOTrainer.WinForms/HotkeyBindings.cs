using MOTrainer.Core.Profiles;

namespace MOTrainer.WinForms;

internal static class HotkeyActionIds
{
    public const string AddHealth = "unit.add-health";
    public const string SetOneHealth = "unit.set-one-health";
    public const string TransferOwner = "unit.transfer-owner";
    public const string AutoEdit = "unit.auto-edit";
    public const string ZeroEconomy = "faction.zero-economy";
    public const string DefeatOwner = "faction.defeat-owner";
    public const string GrantSelectedSuperweapons =
        "superweapon.grant-selected";

    public static string Patch(string patchId) => $"patch.{patchId}";

}

internal sealed record HotkeyDefinition(
    string ActionId,
    string DisplayName,
    HotkeyGesture DefaultGesture);

internal static class HotkeyDefinitions
{
    public static IReadOnlyList<HotkeyDefinition> All { get; } =
    [
        new(
            HotkeyActionIds.AutoEdit,
            UiStrings.Get("ActionAutoEdit"),
            HotkeyGesture.Ctrl(Keys.A)),
        new(
            HotkeyActionIds.AddHealth,
            UiStrings.Get("ActionAddHealth"),
            HotkeyGesture.Ctrl(Keys.S)),
        new(
            HotkeyActionIds.SetOneHealth,
            UiStrings.Get("ActionSetOneHealth"),
            HotkeyGesture.Ctrl(Keys.D)),
        new(
            HotkeyActionIds.TransferOwner,
            UiStrings.Get("ActionTransferOwner"),
            HotkeyGesture.Ctrl(Keys.F)),
        new(
            HotkeyActionIds.ZeroEconomy,
            UiStrings.Get("ActionZeroEconomy"),
            HotkeyGesture.Ctrl(Keys.O)),
        new(
            HotkeyActionIds.DefeatOwner,
            UiStrings.Get("ActionDefeatOwner"),
            HotkeyGesture.Ctrl(Keys.Space)),
        new(
            HotkeyActionIds.GrantSelectedSuperweapons,
            UiStrings.Get("ActionGrantSuperweapons"),
            HotkeyGesture.Ctrl(Keys.G)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.EconomyNoDecrease),
            UiStrings.Get("FeatureLockEconomy"),
            HotkeyGesture.Ctrl(Keys.Q)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.PowerNoDecrease),
            UiStrings.Get("FeatureLockPower"),
            HotkeyGesture.Ctrl(Keys.W)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.InstantUnitBuild),
            UiStrings.Get("FeatureInstantBuild"),
            HotkeyGesture.Ctrl(Keys.E)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreBuildDistance),
            UiStrings.Get("FeatureBuildAnywhere"),
            HotkeyGesture.Ctrl(Keys.R)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.AutoRepair),
            UiStrings.Get("FeatureAutoRepair"),
            HotkeyGesture.Ctrl(Keys.T)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.ControlAllUnits),
            UiStrings.Get("FeatureControlAll"),
            HotkeyGesture.Ctrl(Keys.I)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.PsychicDetection),
            UiStrings.Get("FeaturePsychicDetection"),
            HotkeyGesture.Ctrl(Keys.Y)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.IgnoreCloak),
            UiStrings.Get("FeatureIgnoreCloak"),
            HotkeyGesture.Ctrl(Keys.U)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.StableRevealMap),
            UiStrings.Get("FeatureRevealMap"),
            HotkeyGesture.Ctrl(Keys.H)),
        new(
            HotkeyActionIds.Patch(KnownPatchIds.SafeTechUnlock),
            UiStrings.Get("FeatureTechUnlock"),
            HotkeyGesture.Ctrl(Keys.P)),
    ];

    public static string DisplayName(string actionId) =>
        All.FirstOrDefault(definition =>
            definition.ActionId == actionId)?.DisplayName ??
        actionId;
}

internal sealed class HotkeyBindings
{
    private readonly Dictionary<string, HotkeyGesture> bindings;

    private HotkeyBindings(Dictionary<string, HotkeyGesture> bindings)
    {
        this.bindings = bindings;
    }

    public HotkeyGesture this[string actionId]
    {
        get => bindings[actionId];
        set => bindings[actionId] = value;
    }

    public bool TryGet(
        string actionId,
        out HotkeyGesture gesture) =>
        bindings.TryGetValue(actionId, out gesture);

    public IReadOnlyList<KeyValuePair<string, HotkeyGesture>> Entries =>
        bindings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

    public HotkeyBindings Clone() =>
        new(new Dictionary<string, HotkeyGesture>(
            bindings,
            StringComparer.Ordinal));

    public void ReplaceWith(HotkeyBindings source)
    {
        bindings.Clear();
        foreach (KeyValuePair<string, HotkeyGesture> binding in
            source.bindings)
        {
            bindings.Add(binding.Key, binding.Value);
        }
    }

    public bool TrySet(
        string actionId,
        HotkeyGesture gesture,
        out string? conflictActionId)
    {
        conflictActionId = null;
        if (!bindings.ContainsKey(actionId))
        {
            return false;
        }

        foreach (KeyValuePair<string, HotkeyGesture> pair in bindings)
        {
            if (pair.Key != actionId && pair.Value == gesture)
            {
                conflictActionId = pair.Key;
                return false;
            }
        }

        bindings[actionId] = gesture;
        return true;
    }

    public static HotkeyBindings CreateDefaults()
    {
        Dictionary<string, HotkeyGesture> defaults =
            HotkeyDefinitions.All.ToDictionary(
                definition => definition.ActionId,
                definition => definition.DefaultGesture,
                StringComparer.Ordinal);

        return new HotkeyBindings(defaults);
    }
}
