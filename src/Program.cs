namespace Bunker;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Theme.Init(AppSettings.Load().LightTheme); // must happen before any window exists

        MainForm? main = null;
        using (var splash = new SplashForm())
            splash.RunFor(1800, () => main = new MainForm());

        Application.Run(main!);
    }
}
