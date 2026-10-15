using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Arquivo de uma release (já publicado) ou da lista a enviar (ainda no disco).</summary>
public sealed class AnexoViewModel
{
    public ReleaseAsset Anexo { get; init; } = new();

    /// <summary>Caminho no disco, para os que ainda vão ser enviados.</summary>
    public string Caminho { get; init; } = "";

    public string Nome => Anexo.Nome.Length > 0 ? Anexo.Nome : Path.GetFileName(Caminho);
    public string Tamanho => Rotulos.Bytes(Anexo.Tamanho);
}

public sealed class ReleaseItemViewModel
{
    public Release Release { get; init; } = new();

    public string Titulo => Release.Nome.Trim().Length > 0 ? Release.Nome : Release.Tag;
    public string Tag => Release.Tag;
    public string Notas => Release.Notas.Trim().Length > 0 ? Release.Notas : "*Sem notas.*";

    /// <summary>Rascunho e pré-release ganham um selo; a publicada normal, nenhum.</summary>
    public string Selo => Release.Rascunho ? "rascunho" : Release.PreRelease ? "pré-release" : "";
    public bool TemSelo => Selo.Length > 0;
    public string SeloCor => Release.Rascunho ? "TextDim" : "Yellow";

    public string Rodape
    {
        get
        {
            var partes = new List<string> { Release.Tag };
            var quando = Rotulos.Quando(Release.Publicada);
            if (quando.Length > 0) partes.Add(quando);
            if (Release.Arquivos.Count > 0)
                partes.Add(Release.Arquivos.Count == 1 ? "1 arquivo" : $"{Release.Arquivos.Count} arquivos");
            return string.Join(" · ", partes);
        }
    }

    public IReadOnlyList<AnexoViewModel> Arquivos =>
        Release.Arquivos.Select(a => new AnexoViewModel { Anexo = a }).ToList();

    public bool TemArquivos => Release.Arquivos.Count > 0;
}

