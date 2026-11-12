namespace GRepos.Services;

/// <summary>
/// O que o Puxar traz do remoto: os commits e os arquivos distintos que eles mudam aqui. O
/// selo mostra os arquivos, como o do Enviar: lado a lado, os dois contam a mesma coisa.
/// </summary>
public sealed record RecebimentoPendente(int Commits, int Arquivos)
{
    private const string Opcoes = "Clique direito: opções";

    /// <summary>A dica enquanto não há o que puxar, ou a contagem ainda não chegou.</summary>
    public const string DicaSemContagem = "Trazer os commits do remoto (pull). " + Opcoes;

    public static readonly RecebimentoPendente Nada = new(0, 0);

    /// <summary>Vazio esconde o selo, como nos outros botões da barra.</summary>
    public string Selo => Arquivos > 0 ? Arquivos.ToString() : "";

    /// <summary>A dica do botão: os dois números, já que o selo só mostra os arquivos.</summary>
    public string Dica => Commits == 0 ? DicaSemContagem
        : Arquivos == 0 ? $"Trazer {MensagensGit.Commits(Commits)} do remoto, sem arquivo alterado (pull). {Opcoes}"
        : $"Trazer {MensagensGit.Commits(Commits)} com {MensagensGit.Arquivos(Arquivos)} do remoto (pull). {Opcoes}";
}
