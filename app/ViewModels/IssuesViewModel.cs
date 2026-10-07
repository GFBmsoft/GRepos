using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Uma etiqueta como filtro: o nome, a cor e quantas issues da lista a têm.</summary>
public sealed partial class EtiquetaViewModel : ObservableObject
{
    public Etiqueta Etiqueta { get; init; } = new("", "");
    public int Quantas { get; init; }

    [ObservableProperty] private bool _ativa;

    public string Nome => Etiqueta.Nome;

    /// <summary>Etiqueta sem cor (ou com cor ilegível) usa a do texto apagado do tema.</summary>
    public string Cor => Etiqueta.Cor.Length == 7 ? Etiqueta.Cor : "TextDim";
    public string Texto => Quantas > 0 ? $"{Nome} {Quantas}" : Nome;
}

public sealed class IssueViewModel
{
    public Issue Issue { get; init; } = new();

    public string Numero => "#" + Issue.Numero;
    public string Titulo => Issue.Titulo.Length > 0 ? Issue.Titulo : "(sem título)";
    public string EstadoCor => Issue.Aberta ? "Green" : "Purple";
    public string Corpo => Issue.Corpo.Trim().Length > 0 ? Issue.Corpo : "*Sem descrição.*";

    public IReadOnlyList<EtiquetaViewModel> Etiquetas =>
        Issue.Etiquetas.Select(e => new EtiquetaViewModel { Etiqueta = e }).ToList();

    public bool TemEtiquetas => Issue.Etiquetas.Count > 0;

    public string Rodape
    {
        get
        {
            var partes = new List<string>();
            if (Issue.Autor.Length > 0) partes.Add(Issue.Autor);

            var quando = Rotulos.Quando(Issue.Atualizada);
            if (quando.Length > 0) partes.Add(quando);
            if (Issue.Comentarios > 0)
                partes.Add(Issue.Comentarios == 1 ? "1 comentário" : $"{Issue.Comentarios} comentários");
            return string.Join(" · ", partes);
        }
    }

    public string Responsaveis => Issue.Responsaveis.Count > 0
        ? "com " + string.Join(", ", Issue.Responsaveis)
        : "sem responsável";

    public string Tooltip => $"{Numero} {Titulo}\n{Rodape}";
}

