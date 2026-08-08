using System.Globalization;
using System.Resources;

namespace MOTrainer.Core.Localization;

internal static class CoreStrings
{
    private static readonly ResourceManager Resources = new(
        "MOTrainer.Core.Resources.CoreStrings",
        typeof(CoreStrings).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] arguments) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Get(key),
            arguments);
}
