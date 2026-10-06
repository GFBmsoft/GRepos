using System;
using System.Collections.Generic;
using System.IO;

namespace GRepos.Services;

public enum TipoTrecho { Palavra, Literal, Comentario, Numero, Funcao }

/// <summary>Pedaço colorido de uma linha; o que fica entre dois trechos é texto comum.</summary>
public readonly record struct Trecho(int Inicio, int Tamanho, TipoTrecho Tipo);

/// <summary>O que o realce precisa saber de uma linguagem: palavras, comentários e textos.</summary>
public sealed class Linguagem
{
    public string Nome { get; init; } = "";
    public HashSet<string> Palavras { get; init; } = new();
    public string[] ComentarioDeLinha { get; init; } = Array.Empty<string>();
    public (string Abre, string Fecha)[] ComentarioDeBloco { get; init; } = Array.Empty<(string, string)>();
    public string Aspas { get; init; } = "\"'";

    /// <summary>A barra invertida escapa a aspa (C e derivados); no Pascal e no SQL, não.</summary>
    public bool Escape { get; init; }

    /// <summary>Marcação: o nome depois de "&lt;" é palavra e o que vem antes de "=" é atributo.</summary>
    public bool Marcacao { get; init; }

    /// <summary>Pascal: <c>$FF</c> e <c>#13</c> são números.</summary>
    public bool NumerosPascal { get; init; }
}

