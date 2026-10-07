using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Resolução de conflito bloco a bloco: a leitura dos marcadores, a remontagem do
/// arquivo sem trocar encoding nem fim de linha, e o caminho inteiro num merge real.
/// </summary>
public class ConflitosTests
{
    private const string DoisBlocos =
        "unit Boleto;\n" +
        "<<<<<<< HEAD\n" +
        "  Valor := 10;\n" +
        "=======\n" +
        "  Valor := 20;\n" +
        "  Juros := 1;\n" +
        ">>>>>>> feat/juros\n" +
        "meio\n" +
        "<<<<<<< HEAD\n" +
        "=======\n" +
        "  Novo;\n" +
        ">>>>>>> feat/juros\n" +
        "end.\n";

    [Fact]
    public void Le_os_blocos_e_o_texto_comum_em_ordem()
    {
        var trechos = Conflitos.Ler(DoisBlocos);

        Assert.Equal(new[] { false, true, false, true, false }, trechos.Select(t => t.Conflito));
        Assert.Equal(new[] { "  Valor := 10;\n" }, trechos[1].Nosso);
        Assert.Equal(new[] { "  Valor := 20;\n", "  Juros := 1;\n" }, trechos[1].Deles);
        Assert.Equal(2, trechos[1].LinhaInicial);
        Assert.Empty(trechos[3].Nosso); // um lado sem nada: ele removeu (ou o outro acrescentou)
        Assert.Equal(9, trechos[3].LinhaInicial);
    }

    [Fact]
    public void Base_do_diff3_fica_fora_dos_dois_lados()
    {
        var trechos = Conflitos.Ler("<<<<<<< HEAD\nmeu\n||||||| base\nantigo\n=======\ndeles\n>>>>>>> x\n");

        var bloco = Assert.Single(trechos);
        Assert.Equal(new[] { "meu\n" }, bloco.Nosso);
        Assert.Equal(new[] { "deles\n" }, bloco.Deles);
    }

    [Theory]
    [InlineData("<<<<<<< HEAD\nsem fim\n")] // marcador que não fecha
    [InlineData("========\ntítulo sublinhado\n")] // oito sinais não são marcador
    [InlineData("<<<<<<<<<\n=======\n>>>>>>>>>\n")] // nove também não
    public void O_que_so_parece_conflito_fica_como_texto(string texto)
    {
        Assert.False(Conflitos.TemMarcadores(texto));
        Assert.Equal(texto, string.Concat(Conflitos.Ler(texto).SelectMany(t => t.Linhas)));
    }

    [Fact]
    public void Remonta_com_a_escolha_de_cada_bloco()
    {
        var vm = new ConflitoViewModel("", "Boleto.pas");
        vm.Montar(DoisBlocos);

        Assert.Equal(2, vm.Blocos.Count);
        Assert.Equal("Conflito 1 de 2", vm.Blocos[0].Titulo);
        Assert.Equal("unit Boleto;", vm.Blocos[0].Antes);
        Assert.Equal("meio", vm.Blocos[0].Depois);
        Assert.False(vm.PodeSalvar);

        vm.Blocos[0].UsarAmbos();
        Assert.Equal(1, vm.Pendentes);
        vm.Blocos[1].UsarMeu(); // o meu lado é vazio: as linhas saem
        Assert.True(vm.PodeSalvar);

        Assert.Equal(
            "unit Boleto;\n  Valor := 10;\n  Valor := 20;\n  Juros := 1;\nmeio\nend.\n",
            vm.Resultado());
    }

    [Fact]
    public void Digitar_no_resultado_resolve_o_bloco()
    {
        var vm = new ConflitoViewModel("", "Boleto.pas");
        vm.Montar(DoisBlocos);
        var bloco = vm.Blocos[0];

        bloco.UsarDeles();
        Assert.Equal("deles", bloco.Escolha);

        // a caixa de texto do Windows quebra com \r\n; o arquivo é \n e assim deve voltar
        bloco.Resultado = "  Valor := 15;\r\n  Juros := 1;";
        Assert.Equal("manual", bloco.Escolha);

        vm.Blocos[1].UsarDeles();
        Assert.Equal("unit Boleto;\n  Valor := 15;\n  Juros := 1;\nmeio\n  Novo;\nend.\n", vm.Resultado());

        bloco.Limpar();
        Assert.True(bloco.Pendente);
        Assert.False(vm.PodeSalvar);
    }

    [Fact]
    public void Tudo_meu_so_preenche_o_que_esta_pendente()
    {
        var vm = new ConflitoViewModel("", "Boleto.pas");
        vm.Montar(DoisBlocos);
        vm.Blocos[1].UsarDeles();

        vm.TudoMeuCommand.Execute(null);

        Assert.Equal("meu", vm.Blocos[0].Escolha);
        Assert.Equal("deles", vm.Blocos[1].Escolha);
    }

    [Fact]
    public void No_rebase_os_lados_aparecem_desinvertidos()
    {
        var vm = new ConflitoViewModel("", "Boleto.pas", rebase: true);
        vm.Montar(DoisBlocos);

        // o primeiro lado do marcador é a base do rebase: é o "deles"
        Assert.Equal("  Valor := 10;", vm.Blocos[0].Deles);
        Assert.Equal("  Valor := 20;\n  Juros := 1;", vm.Blocos[0].Meu);
    }

