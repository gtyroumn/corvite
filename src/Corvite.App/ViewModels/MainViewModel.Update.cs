using System.Diagnostics;
using Corvite.App.Localization;
using Corvite.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>"New version available" banner on the Overview (stage 8).</summary>
public partial class MainViewModel
{
    private UpdateInfo? _update;
    [ObservableProperty] private bool _updVisible;
    public string UpdTitle => _update == null ? "" : string.Format(Loc.I["upn.t"], _update.Version);

    private async Task CheckUpdateAsync()
    {
        if (!_settings.CheckUpdates) return;
        var u = await UpdateChecker.CheckAsync();
        if (u == null) return;
        _update = u;
        OnPropertyChanged(nameof(UpdTitle));
        UpdVisible = true;
    }

    [RelayCommand]
    private void UpdOpen()
    {
        if (_update == null) return;
        try { Process.Start(new ProcessStartInfo(_update.Url) { UseShellExecute = true }); } catch { /* no browser */ }
    }

    [RelayCommand]
    private void UpdLater() => UpdVisible = false;
}
