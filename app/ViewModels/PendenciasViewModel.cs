using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed partial class PendenciaViewModel
{
    [RelayCommand]
    private void Abrir() => AbrirSemErro(Pendencia.Url);

    /// <summary>Navegador que não abre não tem o que mostrar no painel: segue sem erro.</summary>
    internal static void AbrirSemErro(string url)
    {
        try { ShellService.AbrirUrl(url); }
        catch (Exception) { }
    }

    public Pendencia Pendencia { get; init; } = new();

    public string Titulo => Pendencia.Titulo;

    /// <summary>"GRepos #12": o repositório sem o dono, que ocuparia meia linha.</summary>
    public string Onde => $"{Pendencia.Repo.Split('/').Last()} #{Pendencia.Numero}";

    public string Rodape
    {
        get
        {
            var partes = new List<string> { Onde };
            if (Pendencia.Rascunho) partes.Add("rascunho");
            var quando = Rotulos.Quando(Pendencia.Atualizada);
            if (quando.Length > 0) partes.Add(quando);
            return string.Join(" · ", partes);
        }
    }

    public string Tipo => Pendencia.EhPr ? "PR" : "issue";
    public string Tooltip => $"{Pendencia.Repo} #{Pendencia.Numero} ({Tipo})\n{Pendencia.Titulo}\nClique para abrir no GitHub";
}

/// <summary>Uma das três colunas: título, quantos, os primeiros e o link para o resto.</summary>
public sealed partial class ColunaDePendencias
{
    [RelayCommand]
    private void AbrirResto() => PendenciaViewModel.AbrirSemErro(UrlDoResto);

    public const int Visiveis = 6;

    public string Titulo { get; init; } = "";
    public string Vazio { get; init; } = "";

    /// <summary>A mesma busca na página do GitHub, para quando há mais do que cabe.</summary>
    public string UrlDoResto { get; init; } = "";
    public List<PendenciaViewModel> Todas { get; init; } = new();

    public string Cabecalho => Todas.Count > 0 ? $"{Titulo} · {Todas.Count}" : Titulo;
    public IReadOnlyList<PendenciaViewModel> Itens => Todas.Take(Visiveis).ToList();
    public bool TemItens => Todas.Count > 0;
    public bool SemItens => Todas.Count == 0;
    public int Resto => Math.Max(0, Todas.Count - Visiveis);
    public bool TemResto => Resto > 0;
    public string RestoTexto => $"mais {Resto} no GitHub";
}

/// <summary>
/// "Esperando por você", no painel geral: o que o "gh status" mostra. É da conta que o
/// cartão de perfil está mostrando; trocar de conta lá troca aqui.
/// </summary>
public sealed partial class PendenciasViewModel : ObservableObject
{
    private readonly string _login;

    public PendenciasViewModel(string login)
    {
        _login = login;
    }

    [ObservableProperty] private ObservableCollection<ColunaDePendencias> _colunas = new();
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _erro = "";

    public bool TemErro => Erro.Length > 0;
    public string Resumo
    {
        get
        {
            if (Colunas.Count == 0) return "";
            var revisar = Colunas[0].Todas.Count;
            var atribuidas = Colunas[1].Todas.Count;
            if (revisar + atribuidas == 0) return "nada pedindo sua atenção";
            var partes = new List<string>();
            if (revisar > 0) partes.Add(revisar == 1 ? "1 revisão pedida" : $"{revisar} revisões pedidas");
            if (atribuidas > 0) partes.Add(atribuidas == 1 ? "1 atribuída a você" : $"{atribuidas} atribuídas a você");
            return string.Join(" · ", partes);
        }
    }

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));
    partial void OnColunasChanged(ObservableCollection<ColunaDePendencias> value) => OnPropertyChanged(nameof(Resumo));

    public async Task CarregarAsync(bool forcar = false)
    {
        if (Carregando || _login.Length == 0) return;
        Carregando = true;
        Erro = "";
        try
        {
            var (revisar, atribuidas, meus) = await GitHubService.PendenciasAsync(_login, forcar);
            Aplicar(revisar, atribuidas, meus);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>Separado da rede para ser testado.</summary>
    public void Aplicar(IEnumerable<Pendencia> revisar, IEnumerable<Pendencia> atribuidas, IEnumerable<Pendencia> meus)
    {
        static List<PendenciaViewModel> Vms(IEnumerable<Pendencia> l) =>
            l.Select(p => new PendenciaViewModel { Pendencia = p }).ToList();

        static string Busca(string q) => "https://github.com/search?type=issues&q=" + Uri.EscapeDataString(q);

        Colunas = new ObservableCollection<ColunaDePendencias>
        {
            new()
            {
                Titulo = "REVISÃO PEDIDA A VOCÊ", Vazio = "Nenhum PR esperando sua revisão.",
                Todas = Vms(revisar), UrlDoResto = Busca("is:open is:pr archived:false review-requested:@me"),
            },
            new()
            {
                Titulo = "ATRIBUÍDAS A VOCÊ", Vazio = "Nada atribuído a você.",
                Todas = Vms(atribuidas), UrlDoResto = Busca("is:open archived:false assignee:@me"),
            },
            new()
            {
                Titulo = "SEUS PRs ABERTOS", Vazio = "Nenhum PR seu aberto.",
                Todas = Vms(meus), UrlDoResto = Busca("is:open is:pr archived:false author:@me"),
            },
        };
    }
}
