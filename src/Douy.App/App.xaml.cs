using System.Windows;

namespace Douy.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            var code = Task.Run(SelfTest.RunAsync).GetAwaiter().GetResult();
            Environment.Exit(code);
            return;
        }

        new MainWindow().Show();
    }
}
