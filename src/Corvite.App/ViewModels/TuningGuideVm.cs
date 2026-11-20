using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corvite.App.Localization;
using Corvite.App.Services;

namespace Corvite.App.ViewModels;

public sealed record GuideChoice(string Id, string Label);
public sealed record GuideRowVm(string Param, string Safe, string Game, string Max);

/// <summary>
/// Common part of the tuning guide cards (processor, graphics card): two drop-downs (group, then model), the specification line,
/// a table of values and notes. Nothing is written to the machine. Derived classes fill the lists and the table.
/// </summary>
public abstract partial class TuningGuideVm : ObservableObject
{
    protected bool Busy;

    public List<GuideChoice> Groups { get; protected set; } = new();
    public List<GuideChoice> Items { get; protected set; } = new();
    public List<GuideRowVm> Rows { get; protected set; } = new();
    public List<string> Notes { get; protected set; } = new();
    public string Specs { get; protected set; } = "";
    public string Where { get; protected set; } = "";
    public string Detected { get; protected set; } = "";
    public List<string> Steps { get; protected set; } = new();
    protected OfficialApp? App;

    public bool HasSteps => Steps.Count > 0;
    public bool HasApp => App != null;
    public string StepsTitle => Loc.I["gg.steps"];
    public string OpenAppText => App == null ? "" : string.Format(Loc.I["gg.open"], Loc.I[App.NameKey]);

    /// <summary>Numbers the step texts ("1. ...") for display.</summary>
    protected static List<string> Number(IEnumerable<string> texts) => texts.Select((t, i) => $"{i + 1}. {t}").ToList();

    [RelayCommand] private void OpenApp() => App?.Launch();
    [RelayCommand] private void OpenSite() => App?.OpenSite();

    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public abstract string GroupLabel { get; }
    public abstract string ItemLabel { get; }
    public abstract string EmptyText { get; }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string? _groupId;
    [ObservableProperty] private string? _itemId;

    public bool HasDetected => Detected.Length > 0;
    public bool HasItem => Rows.Count > 0;
    public bool NoItem => !HasItem;

    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;

    public void Refresh() => Build();

    /// <summary>Rebuilds the lists for the current device and keeps the saved choice, otherwise picks the installed hardware.</summary>
    public abstract void Reload();
    protected abstract void GroupPicked(string? id);
    protected abstract void ItemPicked(string? id);
    protected abstract void Build();

    partial void OnGroupIdChanged(string? value) { if (!Busy) GroupPicked(value); }
    partial void OnItemIdChanged(string? value) { if (!Busy) ItemPicked(value); }

    protected void Changed() => OnPropertyChanged(string.Empty);
}
