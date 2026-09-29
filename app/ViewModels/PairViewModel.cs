using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class PairCommitViewModel
{
    public string Subject { get; init; } = "";
    public string Short { get; init; } = "";
}

/// <summary>Um lado do par (Origem ou Destino).</summary>
public sealed partial class PairSideViewModel : ObservableObject
{
    public Repo Repo { get; init; } = new();

    [ObservableProperty] private RepoStatus? _status;
    [ObservableProperty] private ObservableCollection<PairCommitViewModel> _onlyHere = new();

    public string Name => Repo.Name;
    public string RoleText => Repo.Role ?? "";
    public bool HasRole => !string.IsNullOrEmpty(Repo.Role);

    public string BranchText => Status?.Branch is { Length: > 0 } b ? b : "—";
    public string UpstreamText => Status?.Upstream is { Length: > 0 } u ? $"↔ {u}" : "";
    public bool HasUpstream => !string.IsNullOrEmpty(Status?.Upstream);
    public string AheadBehind => $"↑{Status?.Ahead ?? 0} ↓{Status?.Behind ?? 0}";

    public string DirtyText => Status is null
        ? "carregando…"
        : Status.Error is { Length: > 0 } e ? e
        : Status.IsDirty ? $"{Status.Staged + Status.Unstaged + Status.Untracked} alteração(ões)" : "limpo";

    public string DirtyColor => Status is { Error: null, IsDirty: true } ? "Yellow" : "TextDim";
    public int OnlyCount => OnlyHere.Count;
    public bool IsEmpty => OnlyHere.Count == 0;

    public void Refreshed() => OnPropertyChanged(string.Empty);
}

public sealed partial class PairViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly int _limit;

    public PairViewModel(Repo a, Repo b, MainViewModel main, int limit)
    {
        _main = main;
        _limit = Math.Min(limit, 120);
        Left = new PairSideViewModel { Repo = a };
        Right = new PairSideViewModel { Repo = b };
        Title = a.PairKey ?? "";
    }

    public PairSideViewModel Left { get; }
    public PairSideViewModel Right { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _busy;

    public async Task LoadAsync()
    {
        try
        {
            var logA = GitService.LogAsync(Left.Repo.Path, _limit, false);
            var logB = GitService.LogAsync(Right.Repo.Path, _limit, false);
            await Task.WhenAll(logA, logB);

            // atalho: compara commits por assunto; evoluir para patch-id quando houver
            // cherry-pick com mensagem reescrita entre os dois repositórios
            var subjA = logA.Result.Select(c => c.Subject.Trim()).ToHashSet();
            var subjB = logB.Result.Select(c => c.Subject.Trim()).ToHashSet();

            Left.OnlyHere = Only(logA.Result, subjB);
            Right.OnlyHere = Only(logB.Result, subjA);

            Left.Status = await GitService.StatusAsync(Left.Repo.Path);
            Right.Status = await GitService.StatusAsync(Right.Repo.Path);
            Left.Refreshed();
            Right.Refreshed();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private static ObservableCollection<PairCommitViewModel> Only(List<Commit> list, HashSet<string> other) =>
        new(list.Where(c => !other.Contains(c.Subject.Trim()))
                .Select(c => new PairCommitViewModel { Subject = c.Subject, Short = c.Short }));

    private async Task BothAsync(Func<string, Task> action, string label)
    {
        Busy = true;
        try
        {
            await Task.WhenAll(action(Left.Repo.Path), action(Right.Repo.Path));
            await _main.RefreshRepoAsync(Left.Repo.Id);
            await _main.RefreshRepoAsync(Right.Repo.Id);
            await LoadAsync();
            _main.Notify($"{label} concluído nos dois repositórios.");
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private Task FetchBoth() => BothAsync(p => GitService.FetchAsync(p), "Fetch");

    [RelayCommand]
    private Task PullBoth() => BothAsync(p => GitService.PullAsync(p, false), "Pull");

    [RelayCommand]
    private void OpenLeft() => _main.SelectRepo(Left.Repo.Id);

    [RelayCommand]
    private void OpenRight() => _main.SelectRepo(Right.Repo.Id);
}
