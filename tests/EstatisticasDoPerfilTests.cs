using System;
using System.Collections.Generic;
using System.Linq;
using GRepos.Controls;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Os quadros do cartão de perfil no desenho do github-cool-readme: ritmo semanal, sequências
/// e linguagens, calculados sem rede a partir do calendário e das linhas dos commits.
/// </summary>
public class EstatisticasDoPerfilTests
{
    /// <summary>Um dia por data, com a quantidade dada; a lista começa numa segunda.</summary>
    private static List<DiaContribuicao> Calendario(DateTime inicio, params int[] quantidades) =>
        quantidades.Select((q, i) => new DiaContribuicao(inicio.AddDays(i), q, Math.Min(4, q))).ToList();

    private static readonly DateTime Segunda = new(2026, 11, 2); // uma segunda-feira

    // ------------------------------------------------------------- ritmo semanal

    [Fact]
    public void Ritmo_soma_o_ano_por_dia_da_semana_de_segunda_a_domingo()
    {
        // duas semanas: segunda 5+3, terça 1+1, sábado 0+2, domingo 0+0
        var dias = Calendario(Segunda, 5, 1, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 2, 0);

        var r = EstatisticasDoPerfil.Ritmo(dias)!;

        Assert.Equal(new[] { "seg", "ter", "qua", "qui", "sex", "sáb", "dom" }, r.Dias.Select(d => d.Nome));
        Assert.Equal(new[] { 8, 2, 0, 0, 0, 2, 0 }, r.Dias.Select(d => d.Quantidade));
        Assert.Equal("segunda", r.MaisCheio.NomeCompleto);
        Assert.Equal("quarta", r.MaisVazio.NomeCompleto); // no empate vale o primeiro da semana
        Assert.Equal(10.0 / 12, r.FracaoUteis, 6);
    }

    [Fact]
    public void Ano_vazio_nao_tem_ritmo()
    {
        Assert.Null(EstatisticasDoPerfil.Ritmo(null));
        Assert.Null(EstatisticasDoPerfil.Ritmo(Calendario(Segunda, 0, 0, 0)));
    }

    // --------------------------------------------------------------- sequências

    [Fact]
    public void Sequencia_atual_nao_quebra_porque_hoje_ainda_esta_vazio()
    {
        // ... 3 dias seguidos e hoje (o último) ainda sem nada
        var dias = Calendario(Segunda, 1, 0, 2, 4, 1, 0);

        var s = EstatisticasDoPerfil.Sequencias(dias);

        Assert.Equal(3, s.Atual);
        Assert.Equal(Segunda.AddDays(2), s.InicioDaAtual);
        Assert.Equal(3, s.Maior);
    }

    [Fact]
    public void Ontem_vazio_zera_a_sequencia_atual()
    {
        var s = EstatisticasDoPerfil.Sequencias(Calendario(Segunda, 1, 1, 0, 0));
        Assert.Equal(0, s.Atual);
        Assert.Null(s.InicioDaAtual);
        Assert.Equal(2, s.Maior);
    }

    [Fact]
    public void Maior_sequencia_no_empate_fica_a_mais_recente()
    {
        var dias = Calendario(Segunda, 1, 1, 0, 2, 2, 0, 0);

        var s = EstatisticasDoPerfil.Sequencias(dias);

        Assert.Equal(2, s.Maior);
        Assert.Equal(Segunda.AddDays(3), s.InicioDaMaior);
        Assert.True(s.EmSequencia(Segunda.AddDays(4)));
        Assert.False(s.EmSequencia(Segunda)); // a primeira, de mesmo tamanho, não é destacada
    }

    [Fact]
    public void Dia_que_falta_no_calendario_quebra_a_sequencia()
    {
        var dias = new List<DiaContribuicao>
        {
            new(Segunda, 1, 1), new(Segunda.AddDays(1), 1, 1),
            new(Segunda.AddDays(3), 1, 1), // a quarta não veio
        };

        Assert.Equal(2, EstatisticasDoPerfil.Sequencias(dias).Maior);
    }

