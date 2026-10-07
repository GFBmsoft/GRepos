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

public sealed class WorktreeItemViewModel
{
    public Worktree Worktree { get; init; } = new();

    /// <summary>Já está no workspace do GRepos, como um repositório da árvore.</summary>
    public bool NoWorkspace { get; init; }

    public string Caminho => Worktree.Caminho;
    public string Branch => Worktree.Branch.Length > 0 ? Worktree.Branch : "(HEAD solto)";
    public string Hash => Worktree.Head.Length >= 7 ? Worktree.Head[..7] : Worktree.Head;

    public string Situacao =>
        Worktree.Principal ? "pasta principal"
        : Worktree.Orfa ? "a pasta não existe mais"
        : Worktree.Travada ? "travada"
        : "";

    public string Cor => Worktree.Principal ? "Green" : Worktree.Orfa ? "Red" : "Accent";

    public bool PodeRemover => !Worktree.Principal;
    public bool PodeAbrir => !Worktree.Orfa;
    public bool PodeAdicionar => !Worktree.Orfa && !NoWorkspace;
}

/// <summary>
/// Pastas de trabalho (worktrees) do repositório: listar, criar uma para outra branch
/// e remover. Cada uma pode entrar no workspace como um repositório a mais.
/// </summary>
public sealed partial class WorktreesViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;

    public WorktreesViewModel(Repo repo, MainViewModel main)
    {
        _repo = repo;
        _main = main;
        Title = $"Pastas de trabalho — {repo.Name}";
    }

    public string Title { get; }

    [ObservableProperty] private ObservableCollection<WorktreeItemViewModel> _lista = new();
    [ObservableProperty] private ObservableCollection<string> _branches = new();
    [ObservableProperty] private string _novaBranch = "";
    [ObservableProperty] private string _novaPasta = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _erro = "";

    public bool TemErro => Erro.Length > 0;
    public bool PodeCriar => !Busy && NovaBranch.Trim().Length > 0 && NovaPasta.Trim().Length > 0;
    public bool TemOrfas => Lista.Any(w => w.Worktree.Orfa);

    /// <summary>Confirmação e seletor de pasta; a janela preenche com os diálogos dela.</summary>
    public Func<string, string, Task<bool>>? Confirmar { get; set; }
    public Func<string, Task<string?>>? EscolherPasta { get; set; }

    /// <summary>A pasta sugerida acompanha a branch até o usuário mexer nela.</summary>
    private string _pastaSugerida = "";

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(PodeCriar));
    partial void OnNovaPastaChanged(string value) => OnPropertyChanged(nameof(PodeCriar));

    partial void OnNovaBranchChanged(string value)
    {
        if (NovaPasta.Length == 0 || NovaPasta == _pastaSugerida)
        {
            _pastaSugerida = Worktrees.PastaSugerida(_repo.Path, value ?? "");
            NovaPasta = _pastaSugerida;
        }
        OnPropertyChanged(nameof(PodeCriar));
    }

    public async Task CarregarAsync()
    {
        try
        {
            var lista = await Worktrees.ListarAsync(_repo.Path);
            var conhecidos = _main.Repos.Select(r => Normalizar(r.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Lista = new ObservableCollection<WorktreeItemViewModel>(lista.Select(w => new WorktreeItemViewModel
            {
                Worktree = w,
                NoWorkspace = conhecidos.Contains(Normalizar(w.Caminho)),
            }));

            // uma branch só abre numa pasta por vez: as que já estão abertas saem da sugestão
            var abertas = lista.Select(w => w.Branch).ToHashSet();
            var todas = await GitService.BranchesAsync(_repo.Path);
            Branches = new ObservableCollection<string>(todas
                .Select(b => new RefDeBranch(b.Name, b.IsRemote).NomeLocal)
                .Where(n => !abertas.Contains(n))
                .Distinct()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));

            OnPropertyChanged(nameof(TemOrfas));
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }

    private static string Normalizar(string caminho) => caminho.Replace('\\', '/').TrimEnd('/');

    private async Task ExecutarAsync(Func<Task> acao)
    {
        Busy = true;
        Erro = "";
        try
        {
            await acao();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            await CarregarAsync();
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task ProcurarPasta()
    {
        if (EscolherPasta is null) return;
        if (await EscolherPasta("Pasta da nova pasta de trabalho") is { Length: > 0 } pasta) NovaPasta = pasta;
    }

    [RelayCommand]
    private Task Criar() => ExecutarAsync(async () =>
    {
        await Worktrees.CriarAsync(_repo.Path, NovaPasta, NovaBranch);
        _pastaSugerida = "";
        NovaPasta = "";
        NovaBranch = "";
    });

    /// <summary>
    /// Remover apaga a pasta do disco. O git recusa se houver alteração não commitada
    /// lá; aí pergunta de novo, dizendo o que se perde, antes de forçar.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Remover(WorktreeItemViewModel item)
    {
        if (!item.PodeRemover || Confirmar is null) return;

        if (!await Confirmar("Remover pasta de trabalho",
                $"Remover a pasta de trabalho da branch {item.Branch}?\n\n{item.Caminho}\n\n" +
                "A pasta é apagada do disco. A branch e os commits continuam no repositório."))
            return;

        Busy = true;
        Erro = "";
        try
        {
            try
            {
                await Worktrees.RemoverAsync(_repo.Path, item.Caminho, forcar: false);
            }
            catch (GitException e) when (e.Message.Contains("--force", StringComparison.OrdinalIgnoreCase) ||
                                         e.Message.Contains("modified or untracked", StringComparison.OrdinalIgnoreCase))
            {
                if (!await Confirmar("Há alterações não commitadas",
                        $"A pasta tem arquivos alterados ou novos que não foram commitados:\n\n{item.Caminho}\n\n" +
                        "Remover mesmo assim? Essas alterações serão perdidas e não há como recuperá-las."))
                    return;

                await Worktrees.RemoverAsync(_repo.Path, item.Caminho, forcar: true);
            }
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            await CarregarAsync();
            Busy = false;
        }
    }

    [RelayCommand]
    private Task LimparOrfas() => ExecutarAsync(() => Worktrees.LimparOrfasAsync(_repo.Path));

    [RelayCommand]
    private void AbrirPasta(WorktreeItemViewModel item)
    {
        try
        {
            ShellService.AbrirPasta(item.Caminho);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }

    /// <summary>Põe a pasta na árvore, no mesmo grupo do repositório, com a branch no nome.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Adicionar(WorktreeItemViewModel item)
    {
        if (!item.PodeAdicionar) return;

        _main.AddRepository(item.Caminho, $"{_repo.Name} ({item.Branch})", _repo.GroupId, _repo.Conta);
        await CarregarAsync();
    }
}
