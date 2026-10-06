using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>Notas da versão: o formato do arquivo, o que vai embutido e a seção das Preferências.</summary>
public class NotasDaVersaoTests
{
    [Fact]
    public void Ler_separa_versao_data_e_texto()
    {
        var notas = NotasDaVersao.Ler(
            "texto solto antes\r\n## 1.0.0.2 — 30/09/2026\r\n- um\r\n- dois\r\n\r\n## 1.0.0.1\n- primeiro\n");

        Assert.Equal(2, notas.Count);
        Assert.Equal(new NotaDaVersao("1.0.0.2", "30/09/2026", "- um\n- dois"), notas[0]);
        Assert.Equal("1.0.0.2 — 30/09/2026", notas[0].Rotulo);
        Assert.Equal(new NotaDaVersao("1.0.0.1", "", "- primeiro"), notas[1]);
        Assert.Equal("1.0.0.1", notas[1].Rotulo);
        Assert.Empty(NotasDaVersao.Ler(""));
    }

    [Theory]
    [InlineData("1.0.0.2", 1)]
    [InlineData("v1.0.0.2", 1)]
    [InlineData("1.0.0.3-dev.4", 0)]
    [InlineData("", 0)]         // build local: abre na mais nova
    [InlineData("9.9.9.9", 0)]  // versão que as notas não conhecem
    public void Abre_na_versao_em_uso(string emUso, int esperado)
    {
        var notas = NotasDaVersao.Ler("## 1.0.0.3\n- c\n## 1.0.0.2\n- b\n## 1.0.0.1\n- a\n");
        Assert.Equal(esperado, NotasDaVersao.IndiceDa(notas, emUso));
    }

    /// <summary>O arquivo de verdade: é o que trava uma versão esquecida, repetida ou fora de ordem.</summary>
    [AvaloniaFact]
    public void Arquivo_embutido_esta_em_ordem_e_completo()
    {
        var notas = NotasDaVersao.Carregar();
        Assert.True(notas.Count >= 28, "as notas embutidas não foram carregadas");

        var numeros = notas.Select(n => Version.Parse(n.Versao)).ToList();
        Assert.Equal(numeros.OrderByDescending(v => v), numeros);
        Assert.Equal(numeros.Count, numeros.Distinct().Count());

        foreach (var n in notas)
        {
            Assert.True(n.Notas.StartsWith("- "), $"{n.Versao}: a nota deve ser uma lista de itens");
            Assert.True(DateTime.TryParseExact(n.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _), $"{n.Versao}: data inválida \"{n.Data}\"");
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public System.Threading.Tasks.Task<bool> ConfirmAsync(string t, string m) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string t) => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PromptAsync(string t, string l, string i = "") => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => System.Threading.Tasks.Task.FromResult<(string, string)?>(null);
        public System.Threading.Tasks.Task ShowAddRepoAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowRepoConfigAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowSettingsAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowBranchesAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowNovidadesAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowStashAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Secao_das_preferencias_troca_a_nota_com_a_versao()
    {
        var janela = new SettingsWindow(new MainViewModel(new FakeDialogs()));
        janela.Show();

        var lista = janela.GetVisualDescendants().OfType<ListBox>().First();
        lista.SelectedIndex = lista.Items.Cast<string>().ToList().IndexOf("Notas da versão");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var notas = NotasDaVersao.Carregar();
        var versoes = janela.GetVisualDescendants().OfType<ComboBox>()
            .First(c => c.Items.Cast<string>().FirstOrDefault() == notas[0].Rotulo);
        var texto = janela.GetVisualDescendants().OfType<MarkdownView>().First();
        Assert.True(texto.IsEffectivelyVisible);
        Assert.Equal(notas[versoes.SelectedIndex].Notas, texto.Markdown);

        versoes.SelectedIndex = notas.Count - 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(notas[^1].Notas, texto.Markdown);
        Assert.Contains("Primeira versão publicada", texto.Markdown);
        janela.Close();
    }
}
