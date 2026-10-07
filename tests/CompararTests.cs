using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Comparação entre duas branches ou commits: contagem dos lados, arquivos e diff.</summary>
public class CompararTests
{
    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task Commit(string dir, string nome, string conteudo, string msg)
    {
        File.WriteAllText(Path.Combine(dir, nome), conteudo);
        await Git(dir, "add", "-A");
        await Git(dir, "commit", "-qm", msg);
    }

    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    /// <summary>main com um commit a mais; feat com dois, um deles num arquivo com acento.</summary>
    private static async Task<string> Cenario()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-comparar-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");
        await Commit(dir, "a.txt", "um\ndois\n", "inicial");
        await Git(dir, "tag", "v1");

        await Git(dir, "checkout", "-qb", "feat");
        await Commit(dir, "a.txt", "um\ndois\ntrês\n", "mexe no a");
        await Commit(dir, "Emissão.pas", "unit Emissao;\n", "arquivo novo");

        await Git(dir, "checkout", "-q", "main");
        await Commit(dir, "b.txt", "só na main\n", "na main");
        return dir;
    }

    [Fact]
    public async Task Conta_os_dois_lados_e_lista_os_arquivos_com_acento_no_nome()
    {
        var dir = await Cenario();
        try
        {
            var r = await GitService.CompararAsync(dir, "main", "feat");

            Assert.Equal((1, 2), (r.SoEmA, r.SoEmB));
            Assert.Equal(new[] { "Emissão.pas", "a.txt", "b.txt" }, r.Arquivos.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal));
            Assert.Equal("A", r.Arquivos.Single(f => f.Path == "Emissão.pas").Status);
            Assert.Equal("D", r.Arquivos.Single(f => f.Path == "b.txt").Status); // existe na main, não na feat
            Assert.Equal(1, r.Arquivos.Single(f => f.Path == "a.txt").Added);

            var diff = await GitService.CompararArquivoAsync(dir, "main", "feat", "a.txt");
            Assert.Contains("+três", diff);

            var refs = await GitService.RefsAsync(dir);
            Assert.Contains("v1", refs);
            Assert.Contains("feat", refs);
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Janela_compara_ao_abrir_inverte_e_explica_ponta_que_nao_existe()
    {
        var dir = await Cenario();
        try
        {
            var vm = new CompararViewModel(new Repo { Id = "r", Name = "Financeiro", Path = dir }, "v1", "feat", split: false);
            await vm.IniciarAsync();

            Assert.Equal("feat tem 2 commits que v1 não tem · 2 arquivos diferentes", vm.Resumo);
            Assert.Equal(2, vm.Arquivos.Count);
            Assert.NotNull(vm.Selecionado);

            vm.InverterCommand.Execute(null);
            Assert.Equal(("feat", "v1"), (vm.A, vm.B));
            await vm.CompararAsync();
            Assert.StartsWith("feat tem 2 commits que v1 não tem", vm.Resumo);

            vm.B = "nao-existe";
            await vm.CompararAsync();
            Assert.Contains("não existe neste repositório", vm.Erro);
            Assert.Empty(vm.Arquivos);
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Theory]
    [InlineData(0, 0, 0, "as duas pontas estão no mesmo commit · 0 arquivos diferentes")]
    [InlineData(0, 1, 1, "feat tem 1 commit que main não tem · 1 arquivo diferente")]
    [InlineData(3, 2, 7, "feat tem 2 commits a mais e main tem 3 commits a mais · 7 arquivos diferentes")]
    public void Resumo_diz_quem_tem_o_que(int soEmA, int soEmB, int arquivos, string esperado)
    {
        Assert.Equal(esperado, CompararViewModel.Descrever("main", "feat", soEmA, soEmB, arquivos));
    }
}
