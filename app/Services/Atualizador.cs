using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>
/// Atualização do próprio app pela release do GitHub.
///
/// O Windows não deixa sobrescrever um .exe em execução, mas deixa **renomear**: é nisso
/// que a troca se apoia, e é o que dispensa um .bat auxiliar esperando o app fechar.
/// A sequência é atual -> .old, novo -> atual, reabre, e o .old some na próxima abertura.
/// </summary>
public static class Atualizador
{
    /// <summary>Repositório de onde o próprio app se atualiza.</summary>
    public const string Slug = "GFBmsoft/GRepos";

    /// <summary>Sufixo do executável antigo, apagado na abertura seguinte.</summary>
    public const string SufixoAntigo = ".old";

    /// <summary>De quanto em quanto tempo vale consultar a API pela release.</summary>
    public static readonly TimeSpan IntervaloDeChecagem = TimeSpan.FromHours(24);

    /// <summary>Download de 90 MB não cabe no prazo curto das consultas à API.</summary>
    private static readonly TimeSpan PrazoDownload = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Versão da release é maior que a em execução? Compara só a parte numérica: um
    /// "1.0.0.1-dev.5" foi compilado **depois** da release 1.0.0.1, então não é atraso.
    /// Sem versão carimbada (build local) nunca há atualização a oferecer.
    /// </summary>
    public static bool TemNovidade(string versaoAtual, string tagDaRelease)
    {
        var atual = Numeros(versaoAtual);
        var nova = Numeros(tagDaRelease);
        return atual is not null && nova is not null && nova > atual;
    }

    private static Version? Numeros(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var limpo = texto.Trim().TrimStart('v', 'V').Split('+')[0].Split('-')[0];
        return Version.TryParse(limpo, out var v) && limpo.Split('.').Length == 4 ? v : null;
    }

    /// <summary>
    /// Estamos num executável de arquivo único? Num app publicado assim o
    /// <c>Location</c> do assembly de entrada vem **vazio**, porque não existe .dll em
    /// disco para apontar. Num build de pasta ele é o caminho do GRepos.dll.
    ///
    /// Isso não depende do nome do arquivo — e o nome é justamente o que o usuário pode
    /// mudar. Procurar um ".dll de mesmo nome" dava falso positivo em qualquer .exe
    /// renomeado dentro do `dist`.
    /// </summary>
    public static bool ArquivoUnico(string? localDoAssembly) => string.IsNullOrEmpty(localDoAssembly);

    /// <summary>
    /// A troca automática só vale no arquivo único e com a pasta gravável. Num build de
    /// pasta, trocar só o .exe deixaria as DLLs ao lado desencontradas; numa pasta sem
    /// permissão (Arquivos de Programas sem elevação) a renomeação falharia no meio.
    /// Nos dois casos resta abrir a página da release.
    /// </summary>
    public static bool PodeTrocarSozinho(string? caminhoDoExe, string? localDoAssembly)
    {
        if (string.IsNullOrEmpty(caminhoDoExe)) return false;
        if (!ArquivoUnico(localDoAssembly)) return false;

        var pasta = Path.GetDirectoryName(caminhoDoExe);
        return !string.IsNullOrEmpty(pasta) && PastaGravavel(pasta);
    }

    /// <summary>Como está rodando agora: pergunta ao próprio assembly onde ele está.</summary>
    public static bool PodeTrocarSozinho(string? caminhoDoExe) =>
        PodeTrocarSozinho(caminhoDoExe,
            System.Reflection.Assembly.GetEntryAssembly()?.Location);

    /// <summary>Descobre gravando: permissão no Windows não se deduz pelo caminho.</summary>
    public static bool PastaGravavel(string pasta)
    {
        try
        {
            var teste = Path.Combine(pasta, ".grepos-" + Path.GetRandomFileName());
            using (File.Create(teste)) { }
            File.Delete(teste);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string? CaminhoDoExe() => Environment.ProcessPath;

    /// <summary>
    /// Apaga o executável antigo deixado pela troca anterior. Roda na abertura, quando
    /// ele já não está em uso; falhar aqui não é problema, tenta-se de novo depois.
    /// </summary>
    public static void LimparAntigo(string? caminhoDoExe = null)
    {
        try
        {
            var exe = caminhoDoExe ?? CaminhoDoExe();
            if (string.IsNullOrEmpty(exe)) return;

            var antigo = exe + SufixoAntigo;
            if (File.Exists(antigo)) File.Delete(antigo);
        }
        catch (Exception)
        {
            // ainda travado por algum processo; some na próxima abertura
        }
    }

    /// <summary>
    /// Baixa o arquivo da release para um temporário ao lado do executável — mesma
    /// unidade, porque a troca é feita por renomeação, que não cruza volumes.
    /// </summary>
    /// <param name="progresso">Fração baixada, de 0 a 1, para a barra de status.</param>
    public static async Task<string> BaixarAsync(
        ReleaseAsset arquivo, string destinoPasta, IProgress<double>? progresso = null)
    {
        if (!arquivo.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A release aponta para um endereço que não é HTTPS.");

        var destino = Path.Combine(destinoPasta, arquivo.Nome + ".baixando");
        if (File.Exists(destino)) File.Delete(destino);

        using var http = new HttpClient { Timeout = PrazoDownload };
        http.DefaultRequestHeaders.UserAgent.Add(
            new System.Net.Http.Headers.ProductInfoHeaderValue("GRepos", "1.0"));

        using (var resp = await http.GetAsync(arquivo.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? arquivo.Tamanho;
            await using var origem = await resp.Content.ReadAsStreamAsync();
            await using var saida = File.Create(destino);

            var buffer = new byte[81920];
            long lido = 0;
            int n;
            while ((n = await origem.ReadAsync(buffer)) > 0)
            {
                await saida.WriteAsync(buffer.AsMemory(0, n));
                lido += n;
                if (total > 0) progresso?.Report((double)lido / total);
            }
        }

        // Sem assinatura para conferir, o tamanho anunciado pela API é a única checagem
        // que dá para fazer — pega download cortado, que é a falha provável.
        var baixado = new FileInfo(destino).Length;
        if (arquivo.Tamanho > 0 && baixado != arquivo.Tamanho)
        {
            File.Delete(destino);
            throw new InvalidOperationException(
                $"O download veio incompleto ({baixado:N0} de {arquivo.Tamanho:N0} bytes).");
        }

        return destino;
    }

    /// <summary>
    /// Põe o novo no lugar do atual. O atual vira ".old" em vez de ser apagado: ele está
    /// em execução, e é também o caminho de volta se algo der errado no meio.
    /// </summary>
    public static void Trocar(string exeAtual, string exeNovo)
    {
        var antigo = exeAtual + SufixoAntigo;
        if (File.Exists(antigo)) File.Delete(antigo);

        File.Move(exeAtual, antigo);
        try
        {
            File.Move(exeNovo, exeAtual);
        }
        catch (Exception)
        {
            File.Move(antigo, exeAtual); // desfaz: melhor a versão antiga que nenhuma
            throw;
        }
    }

    /// <summary>Abre o executável recém-instalado. Quem chamou encerra este processo.</summary>
    public static void Reabrir(string exe) =>
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
}
