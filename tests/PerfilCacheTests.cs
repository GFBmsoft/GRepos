using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Cache do cartão de perfil: o que foi mostrado da última vez aparece na hora, sem
/// esperar o GitHub — e, sendo recente, o GitHub nem é consultado.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class PerfilCacheTests
{
    /// <summary>Workspace numa pasta temporária, restaurando o GREPOS_HOME que havia antes.</summary>
    private static async Task Com(Func<string, Task> teste)
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-perfil-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        PerfilCache.EsquecerMemoria();
        try
        {
            await teste(home);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            PerfilCache.EsquecerMemoria();
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    private static PerfilGuardado Guardado(DateTime quando) => new()
    {
        Perfil = new Perfil
        {
            Login = "GFBmsoft", Nome = "Gabriel Ferreira", Local = "Rio do Sul, SC",
            RepositoriosPublicos = 12, Seguidores = 4, Estrelas = 7,
            AvatarUrl = "https://avatars.githubusercontent.com/u/123?v=4",
            Linguagens = new[] { "Pascal", "C#" },
        },
        TotalContribuicoes = 1234,
        Dias = { new DiaContribuicao(new DateTime(2026, 10, 6), 5, 2), new DiaContribuicao(new DateTime(2026, 10, 7), 0, 0) },
        Quando = quando,
    };

    [Fact]
    public Task Guardado_volta_do_disco_inteiro() => Com(home =>
    {
        PerfilCache.Guardar("GFBmsoft", Guardado(DateTime.UtcNow));
        Assert.True(File.Exists(Path.Combine(home, "cache", "perfil-gfbmsoft.json")));

        PerfilCache.EsquecerMemoria(); // como numa abertura nova do app
        var lido = PerfilCache.Ler("gfbmsoft")!; // o login não diferencia maiúsculas

        Assert.Equal("Gabriel Ferreira", lido.Perfil.Nome);
        Assert.Equal(7, lido.Perfil.Estrelas);
        Assert.Equal(new[] { "Pascal", "C#" }, lido.Perfil.Linguagens);
        Assert.Equal(1234, lido.TotalContribuicoes);
        Assert.Equal(new DiaContribuicao(new DateTime(2026, 10, 6), 5, 2), lido.Dias[0]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Arquivo_ilegivel_ou_ausente_e_so_falta_de_cache() => Com(home =>
    {
        Assert.Null(PerfilCache.Ler("ninguem"));
        Assert.Null(PerfilCache.Ler(""));

        Directory.CreateDirectory(Path.Combine(home, "cache"));
        File.WriteAllText(PerfilCache.Arquivo("GFBmsoft"), "{ isto não é json");
        Assert.Null(PerfilCache.Ler("GFBmsoft"));
        return Task.CompletedTask;
    });

    [Fact]
    public void Guardado_vale_como_atual_dentro_da_validade()
    {
        var agora = new DateTime(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc);

        Assert.True(PerfilCache.Atual(Guardado(agora.AddMinutes(-5)), agora));
        Assert.False(PerfilCache.Atual(Guardado(agora - PerfilCache.Validade - TimeSpan.FromSeconds(1)), agora));
    }

    [Fact]
    public Task Cartao_aparece_com_o_guardado_sem_ir_ao_github() => Com(async _ =>
    {
        PerfilCache.Guardar("GFBmsoft", Guardado(DateTime.UtcNow));

        // guardado recente: o carregamento termina na hora, sem rede
        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>());
        var carga = vm.CarregarAsync();

        Assert.True(carga.IsCompleted);
        await carga;
        Assert.Equal("Gabriel Ferreira", vm.Nome);
        Assert.Equal("12", vm.Repositorios);
        Assert.Equal("7", vm.Estrelas);
        Assert.True(vm.TemContribuicoes);
        Assert.Equal(1234, vm.TotalContribuicoes);
        Assert.False(vm.Carregando);
        Assert.False(vm.TemErro);
    });

    [Fact]
    public Task Guardado_velho_aparece_primeiro_e_a_falha_da_atualizacao_nao_vira_erro() => Com(async _ =>
    {
        // sem token e sem rede nos testes, a atualização falha — o cartão não pode ficar
        // vazio nem acusar erro: continua com o que tinha
        PerfilCache.Guardar("usuario-que-nao-existe-grepos-teste", Guardado(DateTime.UtcNow.AddDays(-3)));

        var vm = new PerfilViewModel("usuario-que-nao-existe-grepos-teste", Array.Empty<Repo>());
        var carga = vm.CarregarAsync();

        // o guardado já está na tela antes de a rede responder
        Assert.Equal("Gabriel Ferreira", vm.Nome);

        await carga;
        Assert.Equal("Gabriel Ferreira", vm.Nome);
        Assert.False(vm.TemErro);
        Assert.False(vm.Carregando);
    });
}