    [Fact]
    public void Melhor_dia_melhor_mes_e_dias_ativos()
    {
        var dias = new List<DiaContribuicao>
        {
            new(new DateTime(2026, 9, 29), 9, 4),
            new(new DateTime(2026, 9, 30), 0, 0),
            new(new DateTime(2026, 10, 1), 4, 2),
            new(new DateTime(2026, 10, 2), 4, 2),
            new(new DateTime(2026, 10, 3), 4, 2),
        };

        var s = EstatisticasDoPerfil.Sequencias(dias);

        Assert.Equal(new DateTime(2026, 9, 29), s.MelhorDia);
        Assert.Equal(9, s.NoMelhorDia);
        Assert.Equal(new DateTime(2026, 10, 1), s.MelhorMes); // 12 em outubro contra 9 em setembro
        Assert.Equal(12, s.NoMelhorMes);
        Assert.Equal(4, s.DiasAtivos);
        Assert.Equal(5, s.DiasNoPeriodo);
        Assert.Equal(0.8, s.FracaoAtivos, 6);
    }

    [Fact]
    public void Calendario_vazio_nao_inventa_sequencia()
    {
        var s = EstatisticasDoPerfil.Sequencias(null);
        Assert.Equal(0, s.Atual);
        Assert.Equal(0, s.Maior);
        Assert.Null(s.MelhorDia);
        Assert.Equal(0, s.FracaoAtivos);
    }

    [Fact]
    public void Teto_das_barras_ignora_o_dia_fora_da_curva()
    {
        // 99 dias com 1 e um com 500: a barra cheia vale para 1, não para 500
        var dias = Calendario(Segunda, Enumerable.Repeat(1, 99).Append(500).ToArray());
        Assert.Equal(1, BarrasDoAno.Teto(dias));
    }

    // --------------------------------------------------------------- linguagens

    [Theory]
    [InlineData("Units/uCliente.pas", "Pascal")]
    [InlineData("Forms/fCliente.dfm", "Pascal")]
    [InlineData("Financeiro.dpr", "Pascal")]
    [InlineData("app/Services/GitService.cs", "C#")]
    [InlineData("Units/{uVelho.pas => uNovo.pas}", "Pascal")]
    [InlineData("velho.txt => novo.sql", "SQL")]
    [InlineData("{Units => Fontes}/uX.pas", "Pascal")]
    [InlineData("\"Units/Impress\\303\\243o/uRel.pas\"", "Pascal")]
    [InlineData("scripts/publicar.ps1", "PowerShell")]
    public void Linguagem_sai_da_extensao_como_no_github(string caminho, string linguagem) =>
        Assert.Equal(linguagem, EstatisticasDoPerfil.LinguagemDoCaminho(caminho));

    [Theory]
    [InlineData("README.md")]
    [InlineData("Financeiro.dproj")]
    [InlineData("dados.json")]
    [InlineData("Relatorios/Nota.fr3")]
    [InlineData("Imagens/logo.png")]
    [InlineData("LICENSE")]
    public void Arquivo_que_nao_e_codigo_fica_fora_da_barra(string caminho) =>
        Assert.Null(EstatisticasDoPerfil.LinguagemDoCaminho(caminho));

    [Fact]
    public void Fatias_somam_cem_por_cento_e_o_resto_vira_outras()
    {
        var linhas = new Dictionary<string, long>
        {
            ["Pascal"] = 600, ["C#"] = 200, ["SQL"] = 80, ["Go"] = 50, ["Shell"] = 30,
            ["Lua"] = 20, ["Python"] = 15, ["C"] = 5,
        };

        var f = EstatisticasDoPerfil.Fatias(linhas);

        Assert.Equal(6, f.Count);
        Assert.Equal(new[] { "Pascal", "C#", "SQL", "Go", "Shell", "Outras" }, f.Select(x => x.Nome));
        Assert.Equal(1.0, f.Sum(x => x.Fracao), 6);
        Assert.Equal(40.0 / 1000, f[^1].Fracao, 6);
        Assert.Equal("#E3F171", f[0].Cor);
        Assert.Equal(EstatisticasDoPerfil.CorNeutra, f[^1].Cor);
    }

