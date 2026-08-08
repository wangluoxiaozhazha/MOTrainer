using MOTrainer.Core.Session;

namespace MOTrainer.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
#if NET48
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif
        bool refreshLanguage;
        do
        {
            DisplayLanguage language =
                DisplayCulture.ReadConfiguredLanguage(
                    TrainerSettingsStore.DefaultPath);
            DisplayCulture.Apply(language);

            MainForm form = new(new GameSessionProbe());
            Application.Run(form);
            refreshLanguage = form.LanguageRefreshRequested;
        }
        while (refreshLanguage);
    }
}
