using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Uma fala da conversa de um PR ou de uma issue, como aparece na tela.</summary>
public sealed partial class ComentarioViewModel : ObservableObject
{
    [ObservableProperty] private Comentario _comentario = new();

    /// <summary>Em edição: o texto vira caixa, com Salvar e Cancelar.</summary>
    [ObservableProperty] private bool _editando;
    [ObservableProperty] private string _rascunho = "";

    /// <summary>
    /// O comentário é de quem está usando o app: é o que o GitHub deixa editar e excluir
    /// sem ser administrador. Revisão (aprovou, pediu mudanças) não se edita por aqui.
    /// </summary>
    public bool Proprio { get; set; }

    /// <summary>Identifica a fala entre um ciclo e outro: autor, hora e tipo não mudam.</summary>
    public static string ChaveDe(Comentario c) =>
        c.Id > 0 ? $"{c.Origem}#{c.Id}" : $"{c.Autor}|{c.Quando:O}|{c.Tipo}|{c.Onde}";

    public string Chave => ChaveDe(Comentario);

    public string Autor => Comentario.Autor.Length > 0 ? Comentario.Autor : "(sem autor)";
    public string Tipo => Comentario.Tipo;
    public string Quando => Rotulos.Quando(Comentario.Quando);
    public string Onde => Comentario.Onde;
    public bool TemOnde => Comentario.Onde.Length > 0;

    public bool PodeEditar => Proprio && Comentario.Id > 0 && Comentario.Origem != "revisao" && !Editando;
    public bool Lendo => !Editando;

    /// <summary>Aprovação em verde, pedido de mudança em vermelho; o resto é conversa.</summary>
    public string TipoCor => Comentario.Tipo switch
    {
        "aprovou" => "Green",
        "pediu mudanças" => "Red",
        "comentou no código" => "Purple",
        _ => "TextDim",
    };

    /// <summary>Aprovar sem escrever nada é comum: a fala não pode aparecer em branco.</summary>
    public string Corpo => Comentario.Corpo.Trim().Length > 0 ? Comentario.Corpo : "*Sem texto.*";

    partial void OnComentarioChanged(Comentario value) => OnPropertyChanged(string.Empty);

    partial void OnEditandoChanged(bool value)
    {
        OnPropertyChanged(nameof(PodeEditar));
        OnPropertyChanged(nameof(Lendo));
    }
}