    [Fact]
    public void Ate_seis_linguagens_nao_tem_outras()
    {
        var f = EstatisticasDoPerfil.Fatias(new Dictionary<string, long> { ["Pascal"] = 3, ["C#"] = 1, ["Go"] = 0 });

        Assert.Equal(new[] { "Pascal", "C#" }, f.Select(x => x.Nome)); // linguagem com zero linhas some
        Assert.Empty(EstatisticasDoPerfil.Fatias(null));
        Assert.Empty(EstatisticasDoPerfil.Fatias(new Dictionary<string, long>()));
    }

    [Fact]
    public void Numstat_separa_as_linhas_por_linguagem_sem_repetir_commit()
    {
        const string saida = "\u0001aaa\n" +
                             "10\t2\tUnits/uCliente.pas\n" +
                             "5\t5\tForms/fCliente.dfm\n" +
                             "-\t-\tImagens/logo.png\n" +
                             "7\t0\tREADME.md\n" +
                             "\u0001bbb\n" +
                             "3\t1\tapp/X.cs\n";
        var contados = new HashSet<string> { "bbb" }; // já veio do outro lado do par
        var porLinguagem = new Dictionary<string, long>();

        var (mais, menos) = GitService.SomarNumstatPorCommit(saida, contados, porLinguagem);

        Assert.Equal(22, mais);
        Assert.Equal(7, menos);
        Assert.Equal(new Dictionary<string, long> { ["Pascal"] = 22 }, porLinguagem);
    }

    // -------------------------------------------------------- grade dos quadros

    [Theory]
    [InlineData(1000, 4)]  // cabem 4
    [InlineData(800, 2)]   // cabem 3, mas 3 deixaria um sozinho embaixo
    [InlineData(500, 2)]
    [InlineData(400, 1)]
    public void Quatro_quadros_ficam_em_quatro_duas_ou_uma_coluna(double largura, int colunas) =>
        Assert.Equal(colunas, GradeCartoes.DividirSemSobra(largura, 215, 28, 4).Colunas);

    [Fact]
    public void Tres_quadros_nao_viram_duas_colunas()
    {
        Assert.Equal(3, GradeCartoes.DividirSemSobra(800, 215, 28, 3).Colunas);
        Assert.Equal(1, GradeCartoes.DividirSemSobra(500, 215, 28, 3).Colunas);
    }

    // ------------------------------------------------------------------ cartão

    [Fact]
    public void Cartao_monta_os_quadros_do_calendario_e_do_resumo()
    {
        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>());
        Assert.False(vm.TemQuadros);

        vm.Dias = Calendario(Segunda, 5, 1, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 2, 1);
        vm.Resumo = new ResumoDeContribuicoes { Commits = 1234, PullRequests = 3, Revisoes = 0, Issues = 1, Privadas = 911 };

        Assert.True(vm.TemQuadros);
        Assert.Equal("1.234", vm.CommitsTexto);
        Assert.True(vm.TemPrivadas);
        Assert.Equal("911", vm.PrivadasTexto);

        Assert.Equal(7, vm.BarrasDaSemana.Count);
        Assert.Equal(PerfilViewModel.AlturaDaBarra, vm.BarrasDaSemana[0].Altura); // segunda é a mais cheia
        Assert.Equal("segunda", vm.MaisCheioNome);
        Assert.Equal("mais cheio · 8", vm.MaisCheioRotulo);
        Assert.Equal("77%", vm.UteisTexto); // 10 de 13

