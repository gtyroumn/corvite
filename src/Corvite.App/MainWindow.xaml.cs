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
        // paste: take plain text from the clipboard and flatten line breaks (the field is single-line)
        DataObject.AddPastingHandler(ChatInput, (_, e) =>
        {
            e.CancelCommand();
            string? text = null;
            for (var i = 0; i < 5 && text == null; i++)
            {
                try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; break; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(30); }
            }
            if (string.IsNullOrEmpty(text)) return;
            ChatInput.SelectedText = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            ChatInput.CaretIndex = ChatInput.SelectionStart + ChatInput.SelectionLength;
            ChatInput.SelectionLength = 0;
        });
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