/// <summary>
/// Realce de sintaxe do diff, linha a linha. É um léxico simples — palavras reservadas,
/// textos, comentários, números e nomes de função — e não um analisador: basta para a
/// leitura e não pesa na rolagem. O estado entre linhas só guarda o comentário de bloco
/// aberto; um bloco que começa no meio de um comentário é lido como código até ele fechar.
/// </summary>
public static class Realce
{
    private static HashSet<string> Conjunto(bool ignoraCaixa, string palavras) =>
        new(palavras.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ignoraCaixa ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly Linguagem Pascal = new()
    {
        Nome = "pascal",
        Palavras = Conjunto(true,
            "and array as asm begin case class const constructor destructor dispinterface div do downto else end " +
            "except exports file finalization finally for function goto if implementation in inherited " +
            "initialization inline interface is label library mod nil not object of or packed procedure program " +
            "property raise record repeat resourcestring set shl shr string then threadvar to try type unit until " +
            "uses var while with xor private protected public published strict override virtual abstract dynamic " +
            "overload reintroduce default read write stdcall cdecl forward out on absolute external " +
            "true false self result exit break continue"),
        ComentarioDeLinha = new[] { "//" },
        ComentarioDeBloco = new[] { ("{", "}"), ("(*", "*)") },
        Aspas = "'",
        NumerosPascal = true,
    };

    private static readonly Linguagem Formulario = new()
    {
        Nome = "dfm",
        Palavras = Conjunto(true, "object inherited inline end item true false"),
        Aspas = "'",
        NumerosPascal = true,
    };

    private static readonly Linguagem CSharp = new()
    {
        Nome = "csharp",
        Palavras = Conjunto(false,
            "abstract as async await base bool break byte case catch char checked class const continue decimal " +
            "default delegate do double else enum event explicit extern false finally fixed float for foreach get " +
            "goto if implicit in init int interface internal is lock long namespace new not null object operator " +
            "or out override params partial private protected public readonly record ref required return sbyte " +
            "sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong " +
            "unchecked unsafe ushort using var virtual void volatile when where while with yield and nameof"),
        ComentarioDeLinha = new[] { "//" },
        ComentarioDeBloco = new[] { ("/*", "*/") },
        Escape = true,
    };

    private static readonly Linguagem TipoC = new()
    {
        Nome = "c",
        Palavras = Conjunto(false,
            "abstract any as async await bool boolean break case catch char class const constructor continue debugger " +
            "default defer delete do double else enum export extends false final finally float fn for from func " +
            "function go if implements import in instanceof int interface let long map match mut namespace new nil " +
            "null number of package private protected pub public range readonly return select short static string " +
            "struct super switch this throw throws true try type typeof undefined unsigned use var void while yield"),
        ComentarioDeLinha = new[] { "//" },
        ComentarioDeBloco = new[] { ("/*", "*/") },
        Aspas = "\"'`",
        Escape = true,
    };

    private static readonly Linguagem Sql = new()
    {
        Nome = "sql",
        Palavras = Conjunto(true,
            "add all alter and as asc begin between by case column commit constraint create database declare default " +
            "delete desc distinct drop else end exists foreign from full function group having if in index inner " +
            "insert into is join key left like limit not null on or order outer primary procedure references " +
            "return returns right rollback select set table then top trigger union unique update values view when " +
            "where while with"),
        ComentarioDeLinha = new[] { "--" },
        ComentarioDeBloco = new[] { ("/*", "*/") },
        Aspas = "'",
    };

    private static readonly Linguagem Xml = new()
    {
        Nome = "xml",
        ComentarioDeBloco = new[] { ("<!--", "-->") },
        Marcacao = true,
    };

    private static readonly Linguagem Json = new()
    {
        Nome = "json",
        Palavras = Conjunto(false, "true false null"),
        Aspas = "\"",
        Escape = true,
    };

    private static readonly Linguagem Script = new()
    {
        Nome = "script",
        Palavras = Conjunto(false,
            "and as break case class continue def do done elif else esac except false False fi finally for from " +
            "function if import in is lambda None not null or pass raise return then true True try while with yield"),
        ComentarioDeLinha = new[] { "#" },
        Escape = true,
    };

    private static readonly Linguagem Ini = new()
    {
        Nome = "ini",
        ComentarioDeLinha = new[] { ";", "#" },
    };

    private static readonly Dictionary<string, Linguagem> PorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pas"] = Pascal, [".dpr"] = Pascal, [".dpk"] = Pascal, [".inc"] = Pascal, [".lpr"] = Pascal, [".pp"] = Pascal,
        [".dfm"] = Formulario, [".fmx"] = Formulario, [".lfm"] = Formulario,
        [".cs"] = CSharp,
        [".js"] = TipoC, [".jsx"] = TipoC, [".ts"] = TipoC, [".tsx"] = TipoC, [".java"] = TipoC, [".c"] = TipoC,
        [".h"] = TipoC, [".cpp"] = TipoC, [".hpp"] = TipoC, [".go"] = TipoC, [".php"] = TipoC, [".kt"] = TipoC,
        [".rs"] = TipoC, [".swift"] = TipoC, [".css"] = TipoC,
        [".sql"] = Sql,
        [".xml"] = Xml, [".axaml"] = Xml, [".xaml"] = Xml, [".csproj"] = Xml, [".dproj"] = Xml, [".groupproj"] = Xml,
        [".html"] = Xml, [".htm"] = Xml, [".svg"] = Xml, [".config"] = Xml, [".props"] = Xml, [".targets"] = Xml,
        [".resx"] = Xml, [".manifest"] = Xml, [".fr3"] = Xml,
        [".json"] = Json,
        [".py"] = Script, [".sh"] = Script, [".yml"] = Script, [".yaml"] = Script, [".toml"] = Script, [".ps1"] = Script,
        [".ini"] = Ini, [".cfg"] = Ini, [".conf"] = Ini,
    };

    /// <summary>Linguagem pelo nome do arquivo; null para o que não é reconhecido (fica sem realce).</summary>
    public static Linguagem? Para(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        var ext = Path.GetExtension(caminho.Trim());
        return ext.Length > 0 && PorExtensao.TryGetValue(ext, out var l) ? l : null;
    }

