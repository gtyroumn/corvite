using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corvite.App.Localization;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using Microsoft.Win32;

namespace Corvite.App.ViewModels;

/// <summary>One row of the apply history: what was applied and when. Index 0 is the newest.</summary>
public sealed class HistItemVm
{
    public HistItemVm(int index, string when, string summary, string count)
    { Index = index; When = when; Summary = summary; Count = count; }
    public int Index { get; }
    public string When { get; }
    public string Summary { get; }
    public string Count { get; }
}

/// <summary>Apply history with rollback to any step, plus export / import of the whole set of values as one file.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- history
    public ObservableCollection<HistItemVm> HistItems { get; } = new();
    [ObservableProperty] private bool _histOpen;
    public bool HasHist => _hist.Count > 0;
    public bool NoHist => _hist.Count == 0;
    public string HistCountText => _hist.Count > 0 ? _hist.Count.ToString() : "";

    private void RebuildHist()
    {
        HistItems.Clear();
        var i = 0;
        foreach (var h in _hist)   // Stack enumerates the newest entry first
        {
            var titles = h.Ids.Where(_vms.ContainsKey).Select(id => _vms[id].Title).ToList();
            var summary = titles.Count <= 2 ? string.Join(", ", titles) : string.Join(", ", titles.Take(2)) + " +" + (titles.Count - 2);
            var when = h.When.Date == DateTime.Today ? h.When.ToString("HH:mm") : h.When.ToString("dd.MM HH:mm");
            HistItems.Add(new HistItemVm(i++, when, summary, string.Format(Loc.I["hist.n"], h.Ids.Count)));
        }
        if (_hist.Count == 0) HistOpen = false;
    }

    [RelayCommand]
    private void HistToggle()
    {
        if (_hist.Count == 0) return;
        HistOpen = !HistOpen;
        if (HistOpen) AiOpen = false;
    }

    [RelayCommand] private void HistClose() => HistOpen = false;

    /// <summary>Returns the system to the state it had before the chosen apply (all later applies are undone too).</summary>
    [RelayCommand]
    private async Task HistBack(HistItemVm item)
    {
        if (item == null) return;
        HistOpen = false;
        await UndoSteps(item.Index + 1, Loc.I["hist.done"]);
    }

    private async Task UndoSteps(int n, string toastPrefix)
    {
        if (IsBusy || _hist.Count == 0 || n <= 0) return;
        n = Math.Min(n, _hist.Count);
        IsBusy = true;
        var allFailed = new Dictionary<string, string>();
        for (var i = 0; i < n; i++)
        {
            var h = _hist.Pop();
            Dictionary<string, string> failed;
            try { failed = await Task.Run(() => _engine.Restore(h.Snaps)); }
            catch (Exception ex) { failed = h.Snaps.Keys.ToDictionary(k => k, _ => ex.Message); }
            foreach (var kv in h.Prior)
                if (!failed.ContainsKey(kv.Key)) SetBoth(kv.Key, kv.Value);
            foreach (var kv in failed) allFailed[kv.Key] = kv.Value;
        }
        _profile = DetectProfileLocal();
        IsBusy = false;
        Toast(allFailed.Count > 0
            ? Loc.I["toast.fail"] + string.Join(", ", allFailed.Keys.Where(_vms.ContainsKey).Select(k => _vms[k].Title))
            : toastPrefix + _hist.Count);
    }

    // ---------------------------------------------------------------- export / import of a profile
    private sealed class ProfileFile
    {
        public string App { get; set; } = "Corvite";
        public int Format { get; set; } = 1;
        public string Version { get; set; } = "";
        public string Platform { get; set; } = "pc";
        public DateTime Created { get; set; }
        public Dictionary<string, double> Values { get; set; } = new();
    }

    [RelayCommand]
    private void ExportProfile()
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc.I["pf.exp.t"],
            Filter = "Corvite profile (*.corvite.json)|*.corvite.json|JSON (*.json)|*.json",
            FileName = "My-profile.corvite.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var f = new ProfileFile
            {
                Version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "",
                Platform = Platform == PlatformKind.Laptop ? "laptop" : "pc",
                Created = DateTime.Now
            };
            foreach (var d in TweakCatalog.All.Where(d => !d.IsGuard && IsSupported(d.Id)))
                f.Values[d.Id] = _s[d.Id];
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(f, new JsonSerializerOptions { WriteIndented = true }));
            Toast(Loc.I["pf.saved"]);
        }
        catch (Exception ex) { Toast(Loc.I["toast.fail"] + ex.Message); }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.I["pf.imp.t"],
            Filter = "Corvite profile (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var info = new FileInfo(dlg.FileName);
            if (info.Length > 1_000_000) { Toast(Loc.I["pf.bad"]); return; }
            var f = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(dlg.FileName));
            if (f == null || f.App != "Corvite" || f.Values == null || f.Values.Count == 0) { Toast(Loc.I["pf.bad"]); return; }

            // only known, supported parameters; values are clamped to this machine's limits; nothing is written until "Apply"
            var taken = 0;
            foreach (var kv in f.Values)
            {
                var d = TweakCatalog.All.FirstOrDefault(x => x.Id == kv.Key);
                if (d == null || d.IsGuard || !IsSupported(d.Id) || double.IsNaN(kv.Value) || double.IsInfinity(kv.Value)) continue;
                var v = d.IsToggle ? (kv.Value >= 0.5 ? 1 : 0) : Math.Clamp(kv.Value, d.Min, ProfileEngine.EffectiveMax(d, Platform));
                if (!d.IsToggle && d.Step > 0) v = d.Min + Math.Round((v - d.Min) / d.Step) * d.Step;
                _s[d.Id] = v;
                taken++;
            }
            _profile = DetectProfileLocal();
            RefreshAll();
            Toast(taken == 0 ? Loc.I["pf.bad"] : string.Format(Loc.I["pf.loaded"], taken));
        }
        catch { Toast(Loc.I["pf.bad"]); }
    }
}