        Assert.Equal("2 dias", vm.AtualTexto);
        Assert.Equal("1 dia", EmDias(1));
        Assert.Equal("melhor dia · 02/11", vm.MelhorDiaRotulo);
        Assert.Equal("melhor mês · nov/26", vm.MelhorMesRotulo);
        Assert.Equal("43%", vm.AtivosTexto); // 6 de 14 dias
        Assert.Equal(6.0 / 14 * 360, vm.AtivosAngulo, 6);
    }

    private static string EmDias(int n)
    {
        var vm = new PerfilViewModel("x", Array.Empty<Repo>())
        {
            Dias = Calendario(Segunda, Enumerable.Repeat(1, n).ToArray()),
        };
        return vm.AtualTexto;
    }

    [Fact]
    public void Linguagens_voltam_com_a_contagem_guardada()
    {
        var contagem = new ContagemDeLinhas
        {
            Adicionadas = 10, Removidas = 2, Quando = "2026-11-17T12:00:00Z",
            Linguagens = new Dictionary<string, long> { ["Pascal"] = 750, ["C#"] = 250 },
        };

        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>(), contagem);

        Assert.True(vm.TemLinguagensLocais);
        Assert.True(vm.TemQuadros); // mesmo sem o calendário (sem token), a faixa aparece
        Assert.Equal(new[] { "75,0%", "25,0%" }, vm.Legenda.Select(l => l.Percentual));
    }

    [Fact]
    public void Contagem_de_versao_anterior_convida_a_contar_de_novo()
    {
        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>(),
            new ContagemDeLinhas { Adicionadas = 10, Removidas = 2, Quando = "2026-11-17T12:00:00Z" });

        Assert.False(vm.TemLinguagensLocais);
        Assert.StartsWith("Conte de novo", vm.LinguagensConvite);
    }

    // -------------------------------------------------------- cor de destaque

    [Fact]
    public void Cor_desconhecida_volta_para_a_do_github()
    {
        Assert.Equal("github", CoresDeDestaque.De(null).Id);
        Assert.Equal("github", CoresDeDestaque.De("magenta").Id);
        Assert.Equal("Purple", CoresDeDestaque.De("ROXO").Barras);
        Assert.Equal(7, CoresDeDestaque.Todas.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Escolher_a_cor_pinta_os_quadros_e_grava_uma_vez()
    {
        var gravadas = new List<string>();
        var vm = new PerfilViewModel("GFBmsoft", Array.Empty<Repo>(), corDosQuadros: "ambar")
        {
            GuardarCor = gravadas.Add,
        };

        Assert.Equal("Yellow", vm.CorDasBarras);
        Assert.Empty(gravadas); // abrir com a cor guardada não é escolher

        vm.EscolherCorCommand.Execute("roxo");
        vm.EscolherCorCommand.Execute("roxo"); // a mesma de novo não grava outra vez

        Assert.Equal("Purple", vm.CorDasBarras);
        Assert.Equal("Purple", vm.CorDoAnel);
        Assert.Equal(new[] { "roxo" }, gravadas);

        vm.EscolherCorCommand.Execute("github");
        Assert.Equal("Green", vm.CorDasBarras);
        Assert.Equal("Lane0", vm.CorDoAnel);
    }

    [Fact]
    public void Resumo_sai_da_mesma_consulta_do_calendario()
    {
        const string json = """
        {"data":{"user":{"contributionsCollection":{
          "totalCommitContributions":238,"totalPullRequestContributions":2,
          "totalPullRequestReviewContributions":1,"totalIssueContributions":4,
          "restrictedContributionsCount":911,
          "contributionCalendar":{"totalContributions":1153,"weeks":[
            {"contributionDays":[{"date":"2026-11-16","contributionCount":3,"contributionLevel":"FIRST_QUARTILE"}]}]}}}}}
        """;

        var c = GitHubService.LerContribuicoes(json)!;

        Assert.Equal(1153, c.Total);
        Assert.NotNull(c.Resumo);
        Assert.Equal(238, c.Resumo!.Commits);
        Assert.Equal(2, c.Resumo.PullRequests);
        Assert.Equal(1, c.Resumo.Revisoes);
        Assert.Equal(4, c.Resumo.Issues);
        Assert.Equal(911, c.Resumo.Privadas);
    }
}
