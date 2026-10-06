using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using GRepos.Controls;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Realce de sintaxe do diff: o léxico, o estado entre linhas e a ida para a tela.</summary>
public class RealceTests
{
    private static (string Texto, TipoTrecho Tipo)[] Ler(string arquivo, string linha, int estado = 0)
    {
        var ling = Realce.Para(arquivo)!;
        return Realce.Linha(linha, ling, ref estado)
            .Select(t => (linha.Substring(t.Inicio, t.Tamanho), t.Tipo)).ToArray();
    }

    [Theory]
    [InlineData("src/Unit1.pas", "pascal")]
    [InlineData("Form1.DFM", "dfm")]
    [InlineData("app/App.axaml.cs", "csharp")]
    [InlineData("app/App.axaml", "xml")]
    [InlineData("Projeto.dproj", "xml")]
    [InlineData("consulta.sql", "sql")]
    public void Linguagem_sai_da_extensao(string arquivo, string nome) =>
        Assert.Equal(nome, Realce.Para(arquivo)!.Nome);

    [Theory]
    [InlineData("LEIAME.md")]
    [InlineData("Makefile")]
    [InlineData("")]
    public void Arquivo_desconhecido_fica_sem_realce(string arquivo) => Assert.Null(Realce.Para(arquivo));

    [Fact]
    public void CSharp_palavras_funcao_texto_e_comentario()
    {
        Assert.Equal(new[]
            {
                ("public", TipoTrecho.Palavra), ("override", TipoTrecho.Palavra), ("void", TipoTrecho.Palavra),
                ("Iniciar", TipoTrecho.Funcao), ("// fim", TipoTrecho.Comentario),
            },
            Ler("a.cs", "public override void Iniciar() // fim"));

        // a aspa escapada não fecha o texto, e o que parece comentário dentro dele é texto
        Assert.Equal(new[] { ("var", TipoTrecho.Palavra), ("\"a \\\" // b\"", TipoTrecho.Literal), ("42", TipoTrecho.Numero) },
            Ler("a.cs", "var s = \"a \\\" // b\" + 42;"));
    }

    [Fact]
    public void Pascal_ignora_caixa_e_le_os_numeros_da_linguagem()
    {
        Assert.Equal(new[]
            {
                ("BEGIN", TipoTrecho.Palavra), ("ShowMessage", TipoTrecho.Funcao), ("'it''s'", TipoTrecho.Literal),
                ("#13", TipoTrecho.Numero), ("$FF", TipoTrecho.Numero), ("End", TipoTrecho.Palavra),
            },
            // a aspa dobrada do Pascal vira dois textos colados: a cor é a mesma
            Juntar(Ler("u.pas", "BEGIN ShowMessage('it''s' + #13 + $FF); End;")));
    }

    private static (string, TipoTrecho)[] Juntar((string Texto, TipoTrecho Tipo)[] trechos)
    {
        var saida = new System.Collections.Generic.List<(string, TipoTrecho)>();
        foreach (var t in trechos)
        {
            if (saida.Count > 0 && saida[^1].Item2 == TipoTrecho.Literal && t.Tipo == TipoTrecho.Literal)
                saida[^1] = (saida[^1].Item1 + t.Texto, TipoTrecho.Literal);
            else
                saida.Add(t);
        }
        return saida.ToArray();
    }

    [Fact]
    public void Comentario_de_bloco_atravessa_as_linhas()
    {
        var ling = Realce.Para("u.pas")!;
        var estado = 0;

        var primeira = Realce.Linha("x := 1; { começa", ling, ref estado);
        Assert.Equal(1, estado);
        Assert.Equal(TipoTrecho.Comentario, primeira[^1].Tipo);

        var meio = "  begin end";
        Assert.Equal(new[] { new Trecho(0, meio.Length, TipoTrecho.Comentario) }, Realce.Linha(meio, ling, ref estado));
        Assert.Equal(1, estado);

        var ultima = Realce.Linha("termina } end;", ling, ref estado);
        Assert.Equal(0, estado);
        Assert.Equal(new[] { TipoTrecho.Comentario, TipoTrecho.Palavra }, ultima.Select(t => t.Tipo));
    }

    [Fact]
    public void Marcacao_colore_a_tag_e_o_atributo()
    {
        Assert.Equal(new[]
            {
                ("Button", TipoTrecho.Palavra), ("Grid.Column", TipoTrecho.Funcao), ("\"1\"", TipoTrecho.Literal),
                ("Content", TipoTrecho.Funcao), ("\"Ok\"", TipoTrecho.Literal),
            },
            Ler("v.axaml", "<Button Grid.Column=\"1\" Content=\"Ok\" />"));
        Assert.Equal(new[] { ("StackPanel", TipoTrecho.Palavra) }, Ler("v.axaml", "</StackPanel>"));
    }

    [Fact]
    public void Trechos_nunca_se_sobrepoem_nem_passam_do_fim()
    {
        var linhas = new[] { "", "   ", "'sem fim", "\"\\", "{ aberto", "a.b(c) // x", "$", "#", "<a b=", "1.2.3..4" };
        foreach (var arquivo in new[] { "a.pas", "a.cs", "a.xml", "a.sql", "a.json", "a.py", "a.ini", "a.dfm", "a.js" })
        foreach (var linha in linhas)
        {
            var estado = 0;
            var pos = 0;
            foreach (var t in Realce.Linha(linha, Realce.Para(arquivo)!, ref estado))
            {
                Assert.True(t.Inicio >= pos && t.Tamanho > 0 && t.Inicio + t.Tamanho <= linha.Length,
                    $"{arquivo}: trecho inválido em \"{linha}\"");
                pos = t.Inicio + t.Tamanho;
            }
        }
    }

