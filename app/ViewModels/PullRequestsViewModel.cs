using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>O que a janela de pull requests precisa saber do repositório aberto.</summary>
public sealed record ContextoPr(string Slug, string Branch, string Usuario, string RepoNome)
{
    /// <summary>Branches do remoto, sem o "origin/": os destinos possíveis de um PR novo.</summary>
    public IReadOnlyList<string> Destinos { get; init; } = Array.Empty<string>();

    /// <summary>Assunto do último commit: é o título que o GitHub também sugere.</summary>
    public string TituloSugerido { get; init; } = "";

    /// <summary>A branch já tem par no remoto. Sem isso o GitHub recusa o PR.</summary>
    public bool BranchEnviada { get; init; } = true;

    /// <summary>Commits locais que ainda não foram enviados e ficariam fora do PR.</summary>
    public int NaoEnviados { get; init; }

    /// <summary>
    /// Destino já escolhido por quem abriu — soltar uma branch sobre outra. Com ele a
    /// janela abre direto no formulário de PR novo.
    /// </summary>
    public string DestinoInicial { get; init; } = "";
}

/// <summary>Forma de mesclar, com o nome que o GitHub mostra no botão.</summary>
public sealed record MetodoDeMesclagem(string Id, string Nome)
{
    public static readonly IReadOnlyList<MetodoDeMesclagem> Todos = new[]
    {
        new MetodoDeMesclagem("merge", "Commit de merge"),
        new MetodoDeMesclagem("squash", "Squash (um commit só)"),
        new MetodoDeMesclagem("rebase", "Rebase"),
    };

    public override string ToString() => Nome;
}

public sealed class VerificacaoViewModel
{
    public Verificacao Verificacao { get; init; } = new();

    public string Nome => Verificacao.Nome;
    public string Simbolo => CiVisual.Simbolo(Verificacao.Situacao);
    public string Cor => CiVisual.Cor(Verificacao.Situacao);
    public string Texto => CiVisual.Texto(Verificacao.Situacao);
}

/// <summary>Um pull request na lista da esquerda.</summary>
public sealed partial class PrViewModel : ObservableObject
{
    [ObservableProperty] private PullRequest _pr = new();

    /// <summary>Resumo das verificações; vazio enquanto não foi consultado.</summary>
    [ObservableProperty] private string _ciSituacao = "";

    public string Numero => "#" + Pr.Numero;
    public string Titulo => Pr.Titulo.Length > 0 ? Pr.Titulo : "(sem título)";

    public string Estado => Pr.Rascunho && Pr.Aberto ? "rascunho" : Pr.Estado;

    public string EstadoCor => Estado switch
    {
        "aberto" => "Green",
        "mesclado" => "Purple",
        "fechado" => "Red",
        _ => "TextDim",
    };

    public string Caminho => Pr.Origem.Length > 0 ? $"{Pr.Origem} → {Pr.Destino}" : "";

    public string Rodape
    {
        get
        {
            var partes = new List<string>();
            if (Pr.Autor.Length > 0) partes.Add(Pr.Autor);

            var quando = Rotulos.Quando(Pr.Atualizado);
            if (quando.Length > 0) partes.Add(quando);
            return string.Join(" · ", partes);
        }
    }

    public bool TemCi => CiSituacao.Length > 0 && CiSituacao != "nenhum";
    public string CiSimbolo => CiVisual.Simbolo(CiSituacao);
    public string CiCor => CiVisual.Cor(CiSituacao);

    public string Tooltip => $"{Numero} {Titulo}\n{Caminho}\n{Rodape}";

    partial void OnPrChanged(PullRequest value) => OnPropertyChanged(string.Empty);

    partial void OnCiSituacaoChanged(string value)
    {
        foreach (var p in new[] { nameof(TemCi), nameof(CiSimbolo), nameof(CiCor) })
            OnPropertyChanged(p);
    }
}

/// <summary>
/// Pull requests do repositório dentro do app: a lista, o detalhe com as verificações,
/// criar o PR da branch atual e mesclar. Tudo que escreve no GitHub confirma antes,
/// como na esteira.
/// </summary>
public sealed partial class PullRequestsViewModel : ObservableObject
{
    private readonly ContextoPr _ctx;
    private readonly IDialogService? _dialogs;

