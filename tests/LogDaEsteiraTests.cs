using System;
using System.Collections.Generic;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Log dos passos da esteira e artefatos: o que o "gh run view --log" e o
/// "gh run download" fazem, pela API.
/// </summary>
public class LogDaEsteiraTests
{
    // recorte de um log de verdade do runner: horário no começo de cada linha, cores
    // ANSI e os comandos ##[...] do próprio runner
    private const string Log =
        "﻿2026-10-15T12:00:01.1000000Z Current runner version: '2.329.0'\n" +
        "2026-10-15T12:00:02.2000000Z ##[group]Operating System\n" +
        "2026-10-15T12:00:02.2100000Z Microsoft Windows Server 2022\n" +
        "2026-10-15T12:00:02.2200000Z ##[endgroup]\n" +
        "2026-10-15T12:00:05.0000000Z ##[group]Run actions/checkout@v4\n" +
        "2026-10-15T12:00:05.5000000Z Syncing repository: GFBmsoft/GRepos\n" +
        "2026-10-15T12:00:09.0000000Z ##[group]Run dotnet test\n" +
        "2026-10-15T12:00:09.1000000Z \u001b[36;1mdotnet test -c Release\u001b[0m\n" +
        "2026-10-15T12:01:30.0000000Z   Failed GRepos.Tests.X [12 ms]\n" +
        "linha que quebrou sem horário\n" +
        "2026-10-15T12:01:31.0000000Z ##[error]Process completed with exit code 1.\n" +
        "2026-10-15T12:01:32.0000000Z Cleaning up orphan processes\n";

    private static readonly List<CiEtapa> Etapas = new()
    {
        new() { Numero = 1, Nome = "Set up job", Inicio = T("12:00:01"), Fim = T("12:00:04") },
        new() { Numero = 2, Nome = "Run actions/checkout@v4", Inicio = T("12:00:05"), Fim = T("12:00:08") },
        new() { Numero = 3, Nome = "Artefato", Situacao = "pulado" },
        new() { Numero = 4, Nome = "dotnet test", Inicio = T("12:00:09"), Fim = T("12:01:31") },
        new() { Numero = 5, Nome = "Complete job", Inicio = T("12:01:32"), Fim = T("12:01:32") },
    };

    private static DateTime T(string hora) =>
        DateTime.Parse("2026-10-15T" + hora + "Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    [Fact]
    public void Passo_recebe_so_as_linhas_dele_ate_o_proximo_que_rodou()
    {
        var checkout = LogDaEsteira.DoPasso(Log, Etapas, 2);
        Assert.Equal("▸ Run actions/checkout@v4\nSyncing repository: GFBmsoft/GRepos", checkout.Texto.Replace("\r\n", "\n"));

        // o pulado não tem início: o passo 2 vai até o 4, e o próprio 3 vem vazio
        Assert.Equal("", LogDaEsteira.DoPasso(Log, Etapas, 3).Texto);
    }

    [Fact]
    public void Erro_vem_destacado_e_a_sujeira_do_terminal_sai()
    {
        var teste = LogDaEsteira.DoPasso(Log, Etapas, 4);

        Assert.Equal(new[] { "Process completed with exit code 1." }, teste.Erros);
        Assert.Contains("dotnet test -c Release", teste.Texto);
        Assert.DoesNotContain('\u001b', teste.Texto); // char: a comparação por cultura ignora o ESC
        // linha sem horário herda a da anterior e fica no mesmo passo
        Assert.Contains("linha que quebrou sem horário", teste.Texto);
        Assert.DoesNotContain("Cleaning up", teste.Texto);
    }

    [Fact]
    public void Passo_longo_mostra_so_o_fim()
    {
        var linhas = new System.Text.StringBuilder();
        for (var i = 0; i < LogDaEsteira.LimiteDeLinhas + 20; i++)
            linhas.Append($"2026-10-15T12:00:09.{i:0000000}Z linha {i}\n");

        var trecho = LogDaEsteira.DoPasso(linhas.ToString(), new List<CiEtapa> { new() { Numero = 1, Inicio = T("12:00:09") } }, 1);

        Assert.Equal(20, trecho.Cortadas);
        Assert.StartsWith("linha 20", trecho.Texto);
    }

    [Theory]
    [InlineData("##[endgroup]", null)]
    [InlineData("##[group]Run x", "▸ Run x")]
    [InlineData("##[warning]cuidado", "⚠ cuidado")]
    [InlineData("##[command]git version", "$ git version")]
    [InlineData("texto comum", "texto comum")]
    public void Comandos_do_runner_viram_simbolos(string linha, string? esperado) =>
        Assert.Equal(esperado, LogDaEsteira.Limpar(linha));

    [Fact]
    public void Artefatos_saem_da_resposta_com_tamanho_e_validade()
    {
        var lista = GitHubService.LerArtefatos("""
        {"total_count":2,"artifacts":[
          {"id":11,"name":"GRepos-1.0.0.46","size_in_bytes":29000000,"expired":false,"expires_at":"2026-11-14T12:00:00Z"},
          {"id":12,"name":"velho","size_in_bytes":10,"expired":true}]}
        """);

        Assert.Equal(2, lista.Count);
        Assert.Equal("GRepos-1.0.0.46", lista[0].Nome);
        Assert.Equal(29000000, lista[0].Tamanho);
        Assert.False(lista[0].Expirado);
        Assert.NotNull(lista[0].ExpiraEm);
        Assert.True(lista[1].Expirado);
        Assert.Empty(GitHubService.LerArtefatos("""{"message":"Not Found"}"""));
    }

    [Theory]
    [InlineData("GRepos-1.0.0.46", "GRepos-1.0.0.46")]
    [InlineData("build: win/x64", "build_ win_x64")]
    [InlineData("...", "artefato")]
    public void Nome_do_artefato_vira_pasta_valida(string nome, string esperado) =>
        Assert.Equal(esperado, GitHubService.NomeDePasta(nome));

    [Theory]
    [InlineData(820, "820 B")]
    [InlineData(14540, "14,2 KB")]
    [InlineData(96615044, "92,1 MB")]
    public void Tamanho_vira_texto_curto(long bytes, string esperado) =>
        Assert.Equal(esperado, Rotulos.Bytes(bytes));

    [Fact]
    public void Jobs_trazem_o_id_e_o_horario_dos_passos()
    {
        var jobs = GitHubService.LerJobs("""
        {"jobs":[{"id":555,"name":"build","status":"completed","conclusion":"success",
          "steps":[{"name":"x","status":"completed","conclusion":"success","number":1,
                    "started_at":"2026-10-15T12:00:05Z","completed_at":"2026-10-15T12:00:08Z"}]}]}
        """);

        Assert.Equal(555, jobs[0].Id);
        Assert.Equal(T("12:00:05"), jobs[0].Etapas[0].Inicio);
        Assert.Equal(T("12:00:08"), jobs[0].Etapas[0].Fim);
    }
}
