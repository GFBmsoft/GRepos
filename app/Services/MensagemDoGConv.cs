using System;
using System.IO;
using System.Text.Json;

namespace GRepos.Services;

/// <summary>
/// A mensagem de commit que o GConv (o painel da conversão DBISAM → MySQL) deixa para um repositório: o resumo da
/// conversão, os pedidos, os commits da Origem com o texto que cada desenvolvedor escreveu, e as correções que rodaram em
/// cima. O GConv grava um arquivo por repositório em %LOCALAPPDATA%\GConv\commit\ ao fim da conversão e de cada correção
/// (o contrato é o MensagemParaOGRepos do GConv). Ela só vale com o HEAD e a branch de quando foi gravada: depois do
/// commit, ou de trocar de branch, é velha e fica de fora. A mensagem só entra no campo: quem revisa e commita é o usuário.
/// </summary>
public static class MensagemDoGConv
{
    /// <summary>Desvio dos testes: nunca a pasta real do GConv.</summary>
    internal static string? PastaDeTeste { get; set; }

    public static string Pasta => PastaDeTeste ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GConv", "commit");

    /// <param name="Descricao">De onde veio: "conversão do Notas em 09/11 10:24 + 1 correção".</param>
    public sealed record Sugestao(string Mensagem, string Descricao, string Arquivo, string Head, DateTime Quando)
    {
        /// <summary>Esta gravação: apagada do campo, ela não volta; a próxima que o GConv gravar, sim.</summary>
        public string Chave => $"{Arquivo}|{Head}|{Quando:O}";
    }

    private sealed class Arquivo
    {
        public string? Repositorio { get; set; }
        public string? Branch { get; set; }
        public string? Head { get; set; }
        public string? Mensagem { get; set; }
        public string? Descricao { get; set; }
        public DateTime Quando { get; set; }
    }

    /// <summary>
    /// A mensagem gravada para o repositório que ainda vale para este HEAD e esta branch, ou null. Lê os arquivos da pasta
    /// (um por repositório que passou pelo GConv, poucos); arquivo quebrado ou de outro repositório fica de fora.
    /// </summary>
    public static Sugestao? Achar(string repo, string head, string branch)
    {
        if (string.IsNullOrEmpty(head) || !Directory.Exists(Pasta)) return null;
        string alvo;
        try { alvo = Normalizar(repo); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }

        try
        {
            foreach (var f in Directory.EnumerateFiles(Pasta, "*.json"))
            {
                try
                {
                    var a = JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(f));
                    if (a?.Repositorio is null || a.Head is null || string.IsNullOrWhiteSpace(a.Mensagem)) continue;
                    if (!string.Equals(Normalizar(a.Repositorio), alvo, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(a.Head, head, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(a.Branch ?? "", branch ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    return new Sugestao(a.Mensagem.Replace("\r\n", "\n").Trim(), a.Descricao ?? "", f, a.Head, a.Quando);
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException or PathTooLongException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static string Normalizar(string caminho) => Path.GetFullPath(caminho).TrimEnd('\\', '/');
}
