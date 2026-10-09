using System;
using System.Collections.Generic;
using System.Linq;
using GRepos.Models;

namespace GRepos.Services;

/// <summary>Um grupo na posição dele na árvore: o nível e o caminho desde a raiz.</summary>
public sealed record GrupoNaArvore(Group Grupo, int Nivel, string Caminho)
{
    /// <summary>Nome recuado pelo nível, para caixas de escolha ("   Subpasta").</summary>
    public string Recuado => new string(' ', Nivel * 3) + Grupo.Name;
}

/// <summary>
/// Grupos dentro de grupos: pasta, subpasta e, no fim, os repositórios. O workspace
/// guarda a lista plana, cada grupo com o pai; aqui fica a leitura dela como árvore.
/// </summary>
public static class GrupoArvore
{
    /// <summary>Limite de níveis seguidos: protege contra um ciclo gravado à mão no JSON.</summary>
    private const int Fundo = 32;

    private static string? Pai(Group g, IReadOnlyCollection<Group> todos) =>
        string.IsNullOrEmpty(g.ParentId) || g.ParentId == g.Id || todos.All(x => x.Id != g.ParentId)
            ? null // pai que não existe mais: o grupo volta para a raiz em vez de sumir
            : g.ParentId;

    /// <summary>Os filhos diretos de um grupo (ou as raízes, com <c>null</c>), em ordem alfabética.</summary>
    public static List<Group> Filhos(IReadOnlyCollection<Group> todos, string? paiId) =>
        todos.Where(g => Pai(g, todos) == (string.IsNullOrEmpty(paiId) ? null : paiId))
             .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Todos os grupos em ordem de árvore: cada pai seguido dos descendentes.</summary>
    public static List<GrupoNaArvore> EmOrdem(IReadOnlyCollection<Group> todos)
    {
        var saida = new List<GrupoNaArvore>();
        var vistos = new HashSet<string>();

        void Descer(string? paiId, int nivel, string caminho)
        {
            if (nivel > Fundo) return;
            foreach (var g in Filhos(todos, paiId))
            {
                if (!vistos.Add(g.Id)) continue;
                var aqui = caminho.Length > 0 ? $"{caminho} / {g.Name}" : g.Name;
                saida.Add(new GrupoNaArvore(g, nivel, aqui));
                Descer(g.Id, nivel + 1, aqui);
            }
        }

        Descer(null, 0, "");

        // o que sobrou está preso num ciclo (A dentro de B dentro de A): aparece na raiz
        foreach (var g in todos.Where(g => !vistos.Contains(g.Id)))
            saida.Add(new GrupoNaArvore(g, 0, g.Name));

        return saida;
    }

    /// <summary>O grupo e tudo que está abaixo dele.</summary>
    public static HashSet<string> ComDescendentes(IReadOnlyCollection<Group> todos, string id)
    {
        var ids = new HashSet<string> { id };
        var fila = new Queue<string>();
        fila.Enqueue(id);
        while (fila.Count > 0)
        {
            var atual = fila.Dequeue();
            foreach (var filho in todos.Where(g => g.ParentId == atual && g.Id != atual))
                if (ids.Add(filho.Id)) fila.Enqueue(filho.Id);
        }
        return ids;
    }

    /// <summary>Do grupo até a raiz, ele primeiro. Para abrir os pais de um repositório escondido.</summary>
    public static List<Group> Ancestrais(IReadOnlyCollection<Group> todos, string? id)
    {
        var saida = new List<Group>();
        var vistos = new HashSet<string>();
        while (!string.IsNullOrEmpty(id) && vistos.Add(id) && todos.FirstOrDefault(g => g.Id == id) is { } g)
        {
            saida.Add(g);
            id = Pai(g, todos);
        }
        return saida;
    }

    /// <summary>"Pasta / Subpasta": o nome com o caminho, para onde o nível não aparece desenhado.</summary>
    public static string Caminho(IReadOnlyCollection<Group> todos, string? id)
    {
        var nomes = Ancestrais(todos, id).Select(g => g.Name).Reverse().ToList();
        return nomes.Count > 0 ? string.Join(" / ", nomes) : "Sem grupo";
    }

    /// <summary>
    /// Um grupo não pode ir para dentro de si mesmo nem de um descendente: viraria um
    /// ciclo, e ele e tudo abaixo sumiriam da árvore.
    /// </summary>
    public static bool PodeFicarDentro(IReadOnlyCollection<Group> todos, string id, string? novoPai) =>
        string.IsNullOrEmpty(novoPai) || !ComDescendentes(todos, id).Contains(novoPai);
}
