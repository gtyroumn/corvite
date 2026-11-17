using System.Windows;
using System.Windows.Input;
using Corvite.Core.Ai;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>A small button under a chat message (a suggested next step). Primary = the main one, filled.</summary>
public sealed record ChatBtn(string Text, Action Run, bool Primary = false)
{
    public ICommand Command { get; } = new RelayCommand(Run);
    public Visibility PrimaryVisibility => Primary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GhostVisibility => Primary ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>One proposed change in a checklist: the player can untick what he does not want.</summary>
public sealed partial class ChangeItemVm : ObservableObject
{
    public AdviceChange Change { get; }
    public string Title { get; }
    public string Gain { get; }
    [ObservableProperty] private bool _isChecked = true;
    public ChangeItemVm(AdviceChange change, string title, string gain) { Change = change; Title = title; Gain = gain; }
}

public sealed class ChangeSet
{
    public List<ChangeItemVm> Items { get; }
    public ChangeSet(List<ChangeItemVm> items) { Items = items; }
}

/// <summary>One chat bubble. Optional parts: a single action button, a row of chips, a checklist of changes.</summary>
public sealed record ChatMsg(string Text, bool IsUser, string? ActionText = null, Action? Run = null, bool Greeting = false,
                             IReadOnlyList<ChatBtn>? Chips = null, ChangeSet? Changes = null)
{
    public bool HasAction => ActionText != null && Run != null;
    public ICommand? ActionCommand => Run == null ? null : new RelayCommand(Run);
    public bool HasChips => Chips is { Count: > 0 };
    public bool HasChanges => Changes != null;
}
