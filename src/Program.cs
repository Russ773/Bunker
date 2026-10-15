namespace Bunker;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Bunker.exe --check-widget in.json out.json: judge a widget file without opening any window (used by the check on GitHub).
        if (args.Length >= 3 && args[0] == "--check-widget") return WidgetCheck.Run(args[1], args[2]);

        ApplicationConfiguration.Initialize();
        Theme.Init(AppSettings.Load().LightTheme); // must happen before any window exists

        MainForm? main = null;
        using (var splash = new SplashForm())
            splash.RunFor(1800, () => main = new MainForm());

        Application.Run(main!);
        return 0;
    }
}