    public PullRequestsViewModel(ContextoPr contexto, IDialogService? dialogs = null)
    {
        _ctx = contexto;
        _dialogs = dialogs;

        Title = contexto.RepoNome.Length > 0 ? $"Pull requests — {contexto.RepoNome}" : "Pull requests";
        Subtitulo = contexto.Slug;
        NovoTitulo = contexto.TituloSugerido;
        Destinos = new ObservableCollection<string>(contexto.Destinos.Where(d => d != contexto.Branch));
        NovoDestino = Destinos.FirstOrDefault() ?? "";

        if (contexto.DestinoInicial.Length > 0 && contexto.DestinoInicial != contexto.Branch)
        {
            if (!Destinos.Contains(contexto.DestinoInicial)) Destinos.Insert(0, contexto.DestinoInicial);
            NovoDestino = contexto.DestinoInicial;
            Criando = true;
        }
    }

    public string Title { get; }
    public string Subtitulo { get; }
    public string Branch => _ctx.Branch;

    [ObservableProperty] private ObservableCollection<PrViewModel> _lista = new();
    [ObservableProperty] private PrViewModel? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private bool _executando;
    [ObservableProperty] private string _erro = "";

    /// <summary>Desmarcado, só os abertos — que são os que pedem alguma ação.</summary>
    [ObservableProperty] private bool _mostrarFechados;

