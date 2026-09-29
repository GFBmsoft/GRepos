using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GRepos.Services;

/// <summary>
/// Atualiza uma coleção observável no lugar, mexendo só no que mudou. Trocar a coleção
/// inteira faz a lista inteira ser recriada na tela — é o que provoca a piscada a cada
/// stage, e ainda perde rolagem e seleção.
/// </summary>
public static class ListaSync
{
    public static void Aplicar<T>(
        ObservableCollection<T> destino,
        IReadOnlyList<T> novos,
        Func<T, string> chave,
        Func<T, T, bool> equivalentes)
    {
        // remove o que saiu (de trás para a frente: índice não se desloca)
        for (var i = destino.Count - 1; i >= 0; i--)
        {
            var atual = chave(destino[i]);
            var continua = false;
            foreach (var n in novos)
                if (chave(n) == atual) { continua = true; break; }

            if (!continua) destino.RemoveAt(i);
        }

        for (var i = 0; i < novos.Count; i++)
        {
            var novo = novos[i];
            var k = chave(novo);

            var posicao = -1;
            for (var j = i; j < destino.Count; j++)
                if (chave(destino[j]) == k) { posicao = j; break; }

            if (posicao < 0)
            {
                destino.Insert(Math.Min(i, destino.Count), novo);
                continue;
            }

            if (posicao != i) destino.Move(posicao, i);

            // mesmo caminho, conteúdo diferente (ex.: o status do arquivo mudou)
            if (!equivalentes(destino[i], novo)) destino[i] = novo;
        }

        while (destino.Count > novos.Count) destino.RemoveAt(destino.Count - 1);
    }
}
