using CommunityToolkit.Mvvm.ComponentModel;

namespace Corvite.App.ViewModels;

/// <summary>Loading screen: shown until hardware detection finishes and the logo animation has had time to land.</summary>
public partial class MainViewModel
{
    private static readonly TimeSpan SplashMin = TimeSpan.FromMilliseconds(2900);
    private readonly DateTime _splashStart = DateTime.UtcNow;

    [ObservableProperty] private bool _splashVisible = true;

    /// <summary>
    /// Waits until the logo animation has finished. The heavy UI work after detection (building pages, refreshing every row)
    /// runs on the UI thread and would make the animation stutter, so it starts only when the animation is done.
    /// </summary>
    private async Task WaitSplashAsync()
    {
        var left = SplashMin - (DateTime.UtcNow - _splashStart);
        if (left > TimeSpan.Zero) await Task.Delay(left);
    }
}