    [Fact]
    public void Arquivo_sem_marcadores_explica_em_vez_de_abrir_vazio()
    {
        var vm = new ConflitoViewModel("", "Boleto.pas");
        vm.Montar("unit Boleto;\nend.\n");

        Assert.Empty(vm.Blocos);
        Assert.Contains("não tem marcadores", vm.Erro);
        Assert.False(vm.PodeSalvar);
    }

    [Fact]
    public void Fim_de_linha_do_arquivo_vale_para_o_que_foi_digitado()
    {
        var texto = "a\r\n<<<<<<< HEAD\r\nmeu\r\n=======\r\ndeles\r\n>>>>>>> x\r\nz\r\n";
        var vm = new ConflitoViewModel("", "a.pas");
        vm.Montar(texto);

        vm.Blocos[0].Resultado = "um\ndois";

        Assert.Equal("a\r\num\r\ndois\r\nz\r\n", vm.Resultado());
    }

    // ------------------------------------------------------ num merge real

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

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

    /// <summary>
    /// O caso do fonte Delphi: ANSI com CRLF. Depois de resolver, o arquivo continua em
    /// ANSI com CRLF, sem marcadores, e o git o dá por resolvido.
    /// </summary>
    [Fact]
    public async Task Merge_real_em_arquivo_ansi_salva_sem_trocar_o_encoding()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-conflito-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var arquivo = Path.Combine(dir, "Emissão.pas");

        void Gravar(string valor) => File.WriteAllBytes(arquivo, TextoGit.Ansi.GetBytes(
            "unit Emissão;\r\n// cálculo\r\n" + valor + "\r\nend.\r\n"));

        try
        {
            await Git(dir, "init", "-q", "-b", "main");
            await Git(dir, "config", "user.email", "t@t");
            await Git(dir, "config", "user.name", "Teste");
            await Git(dir, "config", "core.autocrlf", "false");

            Gravar("Valor := 1;");
            await Git(dir, "add", "-A");
            await Git(dir, "commit", "-qm", "inicial");

            await Git(dir, "checkout", "-qb", "feat");
            Gravar("Valor := 3; // promoção");
            await Git(dir, "commit", "-qam", "feat");

            await Git(dir, "checkout", "-q", "main");
            Gravar("Valor := 2; // reajuste");
            await Git(dir, "commit", "-qam", "main");

            await Assert.ThrowsAsync<GitException>(() => Git(dir, "merge", "feat"));

            var (_, arquivos) = await GitService.StatusAndChangesAsync(dir);
            var conflito = Assert.Single(arquivos);
            Assert.True(new FileItemViewModel { Change = conflito }.ConflitoDeConteudo);

            var vm = new ConflitoViewModel(dir, conflito.Path);
            vm.Carregar();

            var bloco = Assert.Single(vm.Blocos);
            Assert.Equal("Valor := 2; // reajuste", bloco.Meu);
            Assert.Equal("Valor := 3; // promoção", bloco.Deles);
            Assert.Equal("unit Emissão;\n// cálculo", bloco.Antes);

            bloco.Resultado = "Valor := 5; // reajuste e promoção";
            await vm.SalvarCommand.ExecuteAsync(null);

            Assert.True(vm.Salvou, vm.Erro);
            Assert.Equal(
                "unit Emissão;\r\n// cálculo\r\nValor := 5; // reajuste e promoção\r\nend.\r\n",
                TextoGit.Ansi.GetString(File.ReadAllBytes(arquivo)));

            var (status, depois) = await GitService.StatusAndChangesAsync(dir);
            Assert.Equal(0, status.Conflicted);
            Assert.DoesNotContain(depois, f => f.Kind == GRepos.Models.ChangeKind.Conflict);
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Arquivo_mexido_com_a_janela_aberta_nao_e_sobrescrito()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-conflito-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var arquivo = Path.Combine(dir, "a.txt");
        try
        {
            File.WriteAllText(arquivo, "<<<<<<< HEAD\nmeu\n=======\ndeles\n>>>>>>> x\n");

            var vm = new ConflitoViewModel(dir, "a.txt");
            vm.Carregar();
            vm.Blocos[0].UsarMeu();

            File.WriteAllText(arquivo, "resolvido no editor\n");
            await vm.SalvarCommand.ExecuteAsync(null);

            Assert.False(vm.Salvou);
            Assert.Contains("mudou no disco", vm.Erro);
            Assert.Equal("resolvido no editor\n", File.ReadAllText(arquivo));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void Utf8_com_bom_volta_com_bom()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "grepos-bom-" + Path.GetRandomFileName());
        try
        {
            File.WriteAllBytes(caminho, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ação\n")).ToArray());

            var lido = Conflitos.LerArquivo(caminho);
            Assert.Equal("ação\n", lido.Texto);
            Assert.True(lido.Bom);

            Conflitos.GravarArquivo(caminho, "ações\n", lido);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ações\n")), File.ReadAllBytes(caminho));
        }
        finally
        {
            File.Delete(caminho);
        }
    }
}
