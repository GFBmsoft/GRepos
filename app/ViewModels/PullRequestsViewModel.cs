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

/// <summary>
/// Uma verificação do PR. Atualizada no lugar a cada ciclo, como as linhas da esteira:
/// quem está olhando um build rodar não pode ver a lista piscar.
/// </summary>
public sealed partial class VerificacaoViewModel : ObservableObject
{
    [ObservableProperty] private Verificacao _verificacao = new();

    /// <summary>"passo 4 de 10: Testes" enquanto roda; vazio quando não se sabe ou já terminou.</summary>
    [ObservableProperty] private string _passo = "";

    public string Nome => Verificacao.Nome;
    public string Simbolo => CiVisual.Simbolo(Verificacao.Situacao);
    public string Cor => CiVisual.Cor(Verificacao.Situacao);
    public bool Rodando => Verificacao.Situacao == "rodando";
    public bool TemPasso => Rodando && Passo.Length > 0;

    /// <summary>"passou · 1m 29s", "rodando há 40s", "na fila".</summary>
    public string Texto
    {
        get
        {
            var texto = CiVisual.Texto(Verificacao.Situacao);
            if (Rodando)
                return Verificacao.Iniciada is { } inicio
                    ? $"rodando há {Rotulos.Duracao(DateTime.UtcNow - inicio)}"
                    : "na fila";

            return Verificacao is { Iniciada: { } i, Concluida: { } f } && f >= i
                ? $"{texto} · {Rotulos.Duracao(f - i)}"
                : texto;
        }
    }

    partial void OnVerificacaoChanged(Verificacao value) => OnPropertyChanged(string.Empty);
    partial void OnPassoChanged(string value) => OnPropertyChanged(nameof(TemPasso));

    /// <summary>O tempo decorrido anda sozinho: a linha se redesenha sem resposta nova da API.</summary>
    public void Tique() => OnPropertyChanged(nameof(Texto));

    /// <summary>O passo em andamento de um job, no formato da linha.</summary>
    public static string PassoDe(CiJob job)
    {
        if (job.Etapas.Count == 0) return "";

        var atual = job.Etapas.FirstOrDefault(e => e.Situacao == "rodando")
                    ?? job.Etapas.FirstOrDefault(e => e.Situacao is not ("sucesso" or "falha" or "cancelado"));
        return atual is null ? "" : $"passo {job.Etapas.IndexOf(atual) + 1} de {job.Etapas.Count}: {atual.Nome}";
    }
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

