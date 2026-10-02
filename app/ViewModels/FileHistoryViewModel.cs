using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class FileCommitViewModel
{
    public Commit Commit { get; init; } = new();

    /// <summary>Caminho do arquivo naquele commit (muda quando foi renomeado).</summary>
    public string Caminho { get; init; } = "";
    public string CaminhoAtual { get; init; } = "";

    public string Short => Commit.Short;
    public string Subject => Commit.Subject;
    public string Author => Commit.Author;
    public string DateText => CommitRowViewModel.FormatDate(Commit.Date, false);
    public bool Renomeado => !string.Equals(Caminho, CaminhoAtual, StringComparison.OrdinalIgnoreCase);
    public string CaminhoTexto => Renomeado ? $"como {Caminho}" : "";
}

public sealed class BlameLineViewModel
{
    public BlameLine Linha { get; init; } = new();

    /// <summary>Primeira linha de um trecho do mesmo commit: só ela mostra autor e data.</summary>
    public bool InicioDeBloco { get; init; }

    /// <summary>Faixas alternadas por trecho, para separar um commit do vizinho.</summary>
    public bool Par { get; init; }

    public string Numero => Linha.Linha.ToString(CultureInfo.InvariantCulture);
    public string Texto => DiffText.Expand(Linha.Texto);
    public string Hash => !InicioDeBloco ? "" : Linha.NaoCommitada ? "local" : Linha.Hash[..7];
    public string Autor => !InicioDeBloco ? "" : Linha.NaoCommitada ? "Não commitado" : Linha.Autor;

    public string Data => !InicioDeBloco || Linha.NaoCommitada
        ? ""
        : DateTimeOffset.FromUnixTimeSeconds(Linha.Quando).ToLocalTime()
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public string Dica => Linha.NaoCommitada
        ? "Alteração ainda não commitada"
        : $"{Linha.Hash[..7]} · {Linha.Autor}\n{Linha.Assunto}";
}

/// <summary>Janela de um arquivo só: os commits que mexeram nele e a autoria de cada linha.</summary>
public sealed partial class FileHistoryViewModel : ObservableObject
{
    private readonly Repo _repo;
    private const int Limite = 500;

    public FileHistoryViewModel(Repo repo, string caminho, bool blame, bool split)
    {
        _repo = repo;
        Caminho = caminho;
        _abaSelecionada = blame ? 1 : 0;
        Diff = new DiffViewModel { Split = split };
    }

    public string Caminho { get; }
    public DiffViewModel Diff { get; }

    [ObservableProperty] private int _abaSelecionada;
    [ObservableProperty] private ObservableCollection<FileCommitViewModel> _commits = new();
    [ObservableProperty] private FileCommitViewModel? _selecionado;
    [ObservableProperty] private ObservableCollection<BlameLineViewModel> _blame = new();
    [ObservableProperty] private string _aviso = "";
    [ObservableProperty] private string _avisoBlame = "";

    private bool _blameCarregado;

    partial void OnSelecionadoChanged(FileCommitViewModel? value) => _ = MostrarDiffAsync(value);

    partial void OnAbaSelecionadaChanged(int value)
    {
        if (value == 1) _ = CarregarBlameAsync();
    }

    public async Task CarregarAsync()
    {
        try
        {
            var lista = await GitService.FileLogAsync(_repo.Path, Caminho, Limite);
            Commits = new ObservableCollection<FileCommitViewModel>(lista.Select(x =>
                new FileCommitViewModel { Commit = x.Commit, Caminho = x.Caminho, CaminhoAtual = Caminho }));
            Aviso = Commits.Count == 0
                ? "Nenhum commit mexeu neste arquivo (ele ainda não foi commitado)."
                : Commits.Count >= Limite ? $"Mostrando os {Limite} commits mais recentes." : $"{Commits.Count} commit(s).";
            Selecionado = Commits.FirstOrDefault();
            if (Selecionado is null) Diff.Clear("Sem commits para mostrar.");
        }
        catch (Exception e)
        {
            Aviso = e.Message;
        }
        if (AbaSelecionada == 1) await CarregarBlameAsync();
    }

    private async Task MostrarDiffAsync(FileCommitViewModel? c)
    {
        if (c is null) return;
        try
        {
            var raw = await GitService.CommitFileDiffAsync(_repo.Path, c.Commit.Hash, c.Caminho);
            Diff.Title = $"{c.Short}  {c.Subject}";
            Diff.Load(raw);
        }
        catch (Exception e)
        {
            Diff.Clear(e.Message);
        }
    }

    private async Task CarregarBlameAsync()
    {
        if (_blameCarregado) return;
        _blameCarregado = true;
        try
        {
            var linhas = await GitService.BlameAsync(_repo.Path, Caminho);
            var lista = new ObservableCollection<BlameLineViewModel>();
            string? anterior = null;
            var par = false;
            foreach (var l in linhas)
            {
                var inicio = l.Hash != anterior;
                if (inicio) par = !par;
                anterior = l.Hash;
                lista.Add(new BlameLineViewModel { Linha = l, InicioDeBloco = inicio, Par = par });
            }
            Blame = lista;
            AvisoBlame = lista.Count == 0 ? "Arquivo vazio ou binário." : "";
        }
        catch (Exception e)
        {
            AvisoBlame = e.Message;
        }
    }

    /// <summary>Do blame para o commit: muda de aba e seleciona o commit da linha.</summary>
    public void IrParaCommit(BlameLineViewModel linha)
    {
        var alvo = Commits.FirstOrDefault(c => c.Commit.Hash == linha.Linha.Hash);
        if (alvo is null) return;
        AbaSelecionada = 0;
        Selecionado = alvo;
    }
}
