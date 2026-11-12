using System;
using System.Collections.Generic;
using GRepos.Models;

namespace GRepos.Services;

/// <summary>
/// O que o Enviar leva ao remoto: os commits e os arquivos distintos que eles mudam. O selo
/// do botão mostra os arquivos, que é o número que se confere com o que foi commitado: com
/// os commits, um commit de três arquivos mostrava "1" e parecia contagem errada.
/// </summary>
public sealed record EnvioPendente(int Commits, int Arquivos)
{
    /// <summary>Marca de commit no log: um caractere que não aparece em nome de arquivo.</summary>
    public const string FormatoDoLog = "--format=%x01";

    private const char Marca = '\u0001';

    public static readonly EnvioPendente Nada = new(0, 0);

    /// <summary>Vazio esconde o selo, como nos outros botões da barra.</summary>
    public string Selo => Arquivos > 0 ? Arquivos.ToString() : "";

    /// <summary>
    /// Lê "git log --format=%x01 --name-only": uma linha com a marca por commit e, depois
    /// dela, os arquivos que ele mudou. O mesmo arquivo em dois commits conta uma vez.
    /// </summary>
    public static EnvioPendente DoLog(string saida)
    {
        var commits = 0;
        var arquivos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bruta in saida.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            if (linha.Length == 0) continue;
            if (linha[0] == Marca) commits++;
            else arquivos.Add(linha);
        }
        return new EnvioPendente(commits, arquivos.Count);
    }

    /// <summary>Lê "git diff --name-only": um arquivo por linha.</summary>
    public static int ContarArquivos(string saida)
    {
        var arquivos = 0;
        foreach (var linha in saida.Split('\n'))
            if (linha.Trim().Length > 0) arquivos++;
        return arquivos;
    }

    /// <summary>A dica do botão: os dois números, já que o selo só mostra os arquivos.</summary>
    public string Dica(RepoStatus status)
    {
        var oQue = Commits == 0 ? ""
            : Arquivos == 0 ? $"{MensagensGit.Commits(Commits)}, sem arquivo alterado"
            : $"{MensagensGit.Commits(Commits)} com {MensagensGit.Arquivos(Arquivos)}";

        // o par foi apagado no remoto (ou o remoto está vazio): o envio o cria de novo
        if (status.ParAusente)
        {
            var oQueVai = oQue.Length == 0 ? $"a branch {status.Branch}" : oQue;
            return $"Enviar {oQueVai} e criar {status.Upstream}, que não existe no remoto (push)";
        }

        // branch nova: o envio a cria no remoto, com ou sem commit próprio
        if (string.IsNullOrEmpty(status.Upstream))
        {
            var nova = $"Enviar a branch {status.Branch}, que ainda não existe no remoto";
            return oQue.Length == 0 ? $"{nova} (push)" : $"{nova}: {oQue} (push)";
        }

        return oQue.Length == 0 ? "Enviar os commits locais (push)" : $"Enviar {oQue} (push)";
    }
}