        Editor = new ConversaViewModel(contexto.Slug, contexto.Usuario)
        {
            AoFalhar = mensagem => Erro = mensagem,
            AoMudar = RecarregarEscolhidoAsync,
            Confirmar = dialogs is null ? null : dialogs.ConfirmAsync,
        };
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversaViewModel.Novo)) OnPropertyChanged(nameof(PodePedirMudancas));
        };

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

    public bool MostraDetalhe => !Criando && !EditandoPr && Selecionada is not null;
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
    public string MesclagemTexto => SituacaoDeMesclagem(Selecionada?.Pr, Detalhe, ResumoCi).Texto;
    public string MesclagemCor => SituacaoDeMesclagem(Selecionada?.Pr, Detalhe, ResumoCi).Cor;

    private string ResumoCi => GitHubService.ResumoDasVerificacoes(Verificacoes.Select(v => v.Verificacao).ToList());

    /// <param name="verificacoes">Resumo das verificações, para dizer por que o GitHub chama o PR de instável.</param>
    public static (string Texto, string Cor, bool Mesclavel) SituacaoDeMesclagem(
        PullRequest? item, PullRequest? detalhe, string verificacoes = "")
    {
        if (item is null) return ("", "TextDim", false);
        if (item.Estado == "mesclado") return ("Mesclado", "Purple", false);
        if (item.Estado == "fechado") return ("Fechado sem mesclar", "Red", false);
        if (item.Rascunho) return ("Rascunho: marque como pronto no GitHub para mesclar", "TextDim", false);

        return (detalhe?.Mesclagem ?? "") switch
        {
            "clean" or "has_hooks" => ("Pronto para mesclar", "Green", true),
            "dirty" => ("Em conflito com o destino: resolva na branch antes de mesclar", "Red", false),
            "blocked" when verificacoes == "rodando" =>
                ("Bloqueado até as verificações obrigatórias terminarem", "Yellow", false),
            "blocked" => ("Bloqueado pelas regras da branch (revisão ou verificação obrigatória)", "Yellow", false),
            "behind" => ("Atrás do destino: pode mesclar, mas vale atualizar a branch antes", "Yellow", true),
            "unstable" when verificacoes == "rodando" =>
                ("Dá para mesclar, mas as verificações ainda estão rodando", "Yellow", true),
            "unstable" when verificacoes == "falha" =>
                ("Dá para mesclar, mas há verificação quebrada: confira antes", "Red", true),
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
        foreach (var p in new[] { nameof(PodeMesclar), nameof(PodeFechar), nameof(PodeCriar), nameof(AvisoNovo),
                                  nameof(PodeRevisar), nameof(PodePedirMudancas), nameof(RevisarDica),
                                  nameof(PodeEditarPr), nameof(PodeSalvarPr) })
            OnPropertyChanged(p);
    }

    private void AvisarDetalhe()
    {
        foreach (var p in new[] { nameof(MostraDetalhe), nameof(SemSelecao), nameof(DetalheTitulo),
                                  nameof(DetalheCaminho), nameof(DetalheCorpo), nameof(DetalheResumo),
                                  nameof(MesclagemTexto), nameof(MesclagemCor), nameof(TemVerificacoes),
                                  nameof(CiTexto), nameof(CiCor), nameof(TemCiTexto),
                                  nameof(AbaConversaRotulo), nameof(AbaCommitsRotulo), nameof(AbaArquivosRotulo) })
            OnPropertyChanged(p);
        AvisarBotoes();
    }

    partial void OnDetalheChanged(PullRequest? value) => AvisarDetalhe();

    partial void OnSelecionadaChanged(PrViewModel? value)
    {
        // o detalhe na tela é de outro PR: não pode ficar por baixo do novo
        _verificacoesConsultadas = false;
        LimparAbas();
        Detalhe = null;
        Verificacoes.Clear();
        AvisarDetalhe();

        if (value is not null)
        {
            Criando = false;
            _ = CarregarDetalheAsync(value);
        }
    }

    /// <param name="silencioso">
    /// Ciclo automático: não mostra "carregando", não espera a mesclagem ser calculada e
    /// falha de rede não vira erro na tela — o que já está lá continua valendo.
    /// </param>
    private async Task CarregarDetalheAsync(PrViewModel item, bool silencioso = false)
    {
        if (_ctx.Slug.Length == 0) return;

        if (!silencioso) CarregandoDetalhe = true;
        try
        {
            var detalhe = await GitHubService.PullRequestAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario);
            if (!ReferenceEquals(Selecionada, item)) return; // trocou de PR no meio

            // o GitHub calcula a mesclagem em segundo plano: a primeira consulta de um PR
            // recém-criado costuma voltar "unknown", e a segunda já traz a resposta
            if (!silencioso && detalhe is { Mesclagem: "unknown" or "" } && item.Pr.Aberto)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (!ReferenceEquals(Selecionada, item)) return;
                detalhe = await GitHubService.PullRequestAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario) ?? detalhe;
                if (!ReferenceEquals(Selecionada, item)) return;
            }

            var verificacoes = await GitHubService.VerificacoesAsync(
                _ctx.Slug, detalhe?.Sha ?? item.Pr.Sha, _ctx.Usuario);
            if (!ReferenceEquals(Selecionada, item)) return;

            AplicarVerificacoes(verificacoes);
            item.CiSituacao = GitHubService.ResumoDasVerificacoes(verificacoes);
            Detalhe = detalhe;

            await CarregarPassosAsync(item);

            // a conversa entra junto do detalhe, mas não em todo ciclo rápido: ela muda
            // pouco, e são três consultas
            if (!silencioso || _ciclos % 3 == 1) await CarregarConversaAsync(item);
            if (!silencioso) await CarregarAbaAsync(item);
        }
        catch (Exception e)
        {
            if (!silencioso && ReferenceEquals(Selecionada, item)) Erro = e.Message;
        }
        finally
        {
            if (!silencioso) CarregandoDetalhe = false;
            AvisarDetalhe();
            AjustarRitmo();
        }
    }

    // ------------------------------------------------- verificações (Actions)

    /// <summary>As verificações do PR escolhido já foram consultadas ao menos uma vez.</summary>
    private bool _verificacoesConsultadas;

    /// <summary>
    /// Atualiza as verificações no lugar. Trocar a coleção recriaria as linhas a cada
    /// ciclo, bem enquanto o usuário acompanha o build.
    /// </summary>
    public void AplicarVerificacoes(IReadOnlyList<Verificacao> verificacoes)
    {
        ListaSync.AplicarModelos(
            Verificacoes, verificacoes,
            item => item.Verificacao.Nome + "#" + item.Verificacao.RunId,
            modelo => modelo.Nome + "#" + modelo.RunId,
            (item, modelo) => item.Verificacao = modelo,
            modelo => new VerificacaoViewModel { Verificacao = modelo });

        _verificacoesConsultadas = true;
        AvisarVerificacoes();
    }

    private void AvisarVerificacoes()
    {
        foreach (var p in new[] { nameof(TemVerificacoes), nameof(CiTexto), nameof(CiCor), nameof(TemCiTexto),
                                  nameof(MesclagemTexto), nameof(MesclagemCor) })
            OnPropertyChanged(p);
    }

    /// <summary>
    /// A frase que responde "e o Actions?": aguardando começar, rodando (quantas já
    /// terminaram e qual está em andamento), quebrou ou passou.
    /// </summary>
    public string CiTexto => SituacaoDasVerificacoes(
        Selecionada?.Pr, Detalhe, Verificacoes.Select(v => v.Verificacao).ToList(),
        _verificacoesConsultadas, DateTime.UtcNow).Texto;

    public string CiCor => SituacaoDasVerificacoes(
        Selecionada?.Pr, Detalhe, Verificacoes.Select(v => v.Verificacao).ToList(),
        _verificacoesConsultadas, DateTime.UtcNow).Cor;

    public bool TemCiTexto => CiTexto.Length > 0;

    /// <summary>PR mexido há menos que isto e ainda sem verificação: o Actions deve estar para começar.</summary>
    public static readonly TimeSpan EsperaPeloActions = TimeSpan.FromMinutes(3);

    public static (string Texto, string Cor, bool Acompanhar) SituacaoDasVerificacoes(
        PullRequest? item, PullRequest? detalhe, IReadOnlyList<Verificacao> verificacoes, bool consultado, DateTime agora)
    {
        if (item is null || !item.Aberto) return ("", "TextDim", false);
        if (!consultado) return ("Consultando as verificações…", "TextDim", false);

        var total = verificacoes.Count;
        if (total == 0)
        {
            // o GitHub leva alguns segundos entre o push (ou a criação do PR) e a
            // verificação aparecer; depois disso, é porque o repositório não roda nenhuma
            var mexido = (detalhe ?? item).Atualizado ?? item.Atualizado;
            return mexido is { } m && agora - m < EsperaPeloActions
                ? ("Aguardando o GitHub Actions começar…", "Yellow", true)
                : ("Nenhuma verificação rodou neste pull request.", "TextDim", false);
        }

        var quebradas = verificacoes.Count(v => v.Situacao == "falha");
        var rodando = verificacoes.Where(v => v.Situacao == "rodando").ToList();
        var concluidas = total - rodando.Count;

        if (rodando.Count > 0)
        {
            var nomes = string.Join(", ", rodando.Take(3).Select(v => v.Nome)) + (rodando.Count > 3 ? "…" : "");
            var falhas = quebradas == 0 ? "" : quebradas == 1 ? ", 1 já quebrou" : $", {quebradas} já quebraram";
            return ($"Actions rodando: {concluidas} de {total} concluída(s){falhas} — agora: {nomes}",
                quebradas > 0 ? "Red" : "Yellow", true);
        }

        if (quebradas > 0)
            return (quebradas == 1 && total == 1 ? "A verificação quebrou."
                : $"{quebradas} de {total} verificações quebraram.", "Red", false);

        return (total == 1 ? "A verificação passou." : $"As {total} verificações passaram.", "Green", false);
    }

    /// <summary>
    /// Para cada verificação rodando que é um job do Actions, o passo em andamento. Uma
    /// consulta por execução, não por job; sem permissão ou fora da cota, a linha só
    /// fica sem o passo.
    /// </summary>
    private async Task CarregarPassosAsync(PrViewModel item)
    {
        var runs = Verificacoes.Where(v => v.Rodando && v.Verificacao.RunId > 0)
            .Select(v => v.Verificacao.RunId).Distinct().ToList();

        foreach (var v in Verificacoes.Where(v => !v.Rodando)) v.Passo = "";

        foreach (var run in runs)
        {
            try
            {
                var jobs = await GitHubService.JobsAsync(_ctx.Slug, run, _ctx.Usuario);
                if (!ReferenceEquals(Selecionada, item)) return;

                foreach (var v in Verificacoes.Where(v => v.Rodando && v.Verificacao.RunId == run))
                    if (jobs.FirstOrDefault(j => j.Nome == v.Nome) is { } job)
                        v.Passo = VerificacaoViewModel.PassoDe(job);
            }
            catch (Exception)
            {
                // o passo é um extra
            }
        }
    }

    // -------------------------------------- escrever no PR: revisar e editar

    /// <summary>Depois de comentar ou revisar: a conversa e a contagem da aba, sem piscar a tela.</summary>
    private async Task RecarregarEscolhidoAsync()
    {
        if (Selecionada is not { } item || _ctx.Slug.Length == 0) return;
        await CarregarConversaAsync(item);
        await CarregarDetalheAsync(item, silencioso: true);
    }

    /// <summary>O PR é de quem está usando o app: o GitHub não deixa revisar o próprio.</summary>
    private bool Proprio => Selecionada is { } s && _ctx.Usuario.Length > 0 &&
                            s.Pr.Autor.Equals(_ctx.Usuario, StringComparison.OrdinalIgnoreCase);

    public bool PodeRevisar => !Executando && Selecionada is { Pr.Aberto: true } && !Proprio;

    /// <summary>Pedir mudanças exige dizer quais: o texto vem da caixa de comentário.</summary>
    public bool PodePedirMudancas => PodeRevisar && Editor.Novo.Trim().Length > 0;

    public string RevisarDica => Proprio
        ? "O GitHub não deixa aprovar nem pedir mudanças no próprio pull request"
        : "O texto da caixa de comentário vai junto com a revisão";

    private async Task RevisarAsync(string evento)
    {
        if (Selecionada is not { } item || !PodeRevisar) return;

        Executando = true;
        Erro = "";
        try
        {
            await GitHubService.RevisarPullRequestAsync(_ctx.Slug, item.Pr.Numero, evento, Editor.Novo.Trim(), _ctx.Usuario);
            if (ReferenceEquals(Selecionada, item)) Editor.Novo = "";
            await RecarregarEscolhidoAsync();
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

    [RelayCommand] private Task Aprovar() => RevisarAsync("APPROVE");
    [RelayCommand] private Task PedirMudancas() => PodePedirMudancas ? RevisarAsync("REQUEST_CHANGES") : Task.CompletedTask;

    /// <summary>O painel da direita mostra o título e a descrição em caixas, para editar.</summary>
    [ObservableProperty] private bool _editandoPr;
    [ObservableProperty] private string _edicaoTitulo = "";
    [ObservableProperty] private string _edicaoCorpo = "";

    public bool PodeEditarPr => !Executando && Selecionada is not null;
    public bool PodeSalvarPr => !Executando && EdicaoTitulo.Trim().Length > 0;

    partial void OnEdicaoTituloChanged(string value) => OnPropertyChanged(nameof(PodeSalvarPr));

    partial void OnEditandoPrChanged(bool value)
    {
        OnPropertyChanged(nameof(MostraDetalhe));
        OnPropertyChanged(nameof(SemSelecao));
    }

    [RelayCommand]
    private void EditarPr()
    {
        if (Selecionada is not { } item) return;
        EdicaoTitulo = item.Pr.Titulo;
        EdicaoCorpo = (Detalhe ?? item.Pr).Corpo;
        EditandoPr = true;
    }

    [RelayCommand]
    private void CancelarEdicaoPr() => EditandoPr = false;

    [RelayCommand]
    private async Task SalvarPrAsync()
    {
        if (Selecionada is not { } item || !PodeSalvarPr) return;

        Executando = true;
        Erro = "";
        try
        {
            await GitHubService.EditarPullRequestAsync(_ctx.Slug, item.Pr.Numero, EdicaoTitulo, EdicaoCorpo, _ctx.Usuario);
            EditandoPr = false;
            await CarregarAsync();
            if (Selecionada is { } atual) await CarregarDetalheAsync(atual, silencioso: true);
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

    // ---------------------------------- abas: conversa, commits e arquivos

    /// <summary>0 conversa, 1 commits, 2 arquivos — como as abas do PR no GitHub.</summary>
    [ObservableProperty] private int _aba;

    /// <summary>A conversa do PR escolhido e o que se faz nela (comentar, editar, excluir).</summary>
    public ConversaViewModel Editor { get; }

    public ObservableCollection<ComentarioViewModel> Conversa => Editor.Itens;
    [ObservableProperty] private ObservableCollection<CommitDoPrViewModel> _commitsDoPr = new();
    [ObservableProperty] private ObservableCollection<CommitFileViewModel> _arquivos = new();
    [ObservableProperty] private CommitFileViewModel? _arquivoSelecionado;
    [ObservableProperty] private bool _carregandoAba;

    /// <summary>Diff do arquivo escolhido na aba Arquivos; só leitura.</summary>
    public DiffViewModel DiffDoArquivo { get; } = new() { Split = false };

    private readonly Dictionary<string, ArquivoDoPr> _patches = new();

    /// <summary>De qual PR são os commits e os arquivos carregados (0: nenhum).</summary>
    private int _commitsDe;
    private int _arquivosDe;

    public bool SemConversa => !CarregandoDetalhe && Conversa.Count == 0;
    public bool SemCommits => !CarregandoAba && CommitsDoPr.Count == 0;
    public bool SemArquivos => !CarregandoAba && Arquivos.Count == 0;

    // o número vem do detalhe do PR, antes mesmo de a aba ser aberta
    public string AbaConversaRotulo => Rotulo("Conversa", Detalhe?.Comentarios ?? Conversa.Count);
    public string AbaCommitsRotulo => Rotulo("Commits", Detalhe?.Commits ?? CommitsDoPr.Count);
    public string AbaArquivosRotulo => Rotulo("Arquivos", Detalhe?.Arquivos ?? Arquivos.Count);

    private static string Rotulo(string nome, int quantos) => quantos > 0 ? $"{nome} ({quantos})" : nome;

    private void AvisarAbas()
    {
        foreach (var p in new[] { nameof(SemConversa), nameof(SemCommits), nameof(SemArquivos),
                                  nameof(AbaConversaRotulo), nameof(AbaCommitsRotulo), nameof(AbaArquivosRotulo) })
            OnPropertyChanged(p);
    }

    partial void OnCarregandoAbaChanged(bool value) => AvisarAbas();
    partial void OnCarregandoDetalheChanged(bool value) => AvisarAbas();

    partial void OnAbaChanged(int value)
    {
        if (Selecionada is { } item) _ = CarregarAbaAsync(item);
    }

    partial void OnArquivoSelecionadoChanged(CommitFileViewModel? value)
    {
        if (value is null)
        {
            DiffDoArquivo.Title = "";
            DiffDoArquivo.Clear("Escolha um arquivo para ver o que mudou.");
            return;
        }

        DiffDoArquivo.Title = value.Path;
        var diff = _patches.TryGetValue(value.Path, out var arquivo) ? GitHubService.DiffDoArquivo(arquivo) : "";
        if (diff.Length == 0) DiffDoArquivo.Clear("Sem visualização: arquivo binário ou grande demais para o GitHub mandar o diff.");
        else DiffDoArquivo.Load(diff);
    }

    /// <summary>O que é do PR anterior sai da tela junto com ele.</summary>
    private void LimparAbas()
    {
        Editor.Limpar();
        Editor.Numero = Selecionada?.Pr.Numero ?? 0;
        EditandoPr = false;
        CommitsDoPr.Clear();
        ArquivoSelecionado = null;
        Arquivos.Clear();
        _patches.Clear();
        _commitsDe = 0;
        _arquivosDe = 0;
        AvisarAbas();
    }

    public void AplicarConversa(IReadOnlyList<Comentario> conversa)
    {
        // no lugar: a conversa é recarregada no ciclo automático, e quem está lendo não pode perder a rolagem
        Editor.Aplicar(conversa);
        AvisarAbas();
    }

    public void AplicarCommits(IReadOnlyList<CommitDoPr> commits)
    {
        CommitsDoPr = new ObservableCollection<CommitDoPrViewModel>(
            commits.Select(c => new CommitDoPrViewModel { Commit = c }));
        AvisarAbas();
    }

    public void AplicarArquivos(IReadOnlyList<ArquivoDoPr> arquivos)
    {
        _patches.Clear();
        foreach (var a in arquivos) _patches[a.Caminho] = a;

        Arquivos = new ObservableCollection<CommitFileViewModel>(arquivos.Select(a => new CommitFileViewModel
        {
            File = new GRepos.Models.CommitFile
            {
                Path = a.Caminho, Added = a.Adicionadas, Removed = a.Removidas, Status = a.Status,
            },
        }));
        ArquivoSelecionado = Arquivos.FirstOrDefault();
        AvisarAbas();
    }

    private async Task CarregarConversaAsync(PrViewModel item)
    {
        try
        {
            var conversa = await GitHubService.ConversaDoPrAsync(_ctx.Slug, item.Pr.Numero, _ctx.Usuario);
            if (ReferenceEquals(Selecionada, item)) AplicarConversa(conversa);
        }
        catch (Exception)
        {
            // a conversa é complemento: sem ela ficam a descrição e as verificações
        }
    }

    /// <summary>Commits e arquivos só são buscados quando a aba deles é aberta.</summary>
    private async Task CarregarAbaAsync(PrViewModel item)
    {
        if (_ctx.Slug.Length == 0) return;

        var numero = item.Pr.Numero;
        var precisa = Aba == 1 ? _commitsDe != numero : Aba == 2 && _arquivosDe != numero;
        if (!precisa) return;

        CarregandoAba = true;
        try
        {
            if (Aba == 1)
            {
                var commits = await GitHubService.CommitsDoPrAsync(_ctx.Slug, numero, _ctx.Usuario);
                if (!ReferenceEquals(Selecionada, item)) return;
                AplicarCommits(commits);
                _commitsDe = numero;
            }
            else
            {
                var arquivos = await GitHubService.ArquivosDoPrAsync(_ctx.Slug, numero, _ctx.Usuario);
                if (!ReferenceEquals(Selecionada, item)) return;
                AplicarArquivos(arquivos);
                _arquivosDe = numero;
            }
        }
        catch (Exception e)
        {
            if (ReferenceEquals(Selecionada, item)) Erro = e.Message;
        }
        finally
        {
            CarregandoAba = false;
        }
    }

    // ------------------------------------------------- atualização automática

    /// <summary>Com algo rodando ou para começar: é quando a tela muda de verdade.</summary>
    public static readonly TimeSpan IntervaloAcompanhando = TimeSpan.FromSeconds(10);

    /// <summary>Com tudo parado: só para notar um PR novo ou um push de outra pessoa.</summary>
    public static readonly TimeSpan IntervaloParado = TimeSpan.FromSeconds(45);

    private Avalonia.Threading.DispatcherTimer? _timer;
    private bool _ocupado;
    private int _ciclos;

    private bool Acompanhando => SituacaoDasVerificacoes(
        Selecionada?.Pr, Detalhe, Verificacoes.Select(v => v.Verificacao).ToList(),
        _verificacoesConsultadas, DateTime.UtcNow).Acompanhar;

    /// <summary>Recado do rodapé, para a atualização sozinha não parecer mágica.</summary>
    public string RitmoTexto => _timer is { IsEnabled: true }
        ? $"atualiza sozinha a cada {(int)_timer.Interval.TotalSeconds}s"
        : "";

    private void AjustarRitmo()
    {
        if (_timer is null) return;

        var alvo = Acompanhando ? IntervaloAcompanhando : IntervaloParado;
        if (_timer.Interval != alvo) _timer.Interval = alvo;
        OnPropertyChanged(nameof(RitmoTexto));
    }

    /// <summary>Ciclo automático: sem "Carregando…" na tela e sem pisar no ciclo anterior.</summary>
    private async Task AtualizarAsync()
    {
        if (_ocupado || Executando || Carregando || _ctx.Slug.Length == 0) return;

        _ocupado = true;
        try
        {
            // a lista muda pouco: no ritmo rápido ela entra só a cada três ciclos, para
            // não gastar a cota da API com o que não mudou
            var rapido = Acompanhando;
            if (!rapido || _ciclos++ % 3 == 0)
            {
                try
                {
                    Aplicar(await GitHubService.ListarPullRequestsAsync(_ctx.Slug, _ctx.Usuario, !MostrarFechados));
                }
                catch (Exception)
                {
                    // falha de rede no ciclo automático não apaga o que já está na tela
                }
            }

            if (Selecionada is { } item) await CarregarDetalheAsync(item, silencioso: true);

            // os tempos ("rodando há 40s") andam mesmo sem resposta nova
            foreach (var v in Verificacoes.Where(v => v.Rodando)) v.Tique();
        }
        finally
        {
            _ocupado = false;
            AjustarRitmo();
        }
    }

    /// <summary>Para de consultar a API. A janela chama ao fechar.</summary>
    public void Parar()
    {
        _timer?.Stop();
        OnPropertyChanged(nameof(RitmoTexto));
    }

    // ---------------------------------------------------------------- lista

    /// <summary>A janela chama ao abrir: carrega e passa a acompanhar.</summary>
    public async Task IniciarAsync()
    {
        await CarregarAsync();
        await SugerirDestinoAsync();

        _timer ??= new Avalonia.Threading.DispatcherTimer { Interval = IntervaloParado };
        _timer.Tick += (_, _) => _ = AtualizarAsync();
        _timer.Start();
        AjustarRitmo();
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
