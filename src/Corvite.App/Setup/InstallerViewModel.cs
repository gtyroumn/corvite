using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corvite.Tweaks;
using Microsoft.Win32;

namespace Corvite.App.Setup;

public enum SetupPage { Options, Working, Done, Error }

/// <summary>View model of the setup window; the same window serves "install" and "uninstall". Installer texts follow the language chosen in the window.</summary>
public partial class InstallerViewModel : ObservableObject
{
    private const int MinAnimationMs = 3600;      // the logo animation needs this long to finish
    private readonly bool _dry;
    private readonly DispatcherTimer _ease = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _target;

    public InstallerViewModel(bool uninstall, bool dryRun)
    {
        IsUninstall = uninstall;
        _dry = dryRun;
        var existing = Installer.InstalledDir();
        HasExisting = !uninstall && existing != null;
        Dir = existing ?? Installer.DefaultDir;
        var sys = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        Lang = sys == "ru" ? "ru" : sys == "uk" ? "uk" : "en";
        RestoreSettings = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Corvite", "Backups", "baseline.json"));
        _ease.Tick += (_, _) =>
        {
            var d = _target - Progress;
            Progress = Math.Abs(d) < 0.05 ? _target : Progress + d * 0.12;
        };
    }

    public bool IsUninstall { get; }
    public bool IsInstall => !IsUninstall;
    public bool HasExisting { get; }
    public string this[string key] => SetupText.Get(key, Lang);
    public string Title => this[IsUninstall ? "t.uninst" : "t.inst"];
    public string Subtitle => IsUninstall ? this["s.uninst"] : string.Format(this["s.inst"], Installer.Version);
    public string PrimaryText => this[IsUninstall ? "b.uninst" : "b.inst"];
    public string DoneTitle => this[IsUninstall ? "d.uninst" : "d.inst"];
    public string DoneHint => this[IsUninstall ? "h.uninst" : "h.inst"];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOptions), nameof(IsWorking), nameof(IsDone), nameof(IsError), nameof(ShowAnim), nameof(CanClose))]
    private SetupPage _page = SetupPage.Options;
    public bool IsOptions => Page == SetupPage.Options;
    public bool IsWorking => Page == SetupPage.Working;
    public bool IsDone => Page == SetupPage.Done;
    public bool IsError => Page == SetupPage.Error;
    public bool ShowAnim => Page is SetupPage.Working or SetupPage.Done;
    public bool CanClose => Page != SetupPage.Working;

    [ObservableProperty] private string _dir = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRu), nameof(IsUk), nameof(IsEn))] private string _lang = "en";
    partial void OnLangChanged(string value) { OnPropertyChanged(string.Empty); if (!string.IsNullOrEmpty(_lastStatus)) Status = SetupText.Tr(_lastStatus, value); }
    private string _lastStatus = "";
    public bool IsRu => Lang == "ru";
    public bool IsUk => Lang == "uk";
    public bool IsEn => Lang == "en";
    [ObservableProperty] private bool _desktop = true;
    [ObservableProperty] private bool _launchAfter = true;
    [ObservableProperty] private bool _restoreSettings;
    [ObservableProperty] private bool _deleteData;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressScale))] private double _progress;
    public double ProgressScale => Math.Clamp(Progress / 100.0, 0, 1);
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _errorText = "";

    [RelayCommand] private void SetLang(string l) => Lang = l;

    /// <summary>"Remove the installed Corvite" from the setup window: opens the uninstall window of the installed copy and closes this one.</summary>
    [RelayCommand]
    private void UninstallExisting()
    {
        if (_dry || Installer.StartUninstall()) System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFolderDialog { Title = SetupText.Get("dlg", Lang) };
        try { if (Directory.Exists(Dir)) dlg.InitialDirectory = Dir; } catch { }
        if (dlg.ShowDialog() == true) Dir = dlg.FolderName;
    }

    [RelayCommand]
    private async Task Run()
    {
        if (!IsUninstall && string.IsNullOrWhiteSpace(Dir)) return;
        Page = SetupPage.Working;
        Progress = 0; _target = 0; _ease.Start();
        var started = Stopwatch.StartNew();
        var pr = new Progress<(double Pct, string Text)>(t => { _target = t.Pct; _lastStatus = t.Text; Status = SetupText.Tr(t.Text, Lang); });
        try
        {
            if (IsUninstall) await Installer.UninstallAsync(new UninstallOptions(RestoreSettings, DeleteData, _dry), pr);
            else await Installer.InstallAsync(new InstallOptions(Dir, Lang, Desktop, _dry), pr);
        }
        catch (Exception ex)
        {
            Log.Info("setup failed: " + ex);
            ErrorText = ex.Message;
            _ease.Stop();
            Page = SetupPage.Error;
            return;
        }
        _target = 100;
        var left = MinAnimationMs - (int)started.ElapsedMilliseconds;
        if (left > 0) await Task.Delay(left);
        _ease.Stop();
        Progress = 100;
        Page = SetupPage.Done;
    }

    [RelayCommand]
    private void Finish()
    {
        if (IsInstall && LaunchAfter && !_dry && Page == SetupPage.Done)
        {
            try
            {
                var exe = Path.Combine(Installer.NormalizeDir(Dir), "Corvite.exe");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
            }
            catch (Exception ex) { Log.Info("launch failed: " + ex.Message); }
        }
        Application.Current.Shutdown();
    }

    [RelayCommand] private void Cancel() => Application.Current.Shutdown();
}
