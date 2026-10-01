using System;
using System.IO;
using System.Runtime.CompilerServices;
using GRepos.Services;

namespace GRepos.Tests;

/// <summary>
/// Trava o workspace dos testes numa pasta temporária **antes de qualquer teste rodar**.
///
/// Existe porque a proteção anterior não bastou: cada teste definia GREPOS_HOME para uma
/// pasta própria, mas o xunit roda classes em paralelo e variável de ambiente é global ao
/// processo. Uma classe zerou a variável enquanto outra ainda gravava, e a gravação caiu
/// no %APPDATA% real — apagando o workspace do usuário.
///
/// Aqui o caminho padrão do WorkspaceStore é redirecionado no carregamento do assembly,
/// de uma vez. Assim, mesmo que um teste esqueça a variável, ou que outro a zere no meio,
/// não existe caminho de código que alcance o arquivo real.
/// </summary>
internal static class IsolamentoDoWorkspace
{
    [ModuleInitializer]
    internal static void Ativar()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-testes-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(raiz);

        WorkspaceStore.PastaPadraoDeTeste = Path.Combine(raiz, "perfil");
        Directory.CreateDirectory(WorkspaceStore.PastaPadraoDeTeste);

        // a pasta "do executável" também sai do lugar: o modo portátil grava nela, e o
        // executável dos testes fica em bin/, que é do projeto
        WorkspaceStore.PastaDoAppDeTeste = Path.Combine(raiz, "app");
        Directory.CreateDirectory(WorkspaceStore.PastaDoAppDeTeste);
    }
}

/// <summary>
/// Classes que mexem em estado global: GREPOS_HOME, os desvios do WorkspaceStore e a
/// conta do GitService (CredentialUser). A variável é
/// global ao processo: com essas classes em paralelo, uma definia a pasta enquanto outra
/// zerava e conferia o caminho padrão — o teste do modo portátil falhava ao acaso e
/// derrubava o build da tag. Na mesma coleção, e sem paralelismo, elas rodam uma de cada vez.
/// </summary>
[Xunit.CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class WorkspaceGlobal
{
    public const string Nome = "Workspace global";
}
