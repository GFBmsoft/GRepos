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

public sealed partial class FileItemViewModel : ObservableObject
{
    public FileChange Change { get; init; } = new();
    public bool Staged { get; init; }

    public string Path => Change.Path;
    public string Tooltip => Change.OrigPath is { Length: > 0 } o ? $"{o} → {Change.Path}" : Change.Path;

    public string Code => Staged
        ? Change.Index
        : Change.Kind == ChangeKind.Untracked ? "?" : Change.Worktree;

    /// <summary>Cor do código de status, no padrão do git (M amarelo, A verde, D vermelho…).</summary>
    public string CodeColor => Code switch
    {
        "M" => "Yellow",
        "A" or "?" => "Green",
        "D" => "Red",
        "R" or "C" => "Purple",
        "U" => "Red",
        _ => "TextDim",
    };

    public bool CanDiscard => Change.Kind != ChangeKind.Conflict;
}

public sealed partial class ChangesViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;

    public ChangesViewModel(Repo repo, MainViewModel main, bool split)
    {
        _repo = repo;
        _main = main;
        Diff = new DiffViewModel { Split = split };
    }

    public DiffViewModel Diff { get; }

    [ObservableProperty] private ObservableCollection<FileItemViewModel> _staged = new();
    [ObservableProperty] private ObservableCollection<FileItemViewModel> _unstaged = new();
    [ObservableProperty] private FileItemViewModel? _selectedStaged;
    [ObservableProperty] private FileItemViewModel? _selectedUnstaged;
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private bool _amend;
    [ObservableProperty] private bool _busy;

    public bool CanCommit => !Busy && !string.IsNullOrWhiteSpace(CommitMessage) && (Staged.Count > 0 || Amend);
    public string CommitCaption => Staged.Count > 0 ? $"Commit ({Staged.Count})" : "Commit";

    partial void OnCommitMessageChanged(string value) => OnPropertyChanged(nameof(CanCommit));
    partial void OnAmendChanged(bool value) => OnPropertyChanged(nameof(CanCommit));
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    partial void OnSelectedStagedChanged(FileItemViewModel? value)
    {
        if (value is null) return;
        SelectedUnstaged = null;
        _ = ShowDiffAsync(value);
    }

    partial void OnSelectedUnstagedChanged(FileItemViewModel? value)
    {
        if (value is null) return;
        SelectedStaged = null;
        _ = ShowDiffAsync(value);
    }

    /// <param name="silent">
    /// Recarga disparada pelo disco: o status vem junto da mesma chamada e erros não
    /// viram aviso na tela, senão o usuário é interrompido enquanto digita.
    /// </param>
    public async Task ReloadAsync(bool silent = false)
    {
        try
        {
            var (status, list) = await GitService.StatusAndChangesAsync(_repo.Path);

            var staged = list.Where(f => f.Index != "." && f.Kind != ChangeKind.Untracked)
                .Select(f => new FileItemViewModel { Change = f, Staged = true });
            var unstaged = list.Where(f => f.Index == "." || f.Worktree != ".")
                .Select(f => new FileItemViewModel { Change = f, Staged = false });

            var keepStaged = SelectedStaged?.Path;
            var keepUnstaged = SelectedUnstaged?.Path;

            Staged = new ObservableCollection<FileItemViewModel>(staged);
            Unstaged = new ObservableCollection<FileItemViewModel>(unstaged);
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(CommitCaption));

            // mantém o arquivo que estava aberto; se não houver, abre o primeiro —
            // entrar na aba e ver o painel vazio não ajuda ninguém
            var again = Staged.FirstOrDefault(f => f.Path == keepStaged)
                        ?? (FileItemViewModel?)Unstaged.FirstOrDefault(f => f.Path == keepUnstaged)
                        ?? Unstaged.FirstOrDefault()
                        ?? Staged.FirstOrDefault();

            if (again is null) Diff.Clear("Nenhuma alteração pendente neste repositório.");
            else if (again.Staged) SelectedStaged = again;
            else SelectedUnstaged = again;

            // o status saiu da mesma chamada: aplicar direto evita um segundo "git status"
            _main.ApplyStatus(_repo.Id, status);
        }
        catch (Exception e)
        {
            if (!silent) _main.Notify(e.Message, true);
        }
    }

    private string _lastDiffKey = "";
    private string _lastDiffRaw = "";

    private async Task ShowDiffAsync(FileItemViewModel item)
    {
        try
        {
            var raw = await GitService.DiffFileAsync(
                _repo.Path, item.Path, item.Staged, item.Change.Kind == ChangeKind.Untracked);
            var key = $"{item.Staged}|{item.Path}";

            // diff igual ao que já está na tela não é remontado: evita piscar e
            // perder a posição de rolagem a cada salvamento de arquivo
            if (key == _lastDiffKey && raw == _lastDiffRaw) return;
            _lastDiffKey = key;
            _lastDiffRaw = raw;

            Diff.Title = item.Path + (item.Staged ? "  (preparado)" : "  (local)");
            Diff.Load(
                raw,
                item.Staged ? "Remover bloco" : "Preparar bloco",
                patch => RunAsync(() => GitService.ApplyPatchAsync(_repo.Path, patch, true, item.Staged)));
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        Busy = true;
        try
        {
            await action();
            await ReloadAsync();
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
    private Task StageAll() => RunAsync(() => GitService.StageAsync(_repo.Path, Unstaged.Select(f => f.Path)));

    [RelayCommand]
    private Task UnstageAll() => RunAsync(() => GitService.UnstageAsync(_repo.Path, Staged.Select(f => f.Path)));

    [RelayCommand]
    private Task Stage(FileItemViewModel item) => RunAsync(() => GitService.StageAsync(_repo.Path, new[] { item.Path }));

    [RelayCommand]
    private Task Unstage(FileItemViewModel item) => RunAsync(() => GitService.UnstageAsync(_repo.Path, new[] { item.Path }));

    [RelayCommand]
    private async Task Discard(FileItemViewModel item)
    {
        var ok = await _main.ConfirmAsync(
            "Descartar alterações",
            $"Descartar as alterações de \"{item.Path}\"?\n\nEsta ação não pode ser desfeita.");
        if (!ok) return;

        var untracked = item.Change.Kind == ChangeKind.Untracked;
        await RunAsync(() => GitService.DiscardAsync(
            _repo.Path,
            untracked ? Array.Empty<string>() : new[] { item.Path },
            untracked ? new[] { item.Path } : Array.Empty<string>()));
    }

    [RelayCommand]
    private async Task Commit()
    {
        if (!CanCommit) return;
        Busy = true;
        try
        {
            await GitService.CommitAsync(_repo.Path, CommitMessage, Amend);
            CommitMessage = "";
            Amend = false;
            _main.Notify("Commit criado.");
            await ReloadAsync();
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
}
