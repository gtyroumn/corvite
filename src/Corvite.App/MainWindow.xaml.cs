using System.Windows;
using Corvite.App.ViewModels;

namespace Corvite.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        vm.AiMessages.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AiOpen) && vm.AiOpen)
                Dispatcher.BeginInvoke(new Action(() => ChatInput.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        };
        Closed += (_, _) => (DataContext as MainViewModel)?.Shutdown();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
