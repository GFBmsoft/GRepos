using System;
using System.Collections.Generic;
using System.Linq;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// O estilo retrô dos quadros do perfil (os displays "dot matrix" e "OLED" do github-cool-readme):
/// a fonte de pontos, as entradas dos slides e os quatro quadros, sem tela.
/// </summary>
public class DisplayDeLedsTests
{
    // ------------------------------------------------------------------------- fonte

    [Fact]
    public void Largura_soma_as_letras_e_um_ponto_entre_elas()
    {
        Assert.Equal(5, FonteDePontos.Largura("A"));
        Assert.Equal(11, FonteDePontos.Largura("AB"));
        Assert.Equal(26, FonteDePontos.Largura("1.153")); // o ponto tem 2 de largura
        Assert.Equal(0, FonteDePontos.Largura(""));
    }

    [Fact]
    public void Acento_fica_acima_da_letra_e_cedilha_abaixo()
    {
        var e = FonteDePontos.Celulas("E", 0, 10);
        var eCircunflexo = FonteDePontos.Celulas("Ê", 0, 10);
        Assert.True(e.All(p => p.Lin >= 10 && p.Lin < 17));
        Assert.Superset(e.ToHashSet(), eCircunflexo.Where(p => p.Lin >= 10).ToHashSet());
        Assert.Contains(eCircunflexo, p => p.Lin is 8 or 9);

        var cedilha = FonteDePontos.Celulas("Ç", 0, 10);
        Assert.Contains(cedilha, p => p.Lin >= 17);
        Assert.Equal(FonteDePontos.Largura("C"), FonteDePontos.Largura("Ç"));
    }

    [Fact]
    public void Minuscula_sai_maiuscula_e_letra_desconhecida_vira_interrogacao()
    {
        Assert.Equal(FonteDePontos.Celulas("SEÇÃO", 0, 0), FonteDePontos.Celulas("seção", 0, 0));
        Assert.Equal(FonteDePontos.Celulas("?", 0, 0), FonteDePontos.Celulas("€", 0, 0));
    }

    [Fact]
    public void Escala_dois_dobra_cada_ponto()
    {
        Assert.Equal(FonteDePontos.Celulas("8", 0, 0).Count * 4, FonteDePontos.Celulas("8", 0, 0, 2).Count);
        Assert.Equal("PULL", FonteDePontos.Caber("PULL REQUESTS", 23));
    }

    // -------------------------------------------------------------------- slideshow

    private static readonly Regiao Area = new(0, 10, 60, 20);

    private static Slideshow Show(params Efeito[] efeitos) => new(
        new[]
        {
            (IReadOnlyList<ItemDeSlide>)new ItemDeSlide[] { new TextoNoDisplay("AAA", 2, 12), new TextoNoDisplay("11", 2, 20, Brilha: true) },
            new ItemDeSlide[] { new TextoNoDisplay("BBB", 2, 12), new TextoNoDisplay("22", 2, 20, Brilha: true) },
            new ItemDeSlide[] { new TextoNoDisplay("CCC", 2, 12) },
        },
        Area, efeitos);

    private static HashSet<Ponto> Acesos(IParteDoDisplay parte, double? t)
    {
        var saida = new List<Camada>();
        parte.Desenhar(t, saida);
        return saida.Where(c => c.Tom != Tom.Brilho).SelectMany(c => c.Pontos).ToHashSet();
    }

    private static HashSet<Ponto> Texto(string s, int col, int lin) => FonteDePontos.Celulas(s, col, lin).ToHashSet();

    [Fact]
    public void Slides_se_revezam_e_voltam_ao_primeiro()
    {
        var show = Show(Efeito.Empurra);
        Assert.Equal((0, 0.0), show.Momento(0));
        var (i, local) = show.Momento(3.7);
        Assert.Equal(1, i);
        Assert.Equal(0.1, local, 6);
        Assert.Equal(0, show.Momento(show.Ciclo + 1).Indice);
    }

    [Fact]
    public void Parado_mostra_o_primeiro_slide_inteiro()
    {
        var acesos = Acesos(Show(Efeito.Faisca), null);
        Assert.True(acesos.SetEquals(Texto("AAA", 2, 12).Union(Texto("11", 2, 20))));
    }

