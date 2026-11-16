using System.IO;
using System.Windows;
using Corvite.App.Setup;
using Corvite.Tweaks;

namespace Corvite.App;

public partial class App : Application
{
    /// <summary>
    /// Start-up modes:
    /// "--restore-baseline": headless rollback of every parameter to the values saved on first launch
    ///   (exit code 0 = all restored, 1 = some failed, 2 = no backup found);
    /// "--uninstall": the uninstall window (this is what the Windows "Apps" entry runs);
    /// "--install", or a file name starting with "Corvite-Setup": the installer window (the setup file is a copy of this exe);
    /// otherwise the app. "--dry-run" makes install / uninstall only pretend.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        var dry = e.Args.Contains("--dry-run");
        var exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");

        if (e.Args.Contains("--restore-baseline"))
        {
            Shutdown(RestoreBaseline());
            return;
        }
        if (e.Args.Contains("--uninstall"))
        {
            new InstallerWindow(new InstallerViewModel(true, dry)).Show();
            return;
        }
        if (e.Args.Contains("--install") || exeName.StartsWith("Corvite-Setup", StringComparison.OrdinalIgnoreCase))
        {
            new InstallerWindow(new InstallerViewModel(false, dry)).Show();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    internal static int RestoreBaseline()
    {
        try
        {
            var b = BaselineStore.Load();
            if (b == null) return 2;
            var failed = new TweakEngine(false).Restore(b.Snapshots);
            return failed.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Info("restore-baseline failed: " + ex.Message);
            return 1;
        }
    }
}