    // --------------------------------------------------------------- view model

    private const string DiffPascal =
        "diff --git a/src/Juros.pas b/src/Juros.pas\n--- a/src/Juros.pas\n+++ b/src/Juros.pas\n" +
        "@@ -1,4 +1,5 @@\n unit Juros;\n-const Taxa = 1;\n+const Taxa = 2;\n+const Multa = 3; // nova\n interface\n";

    [Fact]
    public void Diff_conta_as_linhas_e_realca_pelo_caminho_do_cabecalho()
    {
        var vm = new DiffViewModel { Split = false };
        vm.Load(DiffPascal);

        Assert.Equal("+2", vm.AdicionadasTexto);
        Assert.Equal("−1", vm.RemovidasTexto);
        Assert.Equal(new[] { "Green", "Green", "Red", "Border", "Border" }, vm.Quadros);

        var nova = vm.Rows.OfType<DiffTextRow>().First(r => r.Text.Contains("Multa"));
        Assert.Equal("+", nova.Marker);
        Assert.Equal(new[] { TipoTrecho.Palavra, TipoTrecho.Numero, TipoTrecho.Comentario }, nova.Trechos.Select(t => t.Tipo));

        vm.Split = true;
        var par = vm.Rows.OfType<DiffSplitRow>().First(r => r.LeftIsDel);
        Assert.Equal(TipoTrecho.Palavra, par.LeftTrechos[0].Tipo);
        Assert.Equal(TipoTrecho.Palavra, par.RightTrechos[0].Tipo);

        vm.Clear("vazio");
        Assert.Equal("+0", vm.AdicionadasTexto);
    }

    [Theory]
    [InlineData(0, 0, "Border,Border,Border,Border,Border")]
    [InlineData(1, 0, "Green,Border,Border,Border,Border")]
    [InlineData(6, 0, "Green,Green,Green,Green,Green")]
    [InlineData(300, 1, "Green,Green,Green,Green,Red")]
    [InlineData(1, 300, "Green,Red,Red,Red,Red")]
    [InlineData(10, 10, "Green,Green,Green,Red,Red")]
    public void Quadros_seguem_a_proporcao_sem_esconder_o_lado_menor(int mais, int menos, string esperado) =>
        Assert.Equal(esperado, string.Join(",", DiffViewModel.QuadrosDe(mais, menos)));

    [Fact]
    public async System.Threading.Tasks.Task Arquivo_inteiro_alterna_o_contexto_e_pede_a_recarga()
    {
        var recargas = 0;
        var vm = new DiffViewModel { Recarregar = () => { recargas++; return System.Threading.Tasks.Task.CompletedTask; } };
        Assert.Equal(3, vm.LinhasDeContexto);
        Assert.Equal("Arquivo inteiro", vm.ContextoRotulo);

        await vm.AlternarContextoCommand.ExecuteAsync(null);
        Assert.True(vm.LinhasDeContexto > 10000);
        Assert.Equal("Só alterações", vm.ContextoRotulo);
        Assert.Equal(1, recargas);
    }

    // --------------------------------------------------------------------- tela

    [AvaloniaFact]
    public void TextBlock_recebe_os_trechos_coloridos_e_volta_a_texto_simples()
    {
        var texto = "const Taxa = 2;";
        var estado = 0;
        var tb = new TextBlock();
        Codigo.SetLinha(tb, new LinhaRealcada(texto, Realce.Linha(texto, Realce.Para("a.pas")!, ref estado)));

        var runs = tb.Inlines!.OfType<Run>().ToList();
        Assert.Equal(texto, string.Concat(runs.Select(r => r.Text)));
        Assert.Equal("const", runs[0].Text);
        Assert.True(runs[0].IsSet(TextElement.ForegroundProperty));
        Assert.False(runs[1].IsSet(TextElement.ForegroundProperty)); // texto comum herda a cor da linha

        // a linha reciclada pela lista virtual não pode guardar o colorido da anterior
        Codigo.SetLinha(tb, new LinhaRealcada("simples", LinhaRealcada.SemRealce));
        Assert.Empty(tb.Inlines!);
        Assert.Equal("simples", tb.Text);
    }

    [AvaloniaFact]
    public void Cores_do_realce_sao_legiveis_sobre_os_fundos_do_diff()
    {
        static double Lum(Color c)
        {
            static double Canal(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
        }

        var app = Avalonia.Application.Current!;
        foreach (var tema in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var tipo in System.Enum.GetValues<TipoTrecho>())
        foreach (var fundo in new[] { "Bg", "AddBg", "DelBg" })
        {
            Assert.True(app.TryGetResource(Codigo.Recurso(tipo), tema, out var f), $"sem cor para {tipo}");
            Assert.True(app.TryGetResource(fundo, tema, out var b));
            var (x, y) = (Lum(((ISolidColorBrush)f!).Color), Lum(((ISolidColorBrush)b!).Color));
            var razao = (System.Math.Max(x, y) + 0.05) / (System.Math.Min(x, y) + 0.05);
            Assert.True(razao >= 3.5, $"{tema}: {tipo} sobre {fundo} = {razao:F2}:1");
        }
    }
}
