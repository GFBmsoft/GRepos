using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>GitHub lento: uma nova tentativa na consulta e mensagem em pt-br, sem rede.</summary>
public class PrazoHttpTests
{
    /// <summary>Responde depois de <c>atrasos[n]</c> na n-ésima chamada.</summary>
    private sealed class Lento : HttpMessageHandler
    {
        private readonly TimeSpan[] _atrasos;
        public int Chamadas;

        public Lento(params TimeSpan[] atrasos) => _atrasos = atrasos;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var atraso = _atrasos[Math.Min(Chamadas++, _atrasos.Length - 1)];
            await Task.Delay(atraso, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static readonly TimeSpan Curto = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Longo = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Consulta_lenta_e_refeita_e_responde()
    {
        var lento = new Lento(Longo, TimeSpan.Zero);
        using var http = new HttpClient(new PrazoHttp(lento, Curto, Curto));

        using var resp = await http.GetAsync("https://api.github.com/x");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, lento.Chamadas);
    }

    [Fact]
    public async Task Duas_vezes_lenta_vira_mensagem_em_portugues()
    {
        using var http = new HttpClient(new PrazoHttp(new Lento(Longo), Curto, Curto));

        var erro = await Assert.ThrowsAsync<TimeoutException>(() => http.GetAsync("https://api.github.com/x"));
        Assert.StartsWith("O GitHub não respondeu", erro.Message);
    }

    [Fact]
    public async Task Envio_nao_e_repetido()
    {
        var lento = new Lento(Longo, TimeSpan.Zero);
        using var http = new HttpClient(new PrazoHttp(lento, Curto, Curto));

        await Assert.ThrowsAsync<TimeoutException>(() => http.PostAsync("https://api.github.com/x", new StringContent("{}")));
        Assert.Equal(1, lento.Chamadas);
    }
}
