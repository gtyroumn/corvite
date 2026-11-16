using CommunityToolkit.Mvvm.ComponentModel;

namespace Corvite.App.ViewModels;

/// <summary>Loading screen: shown until hardware detection finishes and the logo animation has had time to land.</summary>
public partial class MainViewModel
{
    private static readonly TimeSpan SplashMin = TimeSpan.FromMilliseconds(3000);
    private readonly DateTime _splashStart = DateTime.UtcNow;

    [ObservableProperty] private bool _splashVisible = true;

    private async Task HideSplashAsync()
    {
        var left = SplashMin - (DateTime.UtcNow - _splashStart);
        if (left > TimeSpan.Zero) await Task.Delay(left);
        SplashVisible = false;
    }
}