    /// <param name="estado">
    /// 0, ou o índice + 1 do comentário de bloco que a linha anterior deixou aberto. Sai
    /// atualizado para a linha seguinte.
    /// </param>
    public static List<Trecho> Linha(string texto, Linguagem ling, ref int estado)
    {
        var trechos = new List<Trecho>();
        var i = 0;

        if (estado > 0)
        {
            var fecha = ling.ComentarioDeBloco[estado - 1].Fecha;
            var fim = texto.IndexOf(fecha, StringComparison.Ordinal);
            if (fim < 0)
            {
                if (texto.Length > 0) trechos.Add(new Trecho(0, texto.Length, TipoTrecho.Comentario));
                return trechos;
            }
            i = fim + fecha.Length;
            trechos.Add(new Trecho(0, i, TipoTrecho.Comentario));
            estado = 0;
        }

        while (i < texto.Length)
        {
            var c = texto[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (ComecaCom(texto, i, ling.ComentarioDeLinha) >= 0)
            {
                trechos.Add(new Trecho(i, texto.Length - i, TipoTrecho.Comentario));
                break;
            }

            var bloco = -1;
            for (var k = 0; k < ling.ComentarioDeBloco.Length && bloco < 0; k++)
                if (string.CompareOrdinal(texto, i, ling.ComentarioDeBloco[k].Abre, 0, ling.ComentarioDeBloco[k].Abre.Length) == 0)
                    bloco = k;
            if (bloco >= 0)
            {
                var (abre, fecha) = ling.ComentarioDeBloco[bloco];
                var fim = texto.IndexOf(fecha, i + abre.Length, StringComparison.Ordinal);
                if (fim < 0)
                {
                    trechos.Add(new Trecho(i, texto.Length - i, TipoTrecho.Comentario));
                    estado = bloco + 1;
                    break;
                }
                trechos.Add(new Trecho(i, fim + fecha.Length - i, TipoTrecho.Comentario));
                i = fim + fecha.Length;
                continue;
            }

            if (ling.Aspas.Contains(c))
            {
                var j = i + 1;
                while (j < texto.Length && texto[j] != c)
                    j += ling.Escape && texto[j] == '\\' ? 2 : 1;
                j = Math.Min(j + 1, texto.Length); // sem aspa de fechamento vai até o fim da linha
                trechos.Add(new Trecho(i, j - i, TipoTrecho.Literal));
                i = j;
                continue;
            }

            if (char.IsDigit(c) || (ling.NumerosPascal && c is '$' or '#' && i + 1 < texto.Length && Uri.IsHexDigit(texto[i + 1])))
            {
                var j = i + 1;
                while (j < texto.Length &&
                       (char.IsLetterOrDigit(texto[j]) || texto[j] == '_' ||
                        (texto[j] == '.' && j + 1 < texto.Length && char.IsDigit(texto[j + 1]))))
                    j++;
                trechos.Add(new Trecho(i, j - i, TipoTrecho.Numero));
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i + 1;
                while (j < texto.Length && ParteDoNome(texto[j], ling.Marcacao)) j++;

                if (TipoDoNome(texto, i, j, ling) is { } tipo) trechos.Add(new Trecho(i, j - i, tipo));
                i = j;
                continue;
            }

            i++;
        }

        return trechos;
    }

    private static bool ParteDoNome(char c, bool marcacao) =>
        char.IsLetterOrDigit(c) || c == '_' || (marcacao && c is ':' or '.' or '-');

    private static TipoTrecho? TipoDoNome(string texto, int inicio, int fim, Linguagem ling)
    {
        var depois = fim;
        while (depois < texto.Length && texto[depois] == ' ') depois++;
        var seguinte = depois < texto.Length ? texto[depois] : '\0';

        if (ling.Marcacao)
        {
            var antes = inicio - 1;
            if (antes >= 0 && texto[antes] == '/') antes--;
            if (antes >= 0 && texto[antes] == '<') return TipoTrecho.Palavra;
            return seguinte == '=' ? TipoTrecho.Funcao : null;
        }

        if (ling.Palavras.Contains(texto[inicio..fim])) return TipoTrecho.Palavra;
        return seguinte == '(' ? TipoTrecho.Funcao : null;
    }

    private static int ComecaCom(string texto, int i, string[] prefixos)
    {
        for (var k = 0; k < prefixos.Length; k++)
            if (string.CompareOrdinal(texto, i, prefixos[k], 0, prefixos[k].Length) == 0)
                return k;
        return -1;
    }
}
