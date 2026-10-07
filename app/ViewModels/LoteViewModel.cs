using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Um repositório na lista do lote: entra ou não, e o que aconteceu com ele.</summary>
public sealed partial class LoteItemViewModel : ObservableObject
{
    public Repo Repo { get; init; } = new();
    public string CorDoGrupo { get; init; } = "TextDim";

    [ObservableProperty] private bool _marcado = true;
    [ObservableProperty] private RepoStatus? _status;

    /// <summary>"", "rodando", "ok", "pulado" ou "erro".</summary>
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private string _mensagem = "";

    public string Nome => Repo.Name;
    public string Branch => Status?.Branch is { Length: > 0 } b ? b : "—";

    public string Pendencias
    {
        get
        {
            if (Status is null) return "";
            var partes = new List<string>();
            if (Status.Ahead > 0) partes.Add($"↑{Status.Ahead}");
            if (Status.Behind > 0) partes.Add($"↓{Status.Behind}");
            if (Status.PendingFiles > 0) partes.Add($"●{Status.PendingFiles}");
            return string.Join(" ", partes);
        }
    }

    public string Simbolo => Situacao switch
    {
        "ok" => "✓",
        "erro" => "✕",
        "pulado" => "—",
        "rodando" => "●",
        _ => "",
    };

    public string Cor => Situacao switch
    {
        "ok" => "Green",
        "erro" => "Red",
        "rodando" => "Yellow",
        _ => "TextDim",
    };

    partial void OnStatusChanged(RepoStatus? value)
    {
        OnPropertyChanged(nameof(Branch));
        OnPropertyChanged(nameof(Pendencias));
    }

    partial void OnSituacaoChanged(string value)
    {
        OnPropertyChanged(nameof(Simbolo));
        OnPropertyChanged(nameof(Cor));
    }
}

/// <summary>Uma operação na caixa de escolha do lote.</summary>
public sealed record OpcaoDeLote(OperacaoEmLote Operacao)
{
    public override string ToString() => Lote.Nome(Operacao);
}

/// <summary>
/// Operações em lote: obter, puxar, enviar ou trocar de branch em vários repositórios
/// de uma vez — o grupo do painel, ou os dois lados de um par.
/// </summary>
public sealed partial class LoteViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public LoteViewModel(string titulo, IEnumerable<LoteItemViewModel> itens, MainViewModel main)
    {
        _main = main;
        Title = "Em lote — " + titulo;
        Itens = new ObservableCollection<LoteItemViewModel>(itens);
        foreach (var i in Itens) i.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoteItemViewModel.Marcado)) AvisarBotoes();
        };
    }

    public string Title { get; }
    public ObservableCollection<LoteItemViewModel> Itens { get; }

    public IReadOnlyList<OpcaoDeLote> Operacoes { get; } =
        Enum.GetValues<OperacaoEmLote>().Select(o => new OpcaoDeLote(o)).ToList();

    [ObservableProperty] private OpcaoDeLote _operacao = new(OperacaoEmLote.Obter);
    [ObservableProperty] private string _branch = "";
    [ObservableProperty] private ObservableCollection<string> _branches = new();
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _resumo = "";

    public Func<string, string, Task<bool>>? Confirmar { get; set; }

    public bool PedeBranch => Operacao.Operacao == OperacaoEmLote.Trocar;
    public string Descricao => Lote.Descricao(Operacao.Operacao);
    public int Marcados => Itens.Count(i => i.Marcado);

    public string ExecutarRotulo => Marcados == 1
        ? $"{Lote.Nome(Operacao.Operacao)} em 1 repositório"
        : $"{Lote.Nome(Operacao.Operacao)} em {Marcados} repositórios";

    public bool PodeExecutar => !Busy && Marcados > 0 && (!PedeBranch || Branch.Trim().Length > 0);

    private void AvisarBotoes()
    {
        foreach (var p in new[] { nameof(PedeBranch), nameof(Descricao), nameof(Marcados),
                                  nameof(ExecutarRotulo), nameof(PodeExecutar) })
            OnPropertyChanged(p);
    }

    partial void OnOperacaoChanged(OpcaoDeLote value) => AvisarBotoes();
    partial void OnBranchChanged(string value) => AvisarBotoes();
    partial void OnBusyChanged(bool value) => AvisarBotoes();

    [RelayCommand]
    private void MarcarTodos()
    {
        var marcar = Itens.Any(i => !i.Marcado);
        foreach (var i in Itens) i.Marcado = marcar;
    }

    /// <summary>
    /// As branches de todos os repositórios juntas, as mais comuns primeiro: no lote o
    /// que se quer é a branch que existe em vários (a develop, a release da vez).
    /// </summary>
    public async Task CarregarBranchesAsync()
    {
        var contagem = new Dictionary<string, int>();
        using var vagas = new SemaphoreSlim(6);
        var listas = await Task.WhenAll(Itens.Select(async item =>
        {
            await vagas.WaitAsync();
            try
            {
                return (await GitService.BranchesAsync(item.Repo.Path))
                    .Select(b => new RefDeBranch(b.Name, b.IsRemote).NomeLocal)
                    .Distinct()
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>(); // repositório com problema só não contribui com sugestões
            }
            finally
            {
                vagas.Release();
            }
        }));

        foreach (var nome in listas.SelectMany(l => l))
            contagem[nome] = contagem.GetValueOrDefault(nome) + 1;

        Branches = new ObservableCollection<string>(contagem
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => p.Key));
    }

    [RelayCommand]
    private async Task ExecutarAsync()
    {
        if (!PodeExecutar) return;

        var operacao = Operacao.Operacao;
        var branch = Branch.Trim();
        var alvos = Itens.Where(i => i.Marcado).ToList();

        // obter não muda nada no trabalho de ninguém; o resto confirma, com o tamanho do lote
        if (operacao != OperacaoEmLote.Obter && Confirmar is not null && !await Confirmar(
                Lote.Nome(operacao) + " em lote",
                $"{ExecutarRotulo}{(operacao == OperacaoEmLote.Trocar ? $", para a branch {branch}" : "")}?\n\n" +
                Lote.Descricao(operacao) + "\n\n" +
                string.Join("\n", alvos.Take(12).Select(a => "• " + a.Nome)) +
                (alvos.Count > 12 ? $"\n… e mais {alvos.Count - 12}" : "")))
            return;

        Busy = true;
        Resumo = "";
        foreach (var i in Itens)
        {
            i.Situacao = "";
            i.Mensagem = "";
        }

        try
        {
            // poucos por vez: é rede e disco em cada um, e todos de uma vez travam a máquina
            using var vagas = new SemaphoreSlim(4);
            await Task.WhenAll(alvos.Select(async item =>
            {
                await vagas.WaitAsync();
                try
                {
                    item.Situacao = "rodando";
                    var r = await Lote.ExecutarAsync(item.Repo.Path, operacao, branch);
                    item.Mensagem = r.Mensagem;
                    item.Situacao = r.Situacao;

                    try { item.Status = await GitService.StatusAsync(item.Repo.Path); }
                    catch (Exception) { /* o resultado da operação já está na linha */ }
                }
                finally
                {
                    vagas.Release();
                }
            }));

            var ok = alvos.Count(a => a.Situacao == "ok");
            var pulados = alvos.Count(a => a.Situacao == "pulado");
            var erros = alvos.Count(a => a.Situacao == "erro");
            Resumo = $"{ok} feito(s), {pulados} pulado(s), {erros} com erro";
            Executou = true;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Algo rodou: quem abriu a janela atualiza a árvore e o painel ao fechar.</summary>
    public bool Executou { get; private set; }
}