/// <summary>
/// Releases do repositório: a lista com os rascunhos, as notas e os arquivos da
/// escolhida, e o formulário de criar ou editar — o que o "gh release" faz. Na release
/// nova com arquivos, ela nasce como rascunho, recebe os arquivos e só então é
/// publicada: quem acompanha o repositório não é avisado de uma release pela metade.
/// </summary>
public sealed partial class ReleasesViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _usuario;
    private readonly string _repoPath;
    private readonly string _branch;

    public ReleasesViewModel(string slug, string usuario, string repoNome = "", string repoPath = "", string branch = "")
    {
        _slug = slug;
        _usuario = usuario;
        _repoPath = repoPath;
        _branch = branch;
        Title = repoNome.Length > 0 ? $"Releases — {repoNome}" : "Releases";
        Subtitulo = slug;
    }

    public string Title { get; }
    public string Subtitulo { get; }

    /// <summary>Confirmação de excluir e publicar; a janela liga ao diálogo dela.</summary>
    public Func<string, string, Task<bool>>? Confirmar { get; set; }

    /// <summary>Seletor de arquivos para anexar; a janela liga ao dela.</summary>
    public Func<Task<IReadOnlyList<string>>>? EscolherArquivos { get; set; }

    [ObservableProperty] private ObservableCollection<ReleaseItemViewModel> _lista = new();
    [ObservableProperty] private ReleaseItemViewModel? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private bool _executando;
    [ObservableProperty] private string _erro = "";

    /// <summary>"Enviando 2 de 3: GRepos.exe…" — subir um .exe grande leva tempo.</summary>
    [ObservableProperty] private string _progresso = "";

    public bool TemErro => Erro.Length > 0;
    public bool TemProgresso => Progresso.Length > 0;
    public bool Vazio => !Carregando && Erro.Length == 0 && Lista.Count == 0;
    public bool SemSelecao => !Editando && Selecionada is null && Lista.Count > 0;
    public bool MostraDetalhe => !Editando && Selecionada is not null;
    public bool PodeAgir => !Executando && Selecionada is not null;
    public bool PodePublicar => PodeAgir && Selecionada?.Release.Rascunho == true;

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(Vazio));
    }

    partial void OnProgressoChanged(string value) => OnPropertyChanged(nameof(TemProgresso));
    partial void OnCarregandoChanged(bool value) => OnPropertyChanged(nameof(Vazio));

    partial void OnExecutandoChanged(bool value)
    {
        foreach (var p in new[] { nameof(PodeAgir), nameof(PodePublicar), nameof(PodeSalvar) })
            OnPropertyChanged(p);
    }

    partial void OnSelecionadaChanged(ReleaseItemViewModel? value)
    {
        foreach (var p in new[] { nameof(SemSelecao), nameof(MostraDetalhe), nameof(PodeAgir), nameof(PodePublicar) })
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
            Aplicar(await GitHubService.ReleasesDoRepoAsync(_slug, _usuario));
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

    /// <summary>Recebe a lista direto; separado da rede para ser testado.</summary>
    public void Aplicar(IReadOnlyList<Release> releases, long escolher = 0)
    {
        var anterior = escolher != 0 ? escolher : Selecionada?.Release.Id ?? 0;
        Lista = new ObservableCollection<ReleaseItemViewModel>(releases.Select(r => new ReleaseItemViewModel { Release = r }));
        Selecionada = Lista.FirstOrDefault(r => r.Release.Id == anterior) ?? Lista.FirstOrDefault();
        foreach (var p in new[] { nameof(Vazio), nameof(SemSelecao) })
            OnPropertyChanged(p);
    }

    private async Task RecarregarAsync(long escolher)
    {
        var lista = await GitHubService.ReleasesDoRepoAsync(_slug, _usuario);
        Aplicar(lista, escolher);
    }

    // ------------------------------------------------------- formulário

    [ObservableProperty] private bool _editando;
    [ObservableProperty] private string _formTag = "";
    [ObservableProperty] private string _formAlvo = "";
    [ObservableProperty] private string _formTitulo = "";
    [ObservableProperty] private string _formNotas = "";
    [ObservableProperty] private bool _formRascunho;
    [ObservableProperty] private bool _formPreRelease;
    [ObservableProperty] private ObservableCollection<AnexoViewModel> _formArquivos = new();
    [ObservableProperty] private ObservableCollection<string> _tagsLocais = new();

    /// <summary>Zero é release nova; senão, o id da que está sendo editada.</summary>
    private long _formId;

    public bool Nova => _formId == 0;
    public string FormCabecalho => Nova ? "NOVA RELEASE" : $"EDITAR {Selecionada?.Tag}";
    public string SalvarRotulo => Nova ? (FormRascunho ? "Salvar rascunho" : "Publicar release") : "Salvar";
    public bool PodeSalvar => !Executando && FormTag.Trim().Length > 0;
    public bool TemFormArquivos => FormArquivos.Count > 0;

    /// <summary>
    /// A tag ainda não existe aqui: o GitHub cria a partir do alvo. Existindo só no
    /// disco, ela é enviada antes — senão o GitHub criaria outra, em outro commit.
    /// </summary>
    public bool TagNova => Nova && FormTag.Trim().Length > 0 && !TagsLocais.Contains(FormTag.Trim());

    partial void OnFormTagChanged(string value)
    {
        OnPropertyChanged(nameof(PodeSalvar));
        OnPropertyChanged(nameof(TagNova));
    }

    partial void OnFormRascunhoChanged(bool value) => OnPropertyChanged(nameof(SalvarRotulo));
    partial void OnFormArquivosChanged(ObservableCollection<AnexoViewModel> value) => OnPropertyChanged(nameof(TemFormArquivos));

    partial void OnEditandoChanged(bool value)
    {
        foreach (var p in new[] { nameof(MostraDetalhe), nameof(SemSelecao) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    private async Task NovaReleaseAsync()
    {
        _formId = 0;
        FormTitulo = "";
        FormNotas = "";
        FormRascunho = false;
        FormPreRelease = false;
        FormAlvo = _branch;
        FormArquivos = new ObservableCollection<AnexoViewModel>();

        if (_repoPath.Length > 0)
        {
            try
            {
                TagsLocais = new ObservableCollection<string>(await GitService.TagsAsync(_repoPath));
            }
            catch (Exception)
            {
                // sem a lista, a tag é digitada inteira
            }
        }

        // a sugestão parte da mais nova entre as tags daqui e as releases publicadas
        FormTag = ProximaVersao.Sugerir(TagsLocais.FirstOrDefault() ?? Lista.FirstOrDefault()?.Tag);
        AbrirForm();
    }

    [RelayCommand]
    private void EditarRelease()
    {
        if (Selecionada is not { } item) return;
        _formId = item.Release.Id;
        FormTag = item.Release.Tag;
        FormAlvo = "";
        FormTitulo = item.Release.Nome;
        FormNotas = item.Release.Notas;
        FormRascunho = item.Release.Rascunho;
        FormPreRelease = item.Release.PreRelease;
        FormArquivos = new ObservableCollection<AnexoViewModel>();
        AbrirForm();
    }

    private void AbrirForm()
    {
        Editando = true;
        foreach (var p in new[] { nameof(Nova), nameof(FormCabecalho), nameof(SalvarRotulo), nameof(TagNova) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    private void CancelarForm() => Editando = false;

    /// <summary>As notas que o GitHub monta (PRs e contribuidores desde a tag anterior).</summary>
    [RelayCommand]
    private async Task GerarNotasAsync()
    {
        if (FormTag.Trim().Length == 0 || Executando) return;
        if (FormNotas.Trim().Length > 0 && Confirmar is not null &&
            !await Confirmar("Gerar notas", "Trocar o texto das notas pelo que o GitHub gerar?"))
            return;

        Executando = true;
        Erro = "";
        try
        {
            FormNotas = await GitHubService.GerarNotasAsync(_slug, FormTag, TagNova ? FormAlvo : "", _usuario);
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
    private async Task AdicionarArquivosAsync()
    {
        if (EscolherArquivos is null) return;
        var escolhidos = await EscolherArquivos();
        AcrescentarArquivos(escolhidos);
    }

    /// <summary>Separado do seletor para ser testado; o mesmo nome não entra duas vezes.</summary>
    public void AcrescentarArquivos(IEnumerable<string> caminhos)
    {
        var lista = FormArquivos.ToList();
        foreach (var c in caminhos.Where(File.Exists))
        {
            if (lista.Any(a => a.Nome.Equals(Path.GetFileName(c), StringComparison.OrdinalIgnoreCase))) continue;
            lista.Add(new AnexoViewModel { Caminho = c, Anexo = new ReleaseAsset { Tamanho = new FileInfo(c).Length } });
        }
        FormArquivos = new ObservableCollection<AnexoViewModel>(lista);
    }

    [RelayCommand]
    private void TirarArquivo(AnexoViewModel? arquivo)
    {
        if (arquivo is null) return;
        FormArquivos = new ObservableCollection<AnexoViewModel>(FormArquivos.Where(a => !ReferenceEquals(a, arquivo)));
    }

    [RelayCommand]
    private async Task SalvarFormAsync()
    {
        if (!PodeSalvar) return;

        Executando = true;
        Erro = "";
        try
        {
            var tag = FormTag.Trim();
            var arquivos = FormArquivos.Select(a => a.Caminho).Where(c => c.Length > 0).ToList();
            Release? salva;

            if (Nova)
            {
                if (TagsLocais.Contains(tag) && _repoPath.Length > 0)
                {
                    Progresso = $"Enviando a tag {tag}…";
                    await GitService.EnviarTagAsync(_repoPath, tag);
                }

                // com arquivos, nasce rascunho e só é publicada depois que eles subirem
                var publicarDepois = arquivos.Count > 0 && !FormRascunho;
                Progresso = "Criando a release…";
                salva = await GitHubService.CriarReleaseAsync(_slug, tag, TagNova ? FormAlvo : "", FormTitulo,
                    FormNotas, FormRascunho || publicarDepois, FormPreRelease, _usuario);

                if (salva is not null) await EnviarAsync(salva.Id, arquivos);
                if (salva is not null && publicarDepois)
                {
                    Progresso = "Publicando…";
                    salva = await GitHubService.EditarReleaseAsync(_slug, salva.Id, tag, FormTitulo, FormNotas,
                        false, FormPreRelease, _usuario);
                }
            }
            else
            {
                Progresso = "Salvando…";
                salva = await GitHubService.EditarReleaseAsync(_slug, _formId, tag, FormTitulo, FormNotas,
                    FormRascunho, FormPreRelease, _usuario);
                await EnviarAsync(_formId, arquivos);
            }

            Editando = false;
            await RecarregarAsync(salva?.Id ?? _formId);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Progresso = "";
            Executando = false;
        }
    }

    private async Task EnviarAsync(long releaseId, IReadOnlyList<string> arquivos)
    {
        for (var i = 0; i < arquivos.Count; i++)
        {
            Progresso = $"Enviando {i + 1} de {arquivos.Count}: {Path.GetFileName(arquivos[i])}…";
            await GitHubService.AnexarAsync(_slug, releaseId, arquivos[i], _usuario);
        }
    }

    // ---------------------------------------------------- na escolhida

    /// <summary>Anexa arquivos à release escolhida, sem abrir o formulário.</summary>
    [RelayCommand]
    private async Task AnexarAsync()
    {
        if (Selecionada is not { } item || EscolherArquivos is null || Executando) return;
        var arquivos = await EscolherArquivos();
        if (arquivos.Count == 0) return;

        Executando = true;
        Erro = "";
        try
        {
            await EnviarAsync(item.Release.Id, arquivos);
            await RecarregarAsync(item.Release.Id);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Progresso = "";
            Executando = false;
        }
    }

    [RelayCommand]
    private async Task ExcluirAnexoAsync(AnexoViewModel? anexo)
    {
        if (Selecionada is not { } item || anexo is null || Executando) return;
        if (Confirmar is not null &&
            !await Confirmar("Excluir arquivo", $"Excluir \"{anexo.Nome}\" da release {item.Tag}?\n\nQuem baixava por este link deixa de conseguir."))
            return;

        await ExecutarAsync(async () =>
        {
            await GitHubService.ExcluirAnexoAsync(_slug, anexo.Anexo.Id, _usuario);
            await RecarregarAsync(item.Release.Id);
        });
    }

    [RelayCommand]
    private async Task PublicarAsync()
    {
        if (Selecionada is not { } item || !PodePublicar) return;
        if (Confirmar is not null &&
            !await Confirmar("Publicar release", $"Publicar o rascunho {item.Tag}? Quem acompanha o repositório é avisado."))
            return;

        var r = item.Release;
        await ExecutarAsync(async () =>
        {
            await GitHubService.EditarReleaseAsync(_slug, r.Id, r.Tag, r.Nome, r.Notas, false, r.PreRelease, _usuario);
            await RecarregarAsync(r.Id);
        });
    }

    [RelayCommand]
    private async Task ExcluirReleaseAsync()
    {
        if (Selecionada is not { } item || Executando) return;
        if (Confirmar is not null &&
            !await Confirmar("Excluir release", $"Excluir a release {item.Tag} e os arquivos dela?\n\nA tag continua no repositório."))
            return;

        await ExecutarAsync(async () =>
        {
            await GitHubService.ExcluirReleaseAsync(_slug, item.Release.Id, _usuario);
            await RecarregarAsync(0);
        });
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
            var url = Selecionada?.Release.Url;
            ShellService.AbrirUrl(string.IsNullOrEmpty(url) ? $"https://github.com/{_slug}/releases" : url);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