    public bool TemErro => Erro.Length > 0;
    public bool Vazio => !Carregando && Erro.Length == 0 && Lista.Count == 0;
    public string TextoVazio => MostrarFechados ? "Nenhum pull request neste repositório." : "Nenhum pull request aberto.";

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(Vazio));
    }

    partial void OnCarregandoChanged(bool value) => OnPropertyChanged(nameof(Vazio));

    partial void OnMostrarFechadosChanged(bool value)
    {
        OnPropertyChanged(nameof(TextoVazio));
        _ = CarregarAsync();
    }

    partial void OnExecutandoChanged(bool value) => AvisarBotoes();

    // ------------------------------------------------------------- detalhe

    /// <summary>O PR escolhido com o que só a consulta individual traz.</summary>
    [ObservableProperty] private PullRequest? _detalhe;
    [ObservableProperty] private ObservableCollection<VerificacaoViewModel> _verificacoes = new();
    [ObservableProperty] private bool _carregandoDetalhe;
    [ObservableProperty] private MetodoDeMesclagem _metodo = MetodoDeMesclagem.Todos[0];

    public IReadOnlyList<MetodoDeMesclagem> Metodos => MetodoDeMesclagem.Todos;

    public bool MostraDetalhe => !Criando && Selecionada is not null;
    public bool SemSelecao => !Criando && Selecionada is null && Lista.Count > 0;

    public string DetalheTitulo => Selecionada is null ? "" : $"{Selecionada.Titulo} {Selecionada.Numero}";
    public string DetalheCaminho => Selecionada?.Caminho ?? "";
    public string DetalheCorpo => (Detalhe ?? Selecionada?.Pr)?.Corpo is { Length: > 0 } c ? c : "*Sem descrição.*";

    public string DetalheResumo
    {
        get
        {
            if (Detalhe is null) return Selecionada?.Rodape ?? "";

            var partes = new List<string>
            {
                Detalhe.Commits == 1 ? "1 commit" : $"{Detalhe.Commits} commits",
                Detalhe.Arquivos == 1 ? "1 arquivo" : $"{Detalhe.Arquivos} arquivos",
                $"+{Detalhe.Adicionadas} −{Detalhe.Removidas}",
            };
            if (Selecionada?.Rodape is { Length: > 0 } r) partes.Add(r);
            return string.Join(" · ", partes);
        }
    }

    /// <summary>A situação de mesclagem em português, que é o que decide o botão Mesclar.</summary>
    public string MesclagemTexto => SituacaoDeMesclagem(Selecionada?.Pr, Detalhe).Texto;
    public string MesclagemCor => SituacaoDeMesclagem(Selecionada?.Pr, Detalhe).Cor;

    public static (string Texto, string Cor, bool Mesclavel) SituacaoDeMesclagem(PullRequest? item, PullRequest? detalhe)
    {
        if (item is null) return ("", "TextDim", false);
        if (item.Estado == "mesclado") return ("Mesclado", "Purple", false);
        if (item.Estado == "fechado") return ("Fechado sem mesclar", "Red", false);
        if (item.Rascunho) return ("Rascunho: marque como pronto no GitHub para mesclar", "TextDim", false);

        return (detalhe?.Mesclagem ?? "") switch
        {
            "clean" or "has_hooks" => ("Pronto para mesclar", "Green", true),
            "dirty" => ("Em conflito com o destino: resolva na branch antes de mesclar", "Red", false),
            "blocked" => ("Bloqueado pelas regras da branch (revisão ou verificação obrigatória)", "Yellow", false),
            "behind" => ("Atrás do destino: pode mesclar, mas vale atualizar a branch antes", "Yellow", true),
            "unstable" => ("Verificações quebradas ou ainda rodando", "Yellow", true),
            "" => ("Consultando a situação…", "TextDim", false),
            _ => ("O GitHub ainda está calculando se dá para mesclar", "TextDim", true),
        };
    }

    public bool TemVerificacoes => Verificacoes.Count > 0;

    public bool PodeMesclar => !Executando && SituacaoDeMesclagem(Selecionada?.Pr, Detalhe).Mesclavel;
    public bool PodeFechar => !Executando && Selecionada is { Pr.Aberto: true };

    private void AvisarBotoes()
    {
        foreach (var p in new[] { nameof(PodeMesclar), nameof(PodeFechar), nameof(PodeCriar), nameof(AvisoNovo) })
            OnPropertyChanged(p);
    }

    private void AvisarDetalhe()
    {
        foreach (var p in new[] { nameof(MostraDetalhe), nameof(SemSelecao), nameof(DetalheTitulo),
                                  nameof(DetalheCaminho), nameof(DetalheCorpo), nameof(DetalheResumo),
                                  nameof(MesclagemTexto), nameof(MesclagemCor), nameof(TemVerificacoes) })
            OnPropertyChanged(p);
        AvisarBotoes();
    }

    partial void OnDetalheChanged(PullRequest? value) => AvisarDetalhe();

    partial void OnSelecionadaChanged(PrViewModel? value)
    {
        // o detalhe na tela é de outro PR: não pode ficar por baixo do novo
        Detalhe = null;
        Verificacoes.Clear();
        AvisarDetalhe();

        if (value is not null)
        {
            Criando = false;
            _ = CarregarDetalheAsync(value);
        }
    }

    private async Task CarregarDetalheAsync(PrViewModel item)
    {
        if (_ctx.Slug.Length == 0) return;

        CarregandoDetalhe = true;
        try
        {
            var detalhe = await GitHubService.PullRequestAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario);
            if (!ReferenceEquals(Selecionada, item)) return; // trocou de PR no meio

            // o GitHub calcula a mesclagem em segundo plano: a primeira consulta de um PR
            // recém-criado costuma voltar "unknown", e a segunda já traz a resposta
            if (detalhe is { Mesclagem: "unknown" or "" } && item.Pr.Aberto)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (!ReferenceEquals(Selecionada, item)) return;
                detalhe = await GitHubService.PullRequestAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario) ?? detalhe;
                if (!ReferenceEquals(Selecionada, item)) return;
            }

            var verificacoes = await GitHubService.VerificacoesAsync(
                _ctx.Slug, detalhe?.Sha ?? item.Pr.Sha, _ctx.Usuario);
            if (!ReferenceEquals(Selecionada, item)) return;

            Verificacoes = new ObservableCollection<VerificacaoViewModel>(
                verificacoes.Select(v => new VerificacaoViewModel { Verificacao = v }));
            item.CiSituacao = GitHubService.ResumoDasVerificacoes(verificacoes);
            Detalhe = detalhe;
        }
        catch (Exception e)
        {
            if (ReferenceEquals(Selecionada, item)) Erro = e.Message;
        }
        finally
        {
            CarregandoDetalhe = false;
            AvisarDetalhe();
        }
    }

    // ---------------------------------------------------------------- lista

    /// <summary>A janela chama ao abrir.</summary>
    public async Task IniciarAsync()
    {
        await CarregarAsync();
        await SugerirDestinoAsync();
    }

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (_ctx.Slug.Length == 0 || Carregando) return;

        Carregando = true;
        Erro = "";
        try
        {
            var lista = await GitHubService.ListarPullRequestsAsync(_ctx.Slug, _ctx.Usuario, !MostrarFechados);
            Aplicar(lista);
            _ = CarregarVerificacoesAsync();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Carregando = false;
            OnPropertyChanged(nameof(Vazio));
        }
    }

    /// <summary>
    /// Atualiza a lista no lugar, como a esteira: recriar as linhas levaria junto o PR
    /// selecionado e a rolagem.
    /// </summary>
    public void Aplicar(IReadOnlyList<PullRequest> prs)
    {
        ListaSync.AplicarModelos(
            Lista, prs,
            item => item.Pr.Numero.ToString(),
            modelo => modelo.Numero.ToString(),
            (item, modelo) => item.Pr = modelo,
            modelo => new PrViewModel { Pr = modelo });

        if (Selecionada is not null && !Lista.Contains(Selecionada)) Selecionada = null;
        if (!Criando) Selecionada ??= Lista.FirstOrDefault(p => p.Pr.Origem == _ctx.Branch) ?? Lista.FirstOrDefault();

        OnPropertyChanged(nameof(Vazio));
        OnPropertyChanged(nameof(PrDaBranch));
        AvisarDetalhe();
    }

    /// <summary>O símbolo de verificação de cada PR aberto, em fila curta para não gastar a cota.</summary>
    private async Task CarregarVerificacoesAsync()
    {
        using var vagas = new SemaphoreSlim(4);
        await Task.WhenAll(Lista.Where(p => p.Pr.Aberto && p.CiSituacao.Length == 0).ToList().Select(async item =>
        {
            await vagas.WaitAsync();
            try
            {
                var v = await GitHubService.VerificacoesAsync(_ctx.Slug, item.Pr.Sha, _ctx.Usuario);
                item.CiSituacao = GitHubService.ResumoDasVerificacoes(v);
            }
            catch (Exception)
            {
                // o símbolo é um extra: sem ele a linha só fica sem a marca
            }
            finally
            {
                vagas.Release();
            }
        }));
    }

    // --------------------------------------------------------------- criar

    /// <summary>O painel da direita mostra o formulário de PR novo em vez do detalhe.</summary>
    [ObservableProperty] private bool _criando;

    [ObservableProperty] private string _novoTitulo = "";
    [ObservableProperty] private string _novoCorpo = "";
    [ObservableProperty] private string _novoDestino = "";
    [ObservableProperty] private bool _novoRascunho;

    public ObservableCollection<string> Destinos { get; }

    /// <summary>PR aberto que já sai da branch atual: não dá para criar outro igual.</summary>
    public PrViewModel? PrDaBranch =>
        _ctx.Branch.Length == 0 ? null : Lista.FirstOrDefault(p => p.Pr.Aberto && p.Pr.Origem == _ctx.Branch);

    public string NovoCaminho => $"{(_ctx.Branch.Length > 0 ? _ctx.Branch : "—")} →";

    /// <summary>O que impede de criar, ou o que convém saber antes. Vazio quando está tudo certo.</summary>
    public string AvisoNovo
    {
        get
        {
            if (_ctx.Branch.Length == 0) return "Sem branch atual (HEAD solto): não há de onde abrir o pull request.";
            if (!_ctx.BranchEnviada) return $"A branch {_ctx.Branch} ainda não está no GitHub. Use Enviar e abra esta janela de novo.";
            if (PrDaBranch is { } pr) return $"Esta branch já tem um pull request aberto: {pr.Numero} {pr.Titulo}.";
            if (NovoDestino.Length == 0) return "Escolha a branch de destino.";
            if (NovoDestino == _ctx.Branch) return "A origem e o destino são a mesma branch.";
            if (NovoTitulo.Trim().Length == 0) return "Informe o título.";
            if (_ctx.NaoEnviados > 0)
                return _ctx.NaoEnviados == 1
                    ? "Há 1 commit local ainda não enviado, que fica fora do pull request."
                    : $"Há {_ctx.NaoEnviados} commits locais ainda não enviados, que ficam fora do pull request.";
            return "";
        }
    }

    public bool TemAvisoNovo => AvisoNovo.Length > 0;

    public bool PodeCriar =>
        !Executando && _ctx.Branch.Length > 0 && _ctx.BranchEnviada && PrDaBranch is null &&
        NovoDestino.Length > 0 && NovoDestino != _ctx.Branch && NovoTitulo.Trim().Length > 0;

    private void AvisarNovo()
    {
        foreach (var p in new[] { nameof(AvisoNovo), nameof(TemAvisoNovo), nameof(PodeCriar) })
            OnPropertyChanged(p);
    }

    partial void OnNovoTituloChanged(string value) => AvisarNovo();
    partial void OnNovoDestinoChanged(string value) => AvisarNovo();

    partial void OnCriandoChanged(bool value)
    {
        if (value) Selecionada = null;
        else Selecionada ??= Lista.FirstOrDefault();

        AvisarDetalhe();
        AvisarNovo();
    }

    [RelayCommand]
    private void Novo() => Criando = true;

    [RelayCommand]
    private void CancelarNovo() => Criando = false;

    /// <summary>
    /// O destino sugerido é a branch padrão do GitHub — num repositório em git-flow ela
    /// costuma ser a develop, e é para lá que os PRs vão.
    /// </summary>
    private async Task SugerirDestinoAsync()
    {
        // quem abriu já disse o destino: a sugestão não passa por cima
        if (_ctx.Slug.Length == 0 || _ctx.DestinoInicial.Length > 0) return;

        try
        {
            var padrao = await GitHubService.BranchPadraoAsync(_ctx.Slug, _ctx.Usuario);
            if (padrao.Length == 0 || padrao == _ctx.Branch) return;

            if (!Destinos.Contains(padrao)) Destinos.Insert(0, padrao);
            NovoDestino = padrao;
        }
        catch (Exception)
        {
            // sem a branch padrão fica o primeiro destino da lista
        }
    }

    [RelayCommand]
    private async Task CriarAsync()
    {
        if (!PodeCriar) return;

        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                "Criar pull request",
                $"Criar o pull request \"{NovoTitulo.Trim()}\"\nde {_ctx.Branch} para {NovoDestino}, em {_ctx.Slug}?" +
                (NovoRascunho ? "\n\nEle nasce como rascunho." : "")))
            return;

        Executando = true;
        Erro = "";
        try
        {
            var criado = await GitHubService.CriarPullRequestAsync(
                _ctx.Slug, NovoTitulo, NovoCorpo, _ctx.Branch, NovoDestino, NovoRascunho, _ctx.Usuario);

            NovoCorpo = "";
            Criando = false;
            await CarregarAsync();

            if (criado is not null)
                Selecionada = Lista.FirstOrDefault(p => p.Pr.Numero == criado.Numero) ?? Selecionada;
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

    // ------------------------------------------------------ mesclar e fechar

    [RelayCommand]
    private async Task MesclarAsync()
    {
        var item = Selecionada;
        if (item is null || !PodeMesclar) return;

        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                "Mesclar pull request",
                $"Mesclar {item.Numero} \"{item.Titulo}\"\n{item.Caminho}\n\n" +
                $"Forma: {Metodo.Nome}. Isso altera a branch {item.Pr.Destino} no GitHub e não tem desfazer por aqui."))
            return;

        await EscreverAsync(() => GitHubService.MesclarPullRequestAsync(_ctx.Slug, item.Pr.Numero, Metodo.Id, _ctx.Usuario));
    }

    [RelayCommand]
    private async Task FecharPrAsync()
    {
        var item = Selecionada;
        if (item is null || !PodeFechar) return;

        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                "Fechar pull request",
                $"Fechar {item.Numero} \"{item.Titulo}\" sem mesclar?\n\nA branch {item.Pr.Origem} continua existindo."))
            return;

        await EscreverAsync(() => GitHubService.FecharPullRequestAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario));
    }

    private async Task EscreverAsync(Func<Task> acao)
    {
        Executando = true;
        Erro = "";
        try
        {
            await acao();
            Alterou = true;
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

    /// <summary>
    /// Algo foi mesclado ou fechado por aqui: quem abriu a janela aproveita para obter
    /// do remoto, senão a branch de destino local continua mostrando o estado antigo.
    /// </summary>
    public bool Alterou { get; private set; }

    [RelayCommand]
    private void AbrirNoGitHub()
    {
        try
        {
            var url = Selecionada?.Pr.Url;
            ShellService.AbrirUrl(string.IsNullOrEmpty(url) ? $"https://github.com/{_ctx.Slug}/pulls" : url);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
