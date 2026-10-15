using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Uma conta do repositório no editor: marcada como responsável e/ou como revisora.</summary>
public sealed partial class PessoaViewModel : ObservableObject
{
    public string Login { get; init; } = "";

    [ObservableProperty] private bool _responsavel;
    [ObservableProperty] private bool _revisor;
}

/// <summary>
/// Responsáveis, revisores (só em PR) e marco de uma issue ou PR: a linha de resumo e o
/// editor que abre no lugar dela. Igual nas duas janelas — o GitHub trata o PR como
/// issue para responsáveis e marco, e só a revisão é dele.
/// </summary>
public sealed partial class PessoasViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _usuario;
    private readonly bool _ehPr;

    public PessoasViewModel(string slug, string usuario, bool ehPr)
    {
        _slug = slug;
        _usuario = usuario;
        _ehPr = ehPr;
    }

    public bool EhPr => _ehPr;

    /// <summary>Erro aparece na janela dona; a recarga também é dela.</summary>
    public Action<string>? AoFalhar { get; set; }
    public Func<Task>? AoMudar { get; set; }

    private int _numero;
    private List<string> _responsaveis = new();
    private List<string> _revisores = new();
    private Marco _marco = new(0, "");

    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private bool _editando;
    [ObservableProperty] private bool _salvando;
    [ObservableProperty] private ObservableCollection<PessoaViewModel> _pessoas = new();
    [ObservableProperty] private ObservableCollection<Marco> _marcos = new();
    [ObservableProperty] private Marco? _marcoEscolhido;

    public bool TemNumero => _numero > 0;
    public bool PodeSalvar => !Salvando;
    public bool TemMarcos => Marcos.Count > 1;

    partial void OnSalvandoChanged(bool value) => OnPropertyChanged(nameof(PodeSalvar));
    partial void OnMarcosChanged(ObservableCollection<Marco> value) => OnPropertyChanged(nameof(TemMarcos));

    /// <summary>A issue ou PR escolhido mudou (ou foi recarregado).</summary>
    public void Mostrar(int numero, IReadOnlyList<string> responsaveis, IReadOnlyList<string> revisores, Marco marco)
    {
        if (numero != _numero) Editando = false;
        _numero = numero;
        _responsaveis = responsaveis.ToList();
        _revisores = revisores.ToList();
        _marco = marco;
        OnPropertyChanged(nameof(TemNumero));
        Resumo = MontarResumo(_responsaveis, _revisores, _marco, _ehPr);
    }

    /// <summary>"com ana, bia · revisão: caio · marco 1.1" — o que falta fica de fora.</summary>
    public static string MontarResumo(IReadOnlyList<string> responsaveis, IReadOnlyList<string> revisores, Marco marco, bool ehPr)
    {
        var partes = new List<string>
        {
            responsaveis.Count > 0 ? "com " + string.Join(", ", responsaveis) : "sem responsável",
        };
        if (ehPr && revisores.Count > 0) partes.Add("revisão pedida a " + string.Join(", ", revisores));
        if (marco.Numero > 0) partes.Add("marco " + marco.Titulo);
        return string.Join(" · ", partes);
    }

    [RelayCommand]
    private async Task EditarAsync()
    {
        if (_numero == 0) return;
        Editando = true;
        try
        {
            var pessoas = _slug.Length > 0 ? await GitHubService.PessoasDoRepoAsync(_slug, _usuario) : new List<string>();
            var marcos = _slug.Length > 0 ? await GitHubService.MarcosAsync(_slug, _usuario) : new List<Marco>();
            MontarEditor(pessoas, marcos);
        }
        catch (Exception e)
        {
            Editando = false;
            AoFalhar?.Invoke(e.Message);
        }
    }

    /// <summary>
    /// Separado da rede para ser testado. Quem já está marcado entra mesmo fora da lista
    /// do repositório (perdeu o acesso, por exemplo): senão sairia sem ninguém pedir.
    /// </summary>
    public void MontarEditor(IReadOnlyList<string> doRepo, IReadOnlyList<Marco> marcos)
    {
        var todos = doRepo
            .Concat(_responsaveis).Concat(_revisores)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase);

        Pessoas = new ObservableCollection<PessoaViewModel>(todos.Select(l => new PessoaViewModel
        {
            Login = l,
            Responsavel = _responsaveis.Contains(l, StringComparer.OrdinalIgnoreCase),
            Revisor = _revisores.Contains(l, StringComparer.OrdinalIgnoreCase),
        }));

        // "nenhum" na frente; o marco atual entra mesmo que já esteja fechado
        var lista = new List<Marco> { new(0, "(nenhum)") };
        lista.AddRange(marcos);
        if (_marco.Numero > 0 && lista.All(m => m.Numero != _marco.Numero)) lista.Add(_marco);
        Marcos = new ObservableCollection<Marco>(lista);
        MarcoEscolhido = lista.First(m => m.Numero == _marco.Numero);
        Editando = true;
    }

    [RelayCommand]
    private void Cancelar() => Editando = false;

    [RelayCommand]
    private async Task SalvarAsync()
    {
        if (_numero == 0 || Salvando) return;

        var responsaveis = Pessoas.Where(p => p.Responsavel).Select(p => p.Login).ToList();
        var revisores = Pessoas.Where(p => p.Revisor).Select(p => p.Login).ToList();
        var marco = MarcoEscolhido?.Numero ?? 0;

        var mudouResponsaveis = !Iguais(responsaveis, _responsaveis) || marco != _marco.Numero;
        var pedir = revisores.Except(_revisores, StringComparer.OrdinalIgnoreCase).ToList();
        var tirar = _revisores.Except(revisores, StringComparer.OrdinalIgnoreCase).ToList();

        Salvando = true;
        try
        {
            if (mudouResponsaveis)
                await GitHubService.DefinirResponsaveisEMarcoAsync(_slug, _numero, responsaveis, marco, _usuario);
            if (_ehPr && (pedir.Count > 0 || tirar.Count > 0))
                await GitHubService.AjustarRevisoresAsync(_slug, _numero, pedir, tirar, _usuario);

            Editando = false;
            if (AoMudar is not null) await AoMudar();
        }
        catch (Exception e)
        {
            AoFalhar?.Invoke(e.Message);
        }
        finally
        {
            Salvando = false;
        }
    }

    private static bool Iguais(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));
}
