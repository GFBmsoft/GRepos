using System;
using System.IO;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Comparação de versões, escolha do arquivo da release e a troca do executável.
/// A troca é feita com arquivos de verdade numa pasta temporária: é renomeação, e
/// o que interessa é que o antigo sobreviva para poder voltar.
/// </summary>
public class AtualizadorTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "1.0.0.2",
      "html_url": "https://github.com/GFBmsoft/GRepos/releases/tag/1.0.0.2",
      "assets": [
        {
          "name": "GRepos-1.0.0.2.exe",
          "size": 26214400,
          "browser_download_url": "https://github.com/GFBmsoft/GRepos/releases/download/1.0.0.2/GRepos-1.0.0.2.exe"
        },
        {
          "name": "GRepos-1.0.0.2-standalone.exe",
          "size": 93323264,
          "browser_download_url": "https://github.com/GFBmsoft/GRepos/releases/download/1.0.0.2/GRepos-1.0.0.2-standalone.exe"
        }
      ]
    }
    """;

    [Theory]
    [InlineData("1.0.0.1", "1.0.0.2", true)]
    [InlineData("1.0.0.1", "1.0.1.0", true)]
    [InlineData("1.0.0.2", "1.0.0.2", false)]
    [InlineData("1.0.0.3", "1.0.0.2", false)]
    // build feito depois da tag: está à frente da release, não atrás
    [InlineData("1.0.0.2-dev.5", "1.0.0.2", false)]
    [InlineData("1.0.0.1-dev.5", "1.0.0.2", true)]
    // tag escrita com "v" na frente ainda assim é comparada
    [InlineData("1.0.0.1", "v1.0.0.2", true)]
    // sem versão carimbada (build local) nunca há o que oferecer
    [InlineData("", "1.0.0.2", false)]
    [InlineData("1.0.0", "1.0.0.2", false)]
    public void Novidade_so_quando_a_release_e_maior(string atual, string tag, bool esperado)
    {
        Assert.Equal(esperado, Atualizador.TemNovidade(atual, tag));
    }

    [Fact]
    public void A_release_entrega_o_standalone_entre_os_anexos()
    {
        var release = GitHubService.LerRelease(ReleaseJson);

        Assert.NotNull(release);
        Assert.Equal("1.0.0.2", release!.Tag);
        Assert.Equal(2, release.Arquivos.Count);

        // o que troca sozinho é o standalone: o outro depende do .NET instalado
        var alvo = release.Standalone;
        Assert.NotNull(alvo);
        Assert.Equal("GRepos-1.0.0.2-standalone.exe", alvo!.Nome);
        Assert.Equal(93323264, alvo.Tamanho);
        Assert.StartsWith("https://", alvo.Url);
    }

    [Fact]
    public void Resposta_sem_release_nao_quebra()
    {
        Assert.Null(GitHubService.LerRelease("""{"message":"Not Found"}"""));
        Assert.Null(GitHubService.LerRelease("[]"));
    }

    [Fact]
    public void Troca_guarda_o_antigo_e_poe_o_novo_no_lugar()
    {
        var dir = Pasta();
        try
        {
            var atual = Path.Combine(dir, "GRepos.exe");
            var novo = Path.Combine(dir, "GRepos-1.0.0.2-standalone.exe.baixando");
            File.WriteAllText(atual, "versao antiga");
            File.WriteAllText(novo, "versao nova");

            Atualizador.Trocar(atual, novo);

            Assert.Equal("versao nova", File.ReadAllText(atual));
            Assert.False(File.Exists(novo));

            // o antigo fica como caminho de volta, e some na abertura seguinte
            var antigo = atual + Atualizador.SufixoAntigo;
            Assert.Equal("versao antiga", File.ReadAllText(antigo));

            Atualizador.LimparAntigo(atual);
            Assert.False(File.Exists(antigo));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void Uma_segunda_troca_substitui_o_antigo_da_anterior()
    {
        var dir = Pasta();
        try
        {
            var atual = Path.Combine(dir, "GRepos.exe");
            File.WriteAllText(atual, "v1");
            File.WriteAllText(atual + Atualizador.SufixoAntigo, "v0"); // sobra de antes

            var novo = Path.Combine(dir, "novo.exe");
            File.WriteAllText(novo, "v2");
            Atualizador.Trocar(atual, novo);

            Assert.Equal("v2", File.ReadAllText(atual));
            Assert.Equal("v1", File.ReadAllText(atual + Atualizador.SufixoAntigo));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void So_o_executavel_de_arquivo_unico_troca_sozinho()
    {
        var dir = Pasta();
        try
        {
            var exe = Path.Combine(dir, "GRepos.exe");
            File.WriteAllText(exe, "exe");

            // arquivo único: o assembly de entrada não tem .dll em disco para apontar
            Assert.True(Atualizador.PodeTrocarSozinho(exe, ""));
            Assert.True(Atualizador.PodeTrocarSozinho(exe, null));

            // build de pasta: trocar só o .exe deixaria as DLLs ao lado desencontradas
            Assert.False(Atualizador.PodeTrocarSozinho(exe, Path.Combine(dir, "GRepos.dll")));

            Assert.False(Atualizador.PodeTrocarSozinho(null, ""));
            Assert.False(Atualizador.PodeTrocarSozinho("", ""));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void Renomear_o_executavel_nao_engana_a_validacao()
    {
        var dir = Pasta();
        try
        {
            // build de pasta com o .exe renomeado pelo usuário: o GRepos.dll continua
            // ali, e a checagem antiga (procurar "MeuGit.dll") dava falso positivo
            var exe = Path.Combine(dir, "MeuGit.exe");
            File.WriteAllText(exe, "exe");
            File.WriteAllText(Path.Combine(dir, "GRepos.dll"), "dll");

            Assert.False(Atualizador.PodeTrocarSozinho(exe, Path.Combine(dir, "GRepos.dll")));

            // arquivo único renomeado continua podendo trocar: o nome não entra na conta
            Assert.True(Atualizador.PodeTrocarSozinho(exe, ""));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void Pasta_sem_permissao_de_escrita_nao_troca()
    {
        var dir = Pasta();
        try
        {
            Assert.True(Atualizador.PastaGravavel(dir));

            // pasta inexistente é o caso que dá para simular sem mexer em ACL
            var inexistente = Path.Combine(dir, "nao-existe", "nem-aqui");
            Assert.False(Atualizador.PastaGravavel(inexistente));
            Assert.False(Atualizador.PodeTrocarSozinho(Path.Combine(inexistente, "GRepos.exe"), ""));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Endereco_que_nao_e_https_e_recusado()
    {
        var dir = Pasta();
        try
        {
            var arquivo = new ReleaseAsset
            {
                Nome = "GRepos.exe",
                Tamanho = 10,
                Url = "http://exemplo/GRepos.exe",
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Atualizador.BaixarAsync(arquivo, dir));
        }
        finally
        {
            Limpar(dir);
        }
    }

    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-upd-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Limpar(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { /* pasta temporária */ }
    }
}

/// <summary>
/// O clique da barra de status (o ícone e o convite), como no GTerm: um só procura, confere e,
/// havendo versão nova, pergunta e atualiza. Nada aqui chega à rede, abre o navegador ou baixa.
/// </summary>
public class AtualizacaoNumCliqueTests
{
    private sealed class Dialogos : GRepos.ViewModels.IDialogService
    {
        public System.Collections.Generic.List<string> Perguntas { get; } = new();
        public System.Threading.Tasks.Task<bool> ConfirmAsync(string t, string m)
        {
            Perguntas.Add(m);
            return System.Threading.Tasks.Task.FromResult(false); // desiste: nada é baixado
        }
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string t) => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PromptAsync(string t, string l, string i = "") => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => System.Threading.Tasks.Task.FromResult<(string, string)?>(null);
        public System.Threading.Tasks.Task ShowAddRepoAsync(GRepos.ViewModels.MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowRepoConfigAsync(GRepos.ViewModels.MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowSettingsAsync(GRepos.ViewModels.MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowBranchesAsync(GRepos.ViewModels.MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowNovidadesAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowStashAsync(GRepos.ViewModels.MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
    }

    private static (GRepos.ViewModels.MainViewModel Main, Dialogos Dialogos, System.Collections.Generic.List<string> Paginas) Montar(
        Release? release, bool podeTrocar = true)
    {
        var dialogos = new Dialogos();
        var paginas = new System.Collections.Generic.List<string>();
        var main = new GRepos.ViewModels.MainViewModel(dialogos)
        {
            VersaoDoApp = "1.0.0.10",
            ProcurarRelease = () => System.Threading.Tasks.Task.FromResult(release),
            PodeTrocar = _ => podeTrocar,
            AbrirPagina = paginas.Add,
        };
        return (main, dialogos, paginas);
    }

    private static Release ReleaseNova() => new()
    {
        Tag = "1.0.0.11",
        Url = "https://github.com/GFBmsoft/GRepos/releases/tag/1.0.0.11",
        Arquivos = { new ReleaseAsset { Nome = "GRepos-1.0.0.11-standalone.exe", Tamanho = 115 * 1024 * 1024, Url = "https://exemplo/x.exe" } },
    };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async System.Threading.Tasks.Task Com_versao_nova_um_clique_procura_e_ja_pergunta_se_atualiza()
    {
        var (main, dialogos, paginas) = Montar(ReleaseNova());
        Assert.False(main.TemAtualizacao); // ninguém tinha procurado ainda

        await main.AtualizacaoCommand.ExecuteAsync(null);

        Assert.Equal("1.0.0.11", main.AtualizacaoTag);
        var pergunta = Assert.Single(dialogos.Perguntas);
        Assert.Contains("Baixar a versão 1.0.0.11 (115 MB)", pergunta);
        Assert.Empty(paginas);
        Assert.False(main.Atualizando); // desistiu na pergunta: o convite continua na barra
        Assert.Equal("Atualização 1.0.0.11 disponível", main.AtualizacaoAviso);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async System.Threading.Tasks.Task Em_dia_o_clique_diz_e_nao_pergunta_nada()
    {
        var (main, dialogos, paginas) = Montar(new Release { Tag = "1.0.0.10" });

        await main.AtualizacaoCommand.ExecuteAsync(null);

        Assert.False(main.TemAtualizacao);
        Assert.Equal("Em dia: é a versão mais nova.", main.SituacaoDaVersao);
        Assert.Empty(dialogos.Perguntas);
        Assert.Empty(paginas);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async System.Threading.Tasks.Task Build_de_pasta_abre_a_pagina_da_release_em_vez_de_trocar()
    {
        var (main, dialogos, paginas) = Montar(ReleaseNova(), podeTrocar: false);

        await main.AtualizacaoCommand.ExecuteAsync(null);

        Assert.Empty(dialogos.Perguntas);
        Assert.Equal(new[] { "https://github.com/GFBmsoft/GRepos/releases/tag/1.0.0.11" }, paginas);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async System.Threading.Tasks.Task Versao_que_sumiu_tira_o_convite_da_barra()
    {
        var release = ReleaseNova();
        var dialogos = new Dialogos();
        var main = new GRepos.ViewModels.MainViewModel(dialogos)
        {
            VersaoDoApp = "1.0.0.10",
            ProcurarRelease = () => System.Threading.Tasks.Task.FromResult<Release?>(release),
            PodeTrocar = _ => true,
            AbrirPagina = _ => { },
        };
        await main.ProcurarAtualizacaoCommand.ExecuteAsync(null);
        Assert.True(main.TemAtualizacao);
        Assert.Equal("Atualizar para 1.0.0.11", main.RotuloDeAtualizar);

        // a release foi retirada (ou esta já é a mais nova): a procura seguinte apaga o convite
        release = new Release { Tag = "1.0.0.10" };
        await main.ProcurarAtualizacaoCommand.ExecuteAsync(null);
        Assert.False(main.TemAtualizacao);
        Assert.Equal("Procurar atualização", main.RotuloDeAtualizar);
        Assert.Equal("Em dia: é a versão mais nova.", main.SituacaoDaVersao);
    }
}
