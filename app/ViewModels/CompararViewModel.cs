using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Comparação entre duas branches, tags ou commits: quantos commits cada lado tem a
/// mais, os arquivos que diferem e o diff de cada um. Só leitura — nada aqui mexe no
/// repositório.
/// </summary>
public sealed partial class CompararViewModel : ObservableObject
{
    private readonly Repo _repo;

    public CompararViewModel(Repo repo, string a, string b, bool split)
    {
        _repo = repo;
        _a = a;
        _b = b;
        Title = $"Comparar — {repo.Name}";
        Diff = new DiffViewModel { Split = split, Recarregar = () => MostrarDiffAsync(Selecionado) };
    }

    public string Title { get; }
    public DiffViewModel Diff { get; }

    [ObservableProperty] private string _a;
    [ObservableProperty] private string _b;
    [ObservableProperty] private ObservableCollection<string> _refs = new();
    [ObservableProperty] private ObservableCollection<CommitFileViewModel> _arquivos = new();
    [ObservableProperty] private CommitFileViewModel? _selecionado;
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _erro = "";
    [ObservableProperty] private bool _busy;

    /// <summary>As pontas da comparação que está na tela; mudar o texto não troca o diff aberto.</summary>
    private string _comparadoA = "";
    private string _comparadoB = "";

    public bool TemErro => Erro.Length > 0;
    public bool PodeComparar => !Busy && A.Trim().Length > 0 && B.Trim().Length > 0;

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));
    partial void OnAChanged(string value) => OnPropertyChanged(nameof(PodeComparar));
    partial void OnBChanged(string value) => OnPropertyChanged(nameof(PodeComparar));
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(PodeComparar));

    partial void OnSelecionadoChanged(CommitFileViewModel? value) => _ = MostrarDiffAsync(value);

    /// <summary>A janela chama ao abrir: carrega as refs e, com as duas pontas dadas, já compara.</summary>
    public async Task IniciarAsync()
    {
        try
        {
            Refs = new ObservableCollection<string>(await GitService.RefsAsync(_repo.Path));
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        if (PodeComparar) await CompararAsync();
    }

    [RelayCommand]
    private void Inverter() => (A, B) = (B, A);

    [RelayCommand]
    public async Task CompararAsync()
    {
        if (!PodeComparar) return;

        Busy = true;
        Erro = "";
        try
        {
            var a = A.Trim();
            var b = B.Trim();
            var r = await GitService.CompararAsync(_repo.Path, a, b);
            _comparadoA = a;
            _comparadoB = b;

            Arquivos = new ObservableCollection<CommitFileViewModel>(
                r.Arquivos.Select(f => new CommitFileViewModel { File = f }));
            Resumo = Descrever(a, b, r.SoEmA, r.SoEmB, r.Arquivos.Count);

            Selecionado = Arquivos.FirstOrDefault();
            if (Selecionado is null) Diff.Clear("Os dois lados têm o mesmo conteúdo.");
        }
        catch (Exception e)
        {
            Arquivos = new ObservableCollection<CommitFileViewModel>();
            Resumo = "";
            Diff.Clear("");
            Erro = e.Message.Contains("unknown revision", StringComparison.OrdinalIgnoreCase) ||
                   e.Message.Contains("bad revision", StringComparison.OrdinalIgnoreCase)
                ? "Uma das pontas não existe neste repositório. Confira o nome da branch, da tag ou o hash."
                : e.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>A frase do rodapé: quem tem o quê que o outro não tem.</summary>
    public static string Descrever(string a, string b, int soEmA, int soEmB, int arquivos)
    {
        static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";

        var lados = (soEmA, soEmB) switch
        {
            (0, 0) => "as duas pontas estão no mesmo commit",
            (0, _) => $"{b} tem {Commits(soEmB)} que {a} não tem",
            (_, 0) => $"{a} tem {Commits(soEmA)} que {b} não tem",
            _ => $"{b} tem {Commits(soEmB)} a mais e {a} tem {Commits(soEmA)} a mais",
        };
        var mudou = arquivos == 1 ? "1 arquivo diferente" : $"{arquivos} arquivos diferentes";
        return $"{lados} · {mudou}";
    }

    private async Task MostrarDiffAsync(CommitFileViewModel? arquivo)
    {
        if (arquivo is null || _comparadoA.Length == 0) return;
        try
        {
            var raw = await GitService.CompararArquivoAsync(
                _repo.Path, _comparadoA, _comparadoB, arquivo.Path, Diff.LinhasDeContexto);
            if (!ReferenceEquals(Selecionado, arquivo)) return; // trocou de arquivo no meio

            Diff.Title = $"{arquivo.Path}  ({_comparadoA} → {_comparadoB})";
            Diff.Load(raw);
        }
        catch (Exception e)
        {
            Diff.Clear(e.Message);
        }
    }
}
