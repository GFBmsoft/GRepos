using System;
using System.Text;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// A mensagem do commit em Markdown: renderizada na tela e copiada de duas formas, como
/// foi escrita e formatada (HTML para colar com formatação, texto limpo onde ela não chega).
/// </summary>
public class MarkdownExportTests
{
    /// <summary>A mensagem da EV15.</summary>
    private const string Corpo =
        "# TESTE\n\n- [Pedido 6443](http://bmsoft.ddns.net:8088/mantis/view.php?id=6443):\n  - Voltando modulo para versão em produção após teste";

    [Fact]
    public void Html_tem_titulo_link_e_lista_dentro_de_lista()
    {
        var html = MarkdownExport.ParaHtml(Corpo);

        Assert.Equal(
            "<h1>TESTE</h1>" +
            "<ul><li><a href=\"http://bmsoft.ddns.net:8088/mantis/view.php?id=6443\">Pedido 6443</a>:</li>" +
            "<ul><li>Voltando modulo para versão em produção após teste</li></ul></ul>",
            html);
    }

    [Fact]
    public void Html_escapa_o_que_e_texto_e_fecha_as_listas()
    {
        var html = MarkdownExport.ParaHtml("Se a < b && c > d\n\n1. um\n2. dois\n\nfim **forte** e `x<y`");

        Assert.Equal(
            "<p>Se a &lt; b &amp;&amp; c &gt; d</p><ol><li>um</li><li>dois</li></ol>" +
            "<p>fim <b>forte</b> e <code>x&lt;y</code></p>",
            html);
        Assert.Equal("", MarkdownExport.ParaHtml(""));
    }

    [Fact]
    public void Texto_limpo_tira_os_sinais_e_mantem_o_endereco_do_link()
    {
        var texto = MarkdownExport.ParaTexto(Corpo).Replace("\r\n", "\n");

        Assert.Equal(
            "TESTE\n\n" +
            "• Pedido 6443 (http://bmsoft.ddns.net:8088/mantis/view.php?id=6443):\n" +
            "  • Voltando modulo para versão em produção após teste",
            texto);

        // link cujo texto já é o endereço não repete o endereço
        Assert.Equal("veja https://x.dev", MarkdownExport.ParaTexto("veja [https://x.dev](https://x.dev)"));
        Assert.Equal("forte e código", MarkdownExport.ParaTexto("**forte** e `código`"));
    }

    [Fact]
    public void Formato_da_area_de_transferencia_aponta_para_o_trecho_certo_mesmo_com_acentos()
    {
        var fragmento = "<p>versão em produção — ação</p>";
        var bytes = MarkdownExport.ParaAreaDeTransferencia(fragmento);
        var texto = Encoding.UTF8.GetString(bytes);

        int Numero(string campo)
        {
            var i = texto.IndexOf(campo + ":", StringComparison.Ordinal) + campo.Length + 1;
            return int.Parse(texto.Substring(i, 10));
        }

        Assert.StartsWith("Version:0.9\r\n", texto);

        // os números são posições em bytes, não em caracteres: é onde o acento engana
        var inicio = Numero("StartFragment");
        var fim = Numero("EndFragment");
        Assert.Equal(fragmento, Encoding.UTF8.GetString(bytes, inicio, fim - inicio));

        Assert.Equal(bytes.Length, Numero("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Numero("StartHTML"), 6));
    }
}

[Collection(WorkspaceGlobal.Nome)]
public class MensagemEmMarkdownTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

    private static HistoryViewModel Vm(MainViewModel main) =>
        new(new Repo { Id = "r", Name = "Dav", Path = "" }, main, 50, split: true)
        {
            DetailSubject = "Dav 1.44.0.0 [auto] [cooldown=0]",
            DetailBody = "# TESTE\n\n- [Pedido 6443](http://x/view.php?id=6443):\n  - Voltando modulo",
            HasDetail = true,
        };

    [Fact]
    public void Botao_alterna_entre_o_texto_e_o_renderizado_e_a_escolha_vale_para_os_outros_repositorios()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = Vm(main);

        Assert.True(vm.CorpoComoTexto);
        Assert.False(vm.CorpoComoMarkdown);

        vm.AlternarMarkdownCommand.Execute(null);
        Assert.True(vm.CorpoComoMarkdown);
        Assert.False(vm.CorpoComoTexto);
        Assert.True(main.MensagemEmMarkdown);

        // outro repositório aberto depois já vem renderizado
        Assert.True(Vm(main).CorpoEmMarkdown);

        // sem corpo não há o que mostrar em nenhum dos dois modos
        vm.DetailBody = "";
        Assert.False(vm.CorpoComoMarkdown);
        Assert.False(vm.CorpoComoTexto);
    }

    [Fact]
    public void As_duas_copias_levam_o_assunto_e_o_corpo()
    {
        var vm = Vm(new MainViewModel(new FakeDialogs()));

        // como foi escrita: com os sinais do Markdown
        var texto = vm.MensagemCompleta.Replace("\r\n", "\n");
        Assert.StartsWith("Dav 1.44.0.0 [auto] [cooldown=0]\n\n# TESTE\n", texto);
        Assert.Contains("[Pedido 6443](http://x/view.php?id=6443)", texto);

        // formatada: assunto em negrito, corpo renderizado
        Assert.StartsWith("<p><b>Dav 1.44.0.0 [auto] [cooldown=0]</b></p><h1>TESTE</h1>", vm.MensagemEmHtml);
        Assert.Contains("<a href=\"http://x/view.php?id=6443\">Pedido 6443</a>", vm.MensagemEmHtml);

        // e a versão em texto limpo, para onde a formatação não chega
        var limpo = vm.MensagemEmTextoLimpo.Replace("\r\n", "\n");
        Assert.StartsWith("Dav 1.44.0.0 [auto] [cooldown=0]\n\nTESTE\n\n• Pedido 6443 (http://x/view.php?id=6443):", limpo);

        // commit só com assunto
        vm.DetailBody = "";
        Assert.Equal("Dav 1.44.0.0 [auto] [cooldown=0]", vm.MensagemCompleta);
        Assert.Equal("Dav 1.44.0.0 [auto] [cooldown=0]", vm.MensagemEmTextoLimpo);
    }
}
