# CLAUDE.md

Orientações para trabalhar neste repositório.

## O que é

GRepos: cliente Git desktop (C# / .NET 8 + Avalonia) para gerenciar muitos repositórios sem
uma aba por repositório. Diferencial: grupos na sidebar e o **par Origem × Destino** — o
mesmo módulo em dois bancos (DBISAM/MySQL) tratado como uma entidade só.

## Comandos

```bash
dotnet run --project app   # abre o aplicativo
dotnet publish app -c Release -r win-x64 --self-contained false -o dist   # gera dist/GRepos.exe
dotnet build               # compila
dotnet test                # xunit (parser, grafo, workspace e telas headless)
```

`GREPOS_HOME` aponta o workspace para outra pasta — use sempre isso ao testar, para não
escrever no `%APPDATA%\GRepos\workspace.json` real do usuário.

## Arquitetura

- **Services** — `GitService` executa o `git` CLI (`git -C <repo> ...`) e faz o parsing;
  `WorkspaceStore` persiste o workspace em JSON (grava em `.tmp` e renomeia);
  `DiffParser` e `GraphBuilder` são lógica pura, sem dependência de UI.
- **ViewModels** — CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
  `MainViewModel` é o centro: workspace, árvore da sidebar, seleção e comandos do repositório.
  Diálogos e seletor de pasta entram por `IDialogService` (implementado pela `MainWindow`).
- **Views** — Avalonia XAML; `DiffView` é reutilizada por Alterações e Histórico.

## Publicação

**Sempre publicar com `-o dist`.** Sem isso o `dotnet publish -r win-x64` grava em
`app/bin/Release/net8.0/win-x64/publish/`, e sobra um executável antigo em
`app/bin/Release/net8.0/` — já aconteceu de o usuário abrir o errado e testar a versão
velha. A barra de status mostra a data do executável em uso, justamente para conferir.

## Regras

- Nada de reimplementar Git: o CLI já traz credenciais, SSH e hooks.
- Lógica não trivial nova vai para `Services` (testável sem UI), não para o code-behind.
- **Bindings**: este projeto usa bindings reflexivos (`AvaloniaUseCompiledBindingsByDefault`
  é `false`). Cast com prefixo de namespace dentro de binding — `((vm:Tipo)DataContext)` —
  compila mas **explode em runtime**. Use `$parent[ListBox].DataContext.Comando`.
- Toda tela nova ganha um teste em `UiSmokeTests` com as listas **populadas**: erro de
  binding só aparece quando o `ItemTemplate` é realmente construído.
- Comentário XML/XAML não pode conter `--` (quebra o build do Avalonia).
- `Grid` do Avalonia 11.2 não tem `ColumnSpacing`/`RowSpacing`; use `Margin`.
- Ao mexer no parsing do `git status --porcelain=v2`, confira a contagem de campos: são 7
  antes do caminho na linha `1`, 8 na `2` (renomeado) e 9 na `u` (conflito). Já errei isso.
- **Um `git status` por recarga.** `StatusAndChangesAsync` devolve status e lista juntos;
  chamar `StatusAsync` e `ChangesAsync` em sequência dobra o custo. O contador de stash sai
  do reflog em disco (`.git/logs/refs/stash`), não de `git stash list`.
- `/dev/null` não existe no Windows: diff de arquivo novo é montado por `NewFileDiff`, no
  formato que o `git apply` aceita (é o que faz o botão "Preparar bloco" funcionar neles).
- A tela se atualiza sozinha pelo `RepoWatcher` (FileSystemWatcher com debounce). Ao mexer
  em recarga, lembre que ela dispara a cada salvamento: recarga tem que ser barata e não
  pode roubar seleção nem posição de rolagem do usuário.
- Antes de otimizar, **meça**: o relato de lentidão em um repositório de 3,6 mil arquivos
  era falta de atualização automática, não custo do git (que respondia em ~60 ms).
- O usuário das preferências só serve ao git se chegar em `GitService.CredentialUser`
  (feito no `InitAsync` e no `SetGithubUser`). Sem isso o git procura credencial sem
  conta, não acha o token e abre a janela de login a cada envio.
- Operação destrutiva (descartar, dropar stash, remover repositório) sempre confirma antes.
- Textos de interface em pt-br com acentuação correta.
