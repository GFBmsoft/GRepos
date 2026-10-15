using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class ItemDeActionsViewModel
{
    public ItemDeActions Item { get; init; } = new();
    public bool Segredo { get; init; }

    public string Nome => Item.Nome;

    /// <summary>Segredo não tem valor a mostrar: nem o GitHub devolve.</summary>
    public string Valor => Segredo ? "••••••" : Item.Valor.Replace("\n", " ⏎ ");
    public string Quando => Rotulos.Quando(Item.Atualizado) is { Length: > 0 } q ? "alterado " + q : "";
}

/// <summary>
/// Segredos e variáveis do GitHub Actions de um repositório — o "gh secret set" e o
/// "gh variable set". Segredo só se cria, troca ou apaga: o valor não volta da API.
/// </summary>
public sealed partial class SegredosViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _usuario;

    public SegredosViewModel(string slug, string usuario, string repoNome = "")
    {
        _slug = slug;
        _usuario = usuario;
        Title = repoNome.Length > 0 ? $"Segredos e variáveis — {repoNome}" : "Segredos e variáveis";
        Subtitulo = slug;
    }

    public string Title { get; }
    public string Subtitulo { get; }

    /// <summary>Confirmação de excluir; a janela liga ao diálogo dela.</summary>
    public Func<string, string, Task<bool>>? Confirmar { get; set; }

    [ObservableProperty] private ObservableCollection<ItemDeActionsViewModel> _segredos = new();
    [ObservableProperty] private ObservableCollection<ItemDeActionsViewModel> _variaveis = new();
    [ObservableProperty] private ItemDeActionsViewModel? _segredoSelecionado;
    [ObservableProperty] private ItemDeActionsViewModel? _variavelSelecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private bool _executando;
    [ObservableProperty] private string _erro = "";

    public bool TemErro => Erro.Length > 0;
    public bool SemSegredos => !Carregando && Segredos.Count == 0;
    public bool SemVariaveis => !Carregando && Variaveis.Count == 0;
    public bool TemSegredoEscolhido => SegredoSelecionado is not null && !Executando;
    public bool TemVariavelEscolhida => VariavelSelecionada is not null && !Executando;

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));
    partial void OnSegredoSelecionadoChanged(ItemDeActionsViewModel? value) => OnPropertyChanged(nameof(TemSegredoEscolhido));
    partial void OnVariavelSelecionadaChanged(ItemDeActionsViewModel? value) => OnPropertyChanged(nameof(TemVariavelEscolhida));

    partial void OnCarregandoChanged(bool value)
    {
        OnPropertyChanged(nameof(SemSegredos));
        OnPropertyChanged(nameof(SemVariaveis));
    }

    partial void OnExecutandoChanged(bool value)
    {
        foreach (var p in new[] { nameof(TemSegredoEscolhido), nameof(TemVariavelEscolhida), nameof(PodeSalvar) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (Carregando || _slug.Length == 0) return;
        Carregando = true;
        Erro = "";
        try
        {
            // uma lista pode falhar sem levar a outra (token só com permissão de variáveis)
            var segredos = GitHubService.SegredosAsync(_slug, _usuario);
            var variaveis = GitHubService.VariaveisAsync(_slug, _usuario);
            try { await Task.WhenAll(segredos, variaveis); } catch (Exception) { }

            Aplicar(segredos.IsCompletedSuccessfully ? segredos.Result : new List<ItemDeActions>(),
                    variaveis.IsCompletedSuccessfully ? variaveis.Result : new List<ItemDeActions>());

            var falha = segredos.Exception ?? variaveis.Exception;
            if (falha is not null) Erro = falha.InnerException?.Message ?? falha.Message;
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>Recebe as listas direto; separado da rede para ser testado.</summary>
    public void Aplicar(IReadOnlyList<ItemDeActions> segredos, IReadOnlyList<ItemDeActions> variaveis)
    {
        Segredos = new ObservableCollection<ItemDeActionsViewModel>(
            segredos.Select(s => new ItemDeActionsViewModel { Item = s, Segredo = true }));
        Variaveis = new ObservableCollection<ItemDeActionsViewModel>(
            variaveis.Select(v => new ItemDeActionsViewModel { Item = v }));
        OnPropertyChanged(nameof(SemSegredos));
        OnPropertyChanged(nameof(SemVariaveis));
    }

    // ------------------------------------------------------- formulário

    [ObservableProperty] private bool _editando;
    [ObservableProperty] private string _formNome = "";
    [ObservableProperty] private string _formValor = "";

    /// <summary>"segredo" ou "variável": o formulário é o mesmo, o destino não.</summary>
    private bool _formSegredo;
    private bool _formExiste;

    public string FormCabecalho => (_formSegredo, _formExiste) switch
    {
        (true, false) => "NOVO SEGREDO",
        (true, true) => $"TROCAR O VALOR DE {FormNome}",
        (false, false) => "NOVA VARIÁVEL",
        _ => $"EDITAR {FormNome}",
    };

    /// <summary>Renomear não existe: muda o nome, é outro item.</summary>
    public bool NomeEditavel => !_formExiste;
    public bool FormEhSegredo => _formSegredo;
    public string ProblemaNoNome => FormNome.Length == 0 ? "" : GitHubService.ProblemaNoNome(FormNome);
    public bool TemProblemaNoNome => ProblemaNoNome.Length > 0;
    public bool PodeSalvar => !Executando && FormNome.Trim().Length > 0 && GitHubService.ProblemaNoNome(FormNome).Length == 0;

    partial void OnFormNomeChanged(string value)
    {
        foreach (var p in new[] { nameof(ProblemaNoNome), nameof(TemProblemaNoNome), nameof(PodeSalvar) })
            OnPropertyChanged(p);
    }

    private void AbrirForm(bool segredo, bool existe, string nome, string valor)
    {
        _formSegredo = segredo;
        _formExiste = existe;
        FormNome = nome;
        FormValor = valor;
        Editando = true;
        foreach (var p in new[] { nameof(FormCabecalho), nameof(NomeEditavel), nameof(FormEhSegredo) })
            OnPropertyChanged(p);
    }

    [RelayCommand] private void NovoSegredo() => AbrirForm(true, false, "", "");
    [RelayCommand] private void NovaVariavel() => AbrirForm(false, false, "", "");

    [RelayCommand]
    private void TrocarSegredo()
    {
        if (SegredoSelecionado is { } s) AbrirForm(true, true, s.Nome, "");
    }

    [RelayCommand]
    private void EditarVariavel()
    {
        if (VariavelSelecionada is { } v) AbrirForm(false, true, v.Nome, v.Item.Valor);
    }

    [RelayCommand]
    private void CancelarForm() => Editando = false;

    [RelayCommand]
    private async Task SalvarAsync()
    {
        if (!PodeSalvar) return;
        var nome = FormNome.Trim().ToUpperInvariant();

        // criar com o nome de um que já existe sobrescreveria sem avisar
        var lista = _formSegredo ? Segredos : Variaveis;
        if (!_formExiste && lista.Any(i => i.Nome == nome) && Confirmar is not null &&
            !await Confirmar("Já existe", $"{nome} já existe. Trocar o valor?"))
            return;

        await ExecutarAsync(async () =>
        {
            if (_formSegredo)
                await GitHubService.GravarSegredoAsync(_slug, nome, FormValor, _usuario);
            else
                await GitHubService.GravarVariavelAsync(_slug, nome, FormValor,
                    _formExiste || Variaveis.Any(v => v.Nome == nome), _usuario);

            Editando = false;
            await RecarregarAsync();
        });
    }

    [RelayCommand]
    private async Task ExcluirSegredoAsync()
    {
        if (SegredoSelecionado is not { } s) return;
        if (Confirmar is not null && !await Confirmar("Excluir segredo",
                $"Excluir o segredo {s.Nome}?\n\nWorkflows que usam ${{{{ secrets.{s.Nome} }}}} passam a receber vazio."))
            return;

        await ExecutarAsync(async () =>
        {
            await GitHubService.ExcluirSegredoAsync(_slug, s.Nome, _usuario);
            await RecarregarAsync();
        });
    }

    [RelayCommand]
    private async Task ExcluirVariavelAsync()
    {
        if (VariavelSelecionada is not { } v) return;
        if (Confirmar is not null && !await Confirmar("Excluir variável",
                $"Excluir a variável {v.Nome}?\n\nWorkflows que usam ${{{{ vars.{v.Nome} }}}} passam a receber vazio."))
            return;

        await ExecutarAsync(async () =>
        {
            await GitHubService.ExcluirVariavelAsync(_slug, v.Nome, _usuario);
            await RecarregarAsync();
        });
    }

    private async Task RecarregarAsync()
    {
        var segredos = await GitHubService.SegredosAsync(_slug, _usuario);
        var variaveis = await GitHubService.VariaveisAsync(_slug, _usuario);
        Aplicar(segredos, variaveis);
    }

    private async Task ExecutarAsync(Func<Task> acao)
    {
        Executando = true;
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
            Executando = false;
        }
    }

    [RelayCommand]
    private void AbrirNoGitHub()
    {
        try
        {
            ShellService.AbrirUrl($"https://github.com/{_slug}/settings/secrets/actions");
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