    [Fact]
    public void Empurrao_sobe_o_novo_de_baixo_e_leva_o_antigo_para_cima()
    {
        var show = Show(Efeito.Empurra);
        var meio = Acesos(show, Slideshow.Exibe + Slideshow.Entra / 2); // metade da entrada do 2º

        // o novo, 10 linhas abaixo (metade das 20 da região); o antigo, 10 acima: o "AAA" saiu
        // pela borda e o "11" subiu até o alto da região
        Assert.Superset(Texto("BBB", 2, 22), meio);
        Assert.Superset(Texto("11", 2, 10), meio);
        Assert.DoesNotContain(Texto("AAA", 2, 12).First(), meio);
        Assert.All(meio, p => Assert.True(Area.Contem(p)));

        // entrou: só ele, no lugar
        var depois = Acesos(show, Slideshow.Exibe + 1);
        Assert.Superset(Texto("BBB", 2, 12), depois);
        Assert.DoesNotContain(Texto("AAA", 2, 12).Except(Texto("BBB", 2, 12)).First(), depois); // um ponto do A que o B não tem
    }

    [Fact]
    public void Varrida_troca_o_antigo_pelo_novo_atras_da_barra()
    {
        var show = Show(Efeito.Varre);
        var saida = new List<Camada>();
        show.Desenhar(Slideshow.Exibe + Slideshow.Entra / 2, saida);

        var frente = Area.Col + Area.Cols / 2;
        var barra = saida.Where(c => c.Tom == Tom.Aceso && c.Pontos.All(p => p.Col is >= 28 and <= 30)).ToList();
        Assert.NotEmpty(barra);

        var novos = Texto("BBB", 2, 12);
        var antigos = Texto("AAA", 2, 12);
        var acesos = saida.SelectMany(c => c.Pontos).ToHashSet();
        Assert.All(novos.Where(acesos.Contains), p => Assert.True(p.Col < frente));
        Assert.All(antigos.Where(p => acesos.Contains(p) && !novos.Contains(p)), p => Assert.True(p.Col >= frente));
    }

    [Fact]
    public void Pisca_some_e_volta_antes_de_ficar()
    {
        var show = Show(Efeito.Pisca);
        var entrada = Slideshow.Exibe;
        Assert.NotEmpty(Acesos(show, entrada + 0.05));                   // fase 0: aceso
        Assert.Empty(Acesos(show, entrada + Slideshow.Entra * 0.3));     // fase 1: apagado
        Assert.NotEmpty(Acesos(show, entrada + Slideshow.Entra * 0.5));  // fase 2: aceso
    }

    [Fact]
    public void Faisca_acende_em_grupos_ate_completar()
    {
        var show = Show(Efeito.Faisca);
        var inteiro = Acesos(show, null);
        Assert.Empty(Acesos(show, 0.05));
        var parcial = Acesos(show, Slideshow.Entra / 2);
        Assert.InRange(parcial.Count, 1, inteiro.Count - 1);
        Assert.True(inteiro.SetEquals(Acesos(show, Slideshow.Entra + 0.01)));
    }

    [Fact]
    public void Maquina_de_escrever_acende_letra_por_letra()
    {
        var show = Show(Efeito.Digita);
        var passo = Slideshow.Digitacao / 5; // AAA + 11 = 5 letras
        var duas = Acesos(show, passo * 2 + 0.001);
        Assert.True(duas.SetEquals(Texto("AA", 2, 12)));
    }

    [Fact]
    public void Estado_so_muda_quando_o_desenho_muda()
    {
        var show = Show(Efeito.Empurra);
        Assert.Equal(show.Estado(2.9), show.Estado(3.3));  // parado no meio do slide
        Assert.NotEqual(show.Estado(Slideshow.Exibe + 0.1), show.Estado(Slideshow.Exibe + 0.3));
        Assert.NotEqual(show.Estado(2.9), show.Estado(Slideshow.Exibe + 2.9)); // outro slide

        var unico = new Slideshow(new[] { (IReadOnlyList<ItemDeSlide>)new ItemDeSlide[] { new TextoNoDisplay("X", 0, 10) } },
            Area, new[] { Efeito.Pisca });
        Assert.Equal(0, unico.Estado(0.3));
        Assert.NotEmpty(Acesos(unico, 0.3)); // um slide só não pisca
    }

