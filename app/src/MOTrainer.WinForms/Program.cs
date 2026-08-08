using MOTrainer.Core.Session;

namespace MOTrainer.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        DisplayLanguage language = DisplayCulture.ReadConfiguredLanguage(
            TrainerSettingsStore.DefaultPath);
        DisplayCulture.Apply(language);

#if NET48
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif
        Application.Run(new MainForm(new GameSessionProbe()));
    }
}
