using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace Corvite.App.Setup;

public partial class InstallerWindow : Window
{
    public InstallerWindow(InstallerViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Corvite.App.Theming.WindowCorners.Round(this);
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
    }

    // the logo animation starts when the "installing" page appears (Loaded would fire while that page is still hidden)
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallerViewModel.Page) && ((InstallerViewModel)DataContext).IsWorking)
            ((Storyboard)Resources["LogoSb"]).Begin(this, true);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
