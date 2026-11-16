using System.Windows;
using Corvite.Tweaks;

namespace Corvite.App;

public partial class App : Application
{
    /// <summary>
    /// "--restore-baseline": headless rollback of every parameter to the values saved on first launch
    /// (used by the uninstaller). Exit code 0 = all restored, 1 = some failed, 2 = no backup found.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--restore-baseline"))
        {
            Shutdown(RestoreBaseline());
            return;
        }
        base.OnStartup(e);
    }

    private static int RestoreBaseline()
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
