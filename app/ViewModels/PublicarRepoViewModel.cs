using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Publica no GitHub um repositório que só existe na máquina — o "gh repo create
/// --source=. --push": cria o repositório vazio, liga como origin e envia a branch atual.
/// </summary>
public sealed partial class PublicarRepoViewModel : ObservableObject
{
    private readonly string _repoPath;
    private readonly string _usuario;

    public PublicarRepoViewModel(string repoPath, string usuario)
    {
        _repoPath = repoPath;
        _usuario = usuario;
        Nome = GitHubService.NomeDeRepositorio(Path.GetFileName(repoPath.TrimEnd('\\', '/')));
        Donos.Add(usuario.Length > 0 ? usuario : "(sua conta)");
        Dono = Donos[0];
    }

    public string Conta => _usuario.Length > 0 ? _usuario : "a conta das Preferências";

    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private bool _privado = true;
    [ObservableProperty] private string _dono = "";
    [ObservableProperty] private string _erro = "";
    [ObservableProperty] private string _progresso = "";
    [ObservableProperty] private bool _executando;

    /// <summary>A conta e as organizações dela: quem pode ser dono do repositório novo.</summary>
    public ObservableCollection<string> Donos { get; } = new();

    /// <summary>"dono/nome" criado; a janela fecha quando ele aparece.</summary>
    public string Criado { get; private set; } = "";

    public bool TemErro => Erro.Length > 0;
    public bool PodePublicar => !Executando && GitHubService.NomeDeRepositorio(Nome) == Nome.Trim() && Nome.Trim().Length > 0;
    public string AvisoDoNome => Nome.Trim().Length > 0 && GitHubService.NomeDeRepositorio(Nome) != Nome.Trim()
        ? $"O GitHub aceita letras, números, ponto, hífen e sublinhado. Sugestão: {GitHubService.NomeDeRepositorio(Nome)}"
        : "";
    public bool TemAvisoDoNome => AvisoDoNome.Length > 0;

    public Action? Fechar { get; set; }

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));
    partial void OnExecutandoChanged(bool value) => OnPropertyChanged(nameof(PodePublicar));

    partial void OnNomeChanged(string value)
    {
        OnPropertyChanged(nameof(PodePublicar));
        OnPropertyChanged(nameof(AvisoDoNome));
        OnPropertyChanged(nameof(TemAvisoDoNome));
    }

    public async Task CarregarAsync()
    {
        if (_usuario.Length == 0) return;
        try
        {
            foreach (var org in await GitHubService.OrganizacoesAsync(_usuario)) Donos.Add(org);
        }
        catch (Exception)
        {
            // sem a lista (token sem read:org), publica na própria conta
        }
    }

    [RelayCommand]
    private async Task PublicarAsync()
    {
        if (!PodePublicar) return;

        Executando = true;
        Erro = "";
        try
        {
            var organizacao = Dono == Donos[0] ? "" : Dono;

            Progresso = "Criando o repositório no GitHub…";
            Criado = await GitHubService.CriarRepositorioAsync(Nome, Privado, Descricao, organizacao, _usuario);

            Progresso = "Ligando como origin…";
            await GitService.AdicionarRemotoAsync(_repoPath, "origin", GitHubService.UrlDoRemoto(_usuario, Criado));

            if (await GitService.TemCommitsAsync(_repoPath))
            {
                Progresso = "Enviando a branch atual…";
                await GitService.PushAsync(_repoPath, setUpstream: true);
            }

            Fechar?.Invoke();
        }
        catch (Exception e)
        {
            // criado mas não enviado: o remoto já está ligado, e Enviar termina o serviço
            Erro = Criado.Length > 0
                ? $"O repositório {Criado} foi criado, mas o envio falhou: {e.Message} Use Enviar para tentar de novo."
                : e.Message;
        }
        finally
        {
            Progresso = "";
            Executando = false;
        }
    }
}
