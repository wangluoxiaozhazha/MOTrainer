using System.Globalization;
using System.Resources;

namespace MOTrainer.WinForms;

internal static class UiStrings
{
    private static readonly ResourceManager Resources = new(
        "MOTrainer.WinForms.Resources.UiStrings",
        typeof(UiStrings).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
