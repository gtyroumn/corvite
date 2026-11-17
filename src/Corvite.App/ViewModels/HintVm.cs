using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>One quiet hint on the Overview: a line of text, an optional action button and a close button.</summary>
public sealed class HintVm
{
    public string Id { get; }
    public string Text { get; }
    public string? ActionText { get; }
    public ICommand? ActionCommand { get; }
    public bool HasAction => ActionCommand != null;
    public ICommand CloseCommand { get; }

    public HintVm(string id, string text, string? actionText, Action? run, Action close)
    {
        Id = id; Text = text; ActionText = actionText;
        ActionCommand = run == null ? null : new RelayCommand(run);
        CloseCommand = new RelayCommand(close);
    }
}