/// <summary>
/// A conversa de um PR ou de uma issue, com o que se faz nela: comentar, editar e
/// excluir o próprio comentário. As duas janelas usam a mesma, como no GitHub, onde PR
/// e issue têm a mesma caixa de comentário.
/// </summary>
public sealed partial class ConversaViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _usuario;

    public ConversaViewModel(string slug, string usuario)
    {
        _slug = slug;
        _usuario = usuario;
    }

    /// <summary>Número da issue ou do PR em tela; zero sem nada escolhido.</summary>
    public int Numero { get; set; }

    public ObservableCollection<ComentarioViewModel> Itens { get; } = new();

    [ObservableProperty] private string _novo = "";
    [ObservableProperty] private bool _enviando;

    /// <summary>Erro de uma escrita; quem hospeda a conversa mostra na faixa dele.</summary>
    public Action<string>? AoFalhar { get; set; }

    /// <summary>Algo foi escrito no GitHub: quem hospeda recarrega o que depende disso.</summary>
    public Func<Task>? AoMudar { get; set; }

    public Func<string, string, Task<bool>>? Confirmar { get; set; }

    public bool PodeComentar => !Enviando && Numero > 0 && Novo.Trim().Length > 0;
    public bool Vazia => Itens.Count == 0;

    partial void OnNovoChanged(string value) => OnPropertyChanged(nameof(PodeComentar));
    partial void OnEnviandoChanged(bool value) => OnPropertyChanged(nameof(PodeComentar));

    /// <summary>Atualiza no lugar: quem está lendo (ou editando) não perde a rolagem nem o rascunho.</summary>
    public void Aplicar(IReadOnlyList<Comentario> conversa)
    {
        ListaSync.AplicarModelos(
            Itens, conversa,
            item => item.Chave,
            modelo => ComentarioViewModel.ChaveDe(modelo),
            (item, modelo) => item.Comentario = modelo,
            modelo => new ComentarioViewModel { Comentario = modelo, Proprio = EhProprio(modelo) });
        OnPropertyChanged(nameof(Vazia));
    }

    public void Limpar()
    {
        Itens.Clear();
        Novo = "";
        OnPropertyChanged(nameof(Vazia));
        OnPropertyChanged(nameof(PodeComentar));
    }

    private bool EhProprio(Comentario c) =>
        _usuario.Length > 0 && c.Autor.Equals(_usuario, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> EscreverAsync(Func<Task> acao)
    {
        Enviando = true;
        try
        {
            await acao();
            if (AoMudar is not null) await AoMudar();
            return true;
        }
        catch (Exception e)
        {
            AoFalhar?.Invoke(e.Message);
            return false;
        }
        finally
        {
            Enviando = false;
        }
    }

    /// <summary>Publica o que está na caixa. O texto só sai dela se o GitHub aceitou.</summary>
    [RelayCommand]
    private Task Comentar() => ComentarAsync();

    public async Task<bool> ComentarAsync()
    {
        if (!PodeComentar) return false;

        var numero = Numero;
        var texto = Novo.Trim();
        Comentario? criado = null;
        var ok = await EscreverAsync(async () => criado = await GitHubService.ComentarAsync(_slug, numero, texto, _usuario));
        if (!ok) return false;

        if (Numero == numero)
        {
            Novo = "";
            if (criado is not null && Itens.All(i => i.Chave != ComentarioViewModel.ChaveDe(criado)))
            {
                Itens.Add(new ComentarioViewModel { Comentario = criado, Proprio = true });
                OnPropertyChanged(nameof(Vazia));
            }
        }
        return true;
    }

    [RelayCommand]
    private void Editar(ComentarioViewModel? item)
    {
        if (item is null || !item.PodeEditar) return;
        item.Rascunho = item.Comentario.Corpo;
        item.Editando = true;
    }

    [RelayCommand]
    private void CancelarEdicao(ComentarioViewModel? item)
    {
        if (item is not null) item.Editando = false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SalvarEdicaoAsync(ComentarioViewModel? item)
    {
        if (item is null || !item.Editando) return;

        var texto = item.Rascunho.Trim();
        if (texto.Length == 0)
        {
            AoFalhar?.Invoke("O comentário não pode ficar vazio. Para tirá-lo, use excluir.");
            return;
        }

        var original = item.Comentario;
        if (await EscreverAsync(() => GitHubService.EditarComentarioAsync(_slug, original, texto, _usuario)))
        {
            item.Comentario = new Comentario
            {
                Id = original.Id, Origem = original.Origem, Autor = original.Autor, Quando = original.Quando,
                Tipo = original.Tipo, Onde = original.Onde, Corpo = texto,
            };
            item.Editando = false;
        }
    }

    /// <summary>Excluir não tem volta: confirma antes, como o GitHub.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ExcluirAsync(ComentarioViewModel? item)
    {
        if (item is null || !item.Proprio || item.Comentario.Id == 0) return;

        if (Confirmar is not null && !await Confirmar("Excluir comentário",
                "Excluir este comentário? Ele some do GitHub e não há como recuperar."))
            return;

        var original = item.Comentario;
        if (await EscreverAsync(() => GitHubService.ExcluirComentarioAsync(_slug, original, _usuario)))
        {
            Itens.Remove(item);
            OnPropertyChanged(nameof(Vazia));
        }
    }
}

public sealed class CommitDoPrViewModel
{
    public CommitDoPr Commit { get; init; } = new();

    public string Short => Commit.Sha.Length >= 7 ? Commit.Sha[..7] : Commit.Sha;
    public string Assunto => Commit.Assunto.Length > 0 ? Commit.Assunto : "(sem mensagem)";

    public string Rodape
    {
        get
        {
            var quando = Rotulos.Quando(Commit.Quando);
            return Commit.Autor.Length > 0 && quando.Length > 0 ? $"{Commit.Autor} · {quando}"
                : Commit.Autor.Length > 0 ? Commit.Autor : quando;
        }
    }
}
