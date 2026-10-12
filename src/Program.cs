namespace Bunker;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        MainForm? main = null;
        using (var splash = new SplashForm())
            splash.RunFor(1800, () => main = new MainForm());

        Application.Run(main!);
    }
}
