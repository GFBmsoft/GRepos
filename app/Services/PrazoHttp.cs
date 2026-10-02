using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>
/// Prazo por tentativa nas chamadas à API do GitHub. Antes era um Timeout de 8 s no
/// HttpClient: uma resposta lenta derrubava a consulta e o painel mostrava, em inglês,
/// "The request was canceled due to the configured HttpClient.Timeout…".
///
/// Consulta (GET) que estoura o prazo é refeita uma vez, com prazo maior. Envio (POST,
/// disparar esteira) não é repetido: o primeiro pode ter chegado ao GitHub.
/// </summary>
public sealed class PrazoHttp : DelegatingHandler
{
    private readonly TimeSpan[] _prazosConsulta;
    private readonly TimeSpan _prazoEnvio;

    public PrazoHttp(HttpMessageHandler interno, TimeSpan? primeira = null, TimeSpan? segunda = null)
        : base(interno)
    {
        _prazosConsulta = new[] { primeira ?? TimeSpan.FromSeconds(10), segunda ?? TimeSpan.FromSeconds(20) };
        _prazoEnvio = segunda ?? TimeSpan.FromSeconds(20);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var prazos = req.Method == HttpMethod.Get ? _prazosConsulta : new[] { _prazoEnvio };

        for (var i = 0; ; i++)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(prazos[i]);
            try
            {
                return await base.SendAsync(req, limite.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (i + 1 < prazos.Length) continue;
                throw new TimeoutException(
                    $"O GitHub não respondeu em {(int)prazos[i].TotalSeconds} segundos. " +
                    "A conexão pode estar lenta; tente de novo em instantes.");
            }
        }
    }
}