    [Fact]
    public void Brilho_passa_uma_vez_sobre_o_numero()
    {
        var show = Show(Efeito.Empurra);
        List<Camada> Em(double t) { var s = new List<Camada>(); show.Desenhar(t, s); return s; }

        var antes = Em(Slideshow.Entra + 0.2);
        var durante = Enumerable.Range(0, 12).Select(k => Em(Slideshow.Entra + 0.5 + k * 0.05)).ToList();
        var depois = Em(Slideshow.Exibe - 0.1);

        Assert.DoesNotContain(antes, c => c.Tom == Tom.Brilho);
        Assert.Contains(durante, s => s.Any(c => c.Tom == Tom.Brilho));
        Assert.DoesNotContain(depois, c => c.Tom == Tom.Brilho);

        // só sobre o texto que brilha ("11"), nunca sobre o "AAA"
        var brilhantes = durante.SelectMany(s => s.Where(c => c.Tom == Tom.Brilho)).SelectMany(c => c.Pontos);
        Assert.All(brilhantes, p => Assert.Contains(p, Texto("11", 2, 20)));
    }

    // ------------------------------------------------------- anel, barra, acender

    [Fact]
    public void Anel_enche_do_topo_no_sentido_do_relogio_ate_a_fracao()
    {
        var anel = new AnelQueEnche(30, 30, 20, 3, 0.25);
        var saida = new List<Camada>();
        anel.Desenhar(null, saida);

        var todas = saida[0].Pontos;
        var cheias = saida[1].Pontos;
        Assert.InRange(cheias.Count, todas.Count * 0.2, todas.Count * 0.3);
        Assert.All(cheias, p => Assert.True(p.Col >= 29 && p.Lin <= 31, $"{p} fora do primeiro quarto"));

        var inicio = new List<Camada>();
        anel.Desenhar(0, inicio);
        Assert.Single(inicio); // ainda não começou a encher: só o fundo
    }

    [Fact]
    public void Barra_cresce_ate_a_fatia_de_cada_linguagem()
    {
        var barra = new BarraQueCresce(new Regiao(4, 54, 100, 6), new[] { 0.6, 0.3 });
        Assert.Equal(60, barra.Largura(null));
        Assert.Equal(30, barra.Largura(0.3));                              // a primeira subindo de 0
        Assert.Equal(60, barra.Largura(2));
        Assert.Equal(45, barra.Largura(Slideshow.Exibe + Slideshow.Entra / 2)); // a segunda descendo
        Assert.Equal(30, barra.Largura(Slideshow.Exibe + 2));
    }

    [Fact]
    public void Ritmo_acende_aos_poucos_e_fica()
    {
        var conteudo = new[] { new Camada(Enumerable.Range(0, 40).Select(c => new Ponto(c, 5)).ToList(), Tom.Aceso) };
        var parte = new AcendeAosPoucos(conteudo);
        Assert.Empty(Acesos(parte, 0.05));
        Assert.InRange(Acesos(parte, 0.35).Count, 1, 39);
        Assert.Equal(40, Acesos(parte, 1).Count);
        Assert.Equal(40, Acesos(parte, null).Count);
    }

    // ----------------------------------------------------------------- os quadros

    private static List<DiaContribuicao> Ano()
    {
        var inicio = new DateTime(2025, 11, 17);
        return Enumerable.Range(0, 366)
            .Select(i => new DiaContribuicao(inicio.AddDays(i), (i * 37 % 11) > 5 ? i % 9 : 0, 1))
            .ToList();
    }

    public static IEnumerable<object[]> Quadros()
    {
        var dias = Ano();
        var s = EstatisticasDoPerfil.Sequencias(dias);
        var resumo = new ResumoDeContribuicoes { Commits = 14_215, PullRequests = 809, Revisoes = 1_008, Issues = 490, Privadas = 911 };
        var fatias = EstatisticasDoPerfil.Fatias(new Dictionary<string, long>
        {
            ["Pascal"] = 589, ["C#"] = 264, ["Go"] = 90, ["PowerShell"] = 18, ["TypeScript"] = 5, ["Objective-C++"] = 3, ["Lua"] = 1,
        });

        yield return new object[] { "resumo", DisplaysDoPerfil.DoResumo(resumo, 13, s)! };
        yield return new object[] { "linguagens", DisplaysDoPerfil.DasLinguagens(fatias)! };
        yield return new object[] { "ritmo", DisplaysDoPerfil.DoRitmo(EstatisticasDoPerfil.Ritmo(dias))! };
        yield return new object[] { "sequências", DisplaysDoPerfil.DasSequencias(dias, s)! };
    }