/// <summary>
/// Issues do repositório: abertas ou fechadas, filtradas por etiqueta e por texto, com a
/// descrição e os comentários da escolhida. Dá para criar, editar, comentar, fechar e
/// reabrir, como na página do GitHub.
/// </summary>
public sealed partial class IssuesViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _usuario;

    /// <summary>As duas listas, guardadas depois da primeira consulta: trocar de aba não refaz a chamada.</summary>
    private List<Issue>? _abertas;
    private List<Issue>? _fechadas;

    public IssuesViewModel(string slug, string usuario, string repoNome = "")
    {
        _slug = slug;
        _usuario = usuario;
        Title = repoNome.Length > 0 ? $"Issues — {repoNome}" : "Issues";
        Subtitulo = slug;

        Editor = new ConversaViewModel(slug, usuario)
        {
            AoFalhar = mensagem => Erro = mensagem,
            AoMudar = DepoisDeComentarAsync,
        };
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversaViewModel.Novo)) OnPropertyChanged(nameof(EstadoRotulo));
        };
    }

    public string Title { get; }
    public string Subtitulo { get; }

    [ObservableProperty] private ObservableCollection<IssueViewModel> _lista = new();
    [ObservableProperty] private IssueViewModel? _selecionada;
    [ObservableProperty] private ObservableCollection<EtiquetaViewModel> _etiquetas = new();
    /// <summary>A conversa da issue escolhida e o que se faz nela (comentar, editar, excluir).</summary>
    public ConversaViewModel Editor { get; }

    public ObservableCollection<ComentarioViewModel> Comentarios => Editor.Itens;
    [ObservableProperty] private bool _mostrarFechadas;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private bool _carregandoComentarios;
    [ObservableProperty] private string _erro = "";

    /// <summary>Etiquetas marcadas no filtro; a issue precisa ter todas.</summary>
    private readonly HashSet<string> _filtro = new(StringComparer.OrdinalIgnoreCase);

    public bool TemErro => Erro.Length > 0;
    public bool SemSelecao => !Editando && Selecionada is null && Lista.Count > 0;
    public bool TemSelecao => Selecionada is not null;
    public bool MostraDetalhe => !Editando && Selecionada is not null;
    public bool TemEtiquetas => Etiquetas.Count > 0;
    public bool Abertas => !MostrarFechadas;

    public string AbertasRotulo => _abertas is null ? "Abertas" : $"Abertas {_abertas.Count}";
    public string FechadasRotulo => _fechadas is null ? "Fechadas" : $"Fechadas {_fechadas.Count}";

    public bool Vazio => !Carregando && Erro.Length == 0 && Lista.Count == 0;

    public string TextoVazio
    {
        get
        {
            var todas = MostrarFechadas ? _fechadas : _abertas;
            if (todas is { Count: > 0 }) return "Nenhuma issue com esse filtro.";
            return MostrarFechadas ? "Nenhuma issue fechada." : "Nenhuma issue aberta.";
        }
    }

    public bool SemComentarios => !CarregandoComentarios && Selecionada is not null && Comentarios.Count == 0;

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(Vazio));
    }

    partial void OnCarregandoChanged(bool value) => OnPropertyChanged(nameof(Vazio));
    partial void OnCarregandoComentariosChanged(bool value) => OnPropertyChanged(nameof(SemComentarios));
    partial void OnBuscaChanged(string value) => Filtrar();

    partial void OnMostrarFechadasChanged(bool value)
    {
        OnPropertyChanged(nameof(Abertas));
        _filtro.Clear(); // as etiquetas das fechadas não são as das abertas
        _ = CarregarAsync();
    }

    partial void OnSelecionadaChanged(IssueViewModel? value)
    {
        // a mesma issue de volta (a lista foi refeita por um filtro ou uma recarga): a
        // conversa e o que estava sendo digitado ficam
        var numero = value?.Issue.Numero ?? 0;
        var mesma = numero != 0 && numero == Editor.Numero;
        if (!mesma)
        {
            Editor.Limpar();
            Editor.Numero = numero;
            Editando = false;
        }
        foreach (var p in new[] { nameof(SemSelecao), nameof(TemSelecao), nameof(SemComentarios),
                                  nameof(MostraDetalhe), nameof(EstadoRotulo), nameof(PodeMudarEstado) })
            OnPropertyChanged(p);

        if (value is not null && !mesma) _ = CarregarComentariosAsync(value);
    }

    [RelayCommand] private void VerAbertas() => MostrarFechadas = false;
    [RelayCommand] private void VerFechadas() => MostrarFechadas = true;

    /// <summary>Clicar numa etiqueta liga ou desliga o filtro por ela.</summary>
    [RelayCommand]
    private void AlternarEtiqueta(EtiquetaViewModel? etiqueta)
    {
        if (etiqueta is null) return;
        if (!_filtro.Add(etiqueta.Nome)) _filtro.Remove(etiqueta.Nome);
        Filtrar();
    }

    /// <summary>Busca do GitHub de novo, esquecendo o que estava guardado.</summary>
    [RelayCommand]
    private Task Atualizar()
    {
        _abertas = null;
        _fechadas = null;
        return CarregarAsync();
    }

    public async Task CarregarAsync()
    {
        if (Carregando) return;

        var fechadas = MostrarFechadas;
        if (_slug.Length > 0 && (fechadas ? _fechadas : _abertas) is null)
        {
            Carregando = true;
            Erro = "";
            try
            {
                var lista = await GitHubService.IssuesAsync(_slug, !fechadas, _usuario);
                if (fechadas) _fechadas = lista;
                else _abertas = lista;
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

        if (fechadas == MostrarFechadas) Filtrar();
    }

    /// <summary>Recebe as listas direto; separado da rede para ser testado.</summary>
    public void Aplicar(IReadOnlyList<Issue> abertas, IReadOnlyList<Issue>? fechadas = null)
    {
        _abertas = abertas.ToList();
        if (fechadas is not null) _fechadas = fechadas.ToList();
        Filtrar();
    }

    private void Filtrar()
    {
        var todas = (MostrarFechadas ? _fechadas : _abertas) ?? new List<Issue>();
        var q = Busca.Trim();

        // as etiquetas e as contagens saem da lista inteira, não da filtrada: senão
        // marcar uma faria as outras sumirem e não haveria como combinar
        Etiquetas = new ObservableCollection<EtiquetaViewModel>(todas
            .SelectMany(i => i.Etiquetas)
            .GroupBy(e => e.Nome, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new EtiquetaViewModel
            {
                Etiqueta = g.First(), Quantas = g.Count(), Ativa = _filtro.Contains(g.Key),
            }));

        bool Combina(Issue i) =>
            _filtro.All(f => i.Etiquetas.Any(e => e.Nome.Equals(f, StringComparison.OrdinalIgnoreCase))) &&
            (q.Length == 0 ||
             i.Titulo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
             i.Autor.Contains(q, StringComparison.OrdinalIgnoreCase) ||
             ("#" + i.Numero).Contains(q, StringComparison.OrdinalIgnoreCase));

        var escolhida = Selecionada?.Issue.Numero;
        Lista = new ObservableCollection<IssueViewModel>(
            todas.Where(Combina).Select(i => new IssueViewModel { Issue = i }));
        Selecionada = Lista.FirstOrDefault(i => i.Issue.Numero == escolhida) ?? Lista.FirstOrDefault();

        foreach (var p in new[] { nameof(Vazio), nameof(TextoVazio), nameof(TemEtiquetas), nameof(SemSelecao),
                                  nameof(AbertasRotulo), nameof(FechadasRotulo) })
            OnPropertyChanged(p);
    }

    private async Task CarregarComentariosAsync(IssueViewModel item)
    {
        if (_slug.Length == 0 || item.Issue.Comentarios == 0) return;

        CarregandoComentarios = true;
        try
        {
            var comentarios = await GitHubService.ComentariosDaIssueAsync(_slug, item.Issue.Numero, _usuario);
            if (!ReferenceEquals(Selecionada, item)) return; // trocou de issue no meio

            Editor.Aplicar(comentarios);
            OnPropertyChanged(nameof(SemComentarios));
        }
        catch (Exception e)
        {
            if (ReferenceEquals(Selecionada, item)) Erro = e.Message;
        }
        finally
        {
            CarregandoComentarios = false;
        }
    }

    // --------------------------------------- escrever: criar, editar, fechar

    /// <summary>O número de comentários da linha acompanha o que foi escrito.</summary>
    private async Task DepoisDeComentarAsync()
    {
        if (Selecionada is not { } item) return;
        await RecarregarAsync(item.Issue.Numero);
    }

    /// <summary>Busca as listas de novo e volta para a issue em que se estava.</summary>
    private async Task RecarregarAsync(int numero)
    {
        _abertas = null;
        _fechadas = null;
        if (_slug.Length == 0) return;

        var rascunho = Editor.Novo;
        await CarregarAsync();

        var mesma = Lista.FirstOrDefault(i => i.Issue.Numero == numero);
        if (mesma is not null) Selecionada = mesma;
        if (Selecionada?.Issue.Numero == numero && Editor.Novo.Length == 0) Editor.Novo = rascunho;
    }

    [ObservableProperty] private bool _executando;

    partial void OnExecutandoChanged(bool value)
    {
        foreach (var p in new[] { nameof(PodeMudarEstado), nameof(PodeSalvar) })
            OnPropertyChanged(p);
    }

    public bool PodeMudarEstado => !Executando && Selecionada is not null;

    /// <summary>Como no GitHub: com texto na caixa, fechar também publica o comentário.</summary>
    public string EstadoRotulo
    {
        get
        {
            if (Selecionada is not { } item) return "Fechar issue";
            var comTexto = Editor.Novo.Trim().Length > 0;
            return item.Issue.Aberta
                ? comTexto ? "Comentar e fechar" : "Fechar issue"
                : comTexto ? "Comentar e reabrir" : "Reabrir issue";
        }
    }

    [RelayCommand]
    private async Task MudarEstadoAsync()
    {
        if (Selecionada is not { } item || !PodeMudarEstado) return;

        Executando = true;
        Erro = "";
        try
        {
            if (Editor.Novo.Trim().Length > 0 && !await Editor.ComentarAsync()) return;

            await GitHubService.MudarEstadoDaIssueAsync(_slug, item.Issue.Numero, abrir: !item.Issue.Aberta, _usuario);

            // ela mudou de aba; a tela fica onde está, na próxima da lista
            _abertas = null;
            _fechadas = null;
            await CarregarAsync();
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

    // o painel da direita vira formulário: issue nova, ou a escolhida em edição

    [ObservableProperty] private bool _editando;
    [ObservableProperty] private string _formTitulo = "";
    [ObservableProperty] private string _formCorpo = "";
    [ObservableProperty] private ObservableCollection<EtiquetaViewModel> _formEtiquetas = new();

    /// <summary>Zero é issue nova; senão, o número da que está sendo editada.</summary>
    private int _formNumero;

    /// <summary>As etiquetas do repositório, buscadas uma vez só.</summary>
    private List<Etiqueta>? _etiquetasDoRepo;

    public string FormCabecalho => _formNumero == 0 ? "NOVA ISSUE" : $"EDITAR ISSUE #{_formNumero}";
    public string SalvarRotulo => _formNumero == 0 ? "Criar issue" : "Salvar";
    public bool PodeSalvar => !Executando && FormTitulo.Trim().Length > 0;
    public bool TemFormEtiquetas => FormEtiquetas.Count > 0;

    partial void OnFormTituloChanged(string value) => OnPropertyChanged(nameof(PodeSalvar));

    partial void OnEditandoChanged(bool value)
    {
        foreach (var p in new[] { nameof(MostraDetalhe), nameof(SemSelecao) })
            OnPropertyChanged(p);
    }

    private async Task AbrirFormAsync(int numero, string titulo, string corpo, IReadOnlyCollection<string> marcadas)
    {
        _formNumero = numero;
        FormTitulo = titulo;
        FormCorpo = corpo;
        Editando = true;
        OnPropertyChanged(nameof(FormCabecalho));
        OnPropertyChanged(nameof(SalvarRotulo));

        if (_etiquetasDoRepo is null && _slug.Length > 0)
        {
            try
            {
                _etiquetasDoRepo = await GitHubService.EtiquetasDoRepoAsync(_slug, _usuario);
            }
            catch (Exception)
            {
                // sem a lista do repositório ficam as etiquetas que as issues já usam
            }
        }

        MontarFormEtiquetas(marcadas);
    }

    /// <summary>As etiquetas do formulário; separado da rede para ser testado.</summary>
    public void MontarFormEtiquetas(IReadOnlyCollection<string> marcadas, IReadOnlyList<Etiqueta>? doRepo = null)
    {
        if (doRepo is not null) _etiquetasDoRepo = doRepo.ToList();

        var conhecidas = _etiquetasDoRepo ?? (_abertas ?? new List<Issue>())
            .Concat(_fechadas ?? new List<Issue>())
            .SelectMany(i => i.Etiquetas)
            .GroupBy(e => e.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FormEtiquetas = new ObservableCollection<EtiquetaViewModel>(conhecidas.Select(e => new EtiquetaViewModel
        {
            Etiqueta = e,
            Ativa = marcadas.Contains(e.Nome, StringComparer.OrdinalIgnoreCase),
        }));
        OnPropertyChanged(nameof(TemFormEtiquetas));
    }

    [RelayCommand]
    private Task Nova() => AbrirFormAsync(0, "", "", Array.Empty<string>());

    [RelayCommand]
    private Task EditarIssue() => Selecionada is { } item
        ? AbrirFormAsync(item.Issue.Numero, item.Issue.Titulo, item.Issue.Corpo,
            item.Issue.Etiquetas.Select(e => e.Nome).ToList())
        : Task.CompletedTask;

    [RelayCommand]
    private void CancelarForm() => Editando = false;

    [RelayCommand]
    private void AlternarEtiquetaDoForm(EtiquetaViewModel? etiqueta)
    {
        if (etiqueta is not null) etiqueta.Ativa = !etiqueta.Ativa;
    }

    [RelayCommand]
    private async Task SalvarFormAsync()
    {
        if (!PodeSalvar) return;

        Executando = true;
        Erro = "";
        try
        {
            var etiquetas = FormEtiquetas.Where(e => e.Ativa).Select(e => e.Nome).ToList();
            var salva = _formNumero == 0
                ? await GitHubService.CriarIssueAsync(_slug, FormTitulo, FormCorpo, etiquetas, _usuario)
                : await GitHubService.EditarIssueAsync(_slug, _formNumero, FormTitulo, FormCorpo, etiquetas, _usuario);

            Editando = false;

            // a nova nasce aberta: é nessa aba que ela aparece
            if (_formNumero == 0 && MostrarFechadas)
            {
                _abertas = null;
                MostrarFechadas = false; // já recarrega
            }
            else
            {
                await RecarregarAsync(salva?.Numero ?? _formNumero);
            }

            if (salva is not null && Lista.FirstOrDefault(i => i.Issue.Numero == salva.Numero) is { } linha)
                Selecionada = linha;
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
            var url = Selecionada?.Issue.Url;
            ShellService.AbrirUrl(string.IsNullOrEmpty(url) ? $"https://github.com/{_slug}/issues" : url);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