    /// <summary>
    /// Nenhum ponto fora dos 150 × 73 em instante nenhum: o controle descarta o que sai, e texto
    /// cortado na borda passaria batido na tela.
    /// </summary>
    [Theory]
    [MemberData(nameof(Quadros))]
    public void Quadro_cabe_no_display_em_qualquer_instante(string nome, DisplayDeLeds display)
    {
        var instantes = new double?[] { null }.Concat(Enumerable.Range(0, 300).Select(k => (double?)(k * 0.1)));
        foreach (var t in instantes)
            foreach (var p in display.Camadas(t).SelectMany(c => c.Pontos))
                Assert.True(p.Col is >= 0 and < DisplayDeLeds.Colunas && p.Lin is >= 0 and < DisplayDeLeds.Linhas,
                    $"{nome}: ponto {p} fora do display em t={t}");

        Assert.NotEmpty(display.Descricao);
    }

    [Fact]
    public void Resumo_reveza_os_numeros_e_mostra_privadas_e_estrelas_quando_ha()
    {
        var s = EstatisticasDoPerfil.Sequencias(Ano());
        var com = DisplaysDoPerfil.DoResumo(new ResumoDeContribuicoes { Commits = 1, Privadas = 911 }, 13, s)!;
        var sem = DisplaysDoPerfil.DoResumo(new ResumoDeContribuicoes { Commits = 1 }, -1, s)!;

        Assert.Equal(6, com.Partes.OfType<Slideshow>().Single().Quantidade);
        Assert.Equal(4, sem.Partes.OfType<Slideshow>().Single().Quantidade);
        Assert.Contains("Privadas: 911.", com.Descricao);
        Assert.Null(DisplaysDoPerfil.DoResumo(null, 13, s));
    }

    [Fact]
    public void Sem_dados_nao_ha_quadro()
    {
        Assert.Null(DisplaysDoPerfil.DasLinguagens(Array.Empty<FatiaDeLinguagem>()));
        Assert.Null(DisplaysDoPerfil.DoRitmo(null));
        var vazio = Enumerable.Range(0, 10).Select(i => new DiaContribuicao(new DateTime(2026, 1, 1).AddDays(i), 0, 0)).ToList();
        Assert.Null(DisplaysDoPerfil.DasSequencias(vazio, EstatisticasDoPerfil.Sequencias(vazio)));
    }

    // ------------------------------------------------------------------ estilo e cartão

    [Fact]
    public void Estilo_desconhecido_volta_para_o_classico()
    {
        Assert.Equal("classico", EstilosDosQuadros.De(null).Id);
        Assert.Equal("classico", EstilosDosQuadros.De("neon").Id);
        Assert.True(EstilosDosQuadros.De("OLED").Retro);
        Assert.False(EstilosDosQuadros.De("oled").Pontos);
        Assert.False(EstilosDosQuadros.De("flutuante").Grade);
    }

    [Fact]
    public void Escolher_o_estilo_troca_os_quadros_e_grava_uma_vez()
    {
        var gravados = new List<string>();
        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>(), estiloDosQuadros: "oled")
        {
            GuardarEstilo = gravados.Add,
            Dias = Ano(),
            Resumo = new ResumoDeContribuicoes { Commits = 238 },
        };

        Assert.True(vm.EstiloRetro);
        Assert.Equal(320, vm.LarguraDosQuadros);
        Assert.NotNull(vm.DisplayDoResumo);
        Assert.NotNull(vm.DisplayDoRitmo);
        Assert.NotNull(vm.DisplayDasSequencias);
        Assert.False(vm.LinguagensRetro); // sem linguagens contadas, fica o convite para contar
        Assert.Empty(gravados);

        vm.EscolherEstiloCommand.Execute("classico");
        vm.EscolherEstiloCommand.Execute("classico");

        Assert.True(vm.EstiloClassico);
        Assert.Equal(230, vm.LarguraDosQuadros);
        Assert.Equal(new[] { "classico" }, gravados);
    }
}
