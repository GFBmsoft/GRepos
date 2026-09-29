# GRepos

Cliente Git desktop para quem trabalha com **muitos repositórios ao mesmo tempo** —
substituto do SourceTree com foco em organização: a sidebar mostra todos os repositórios
agrupados, já com status (ahead/behind/sujo/conflito) de todos de uma vez, sem abrir uma
aba por repositório.

Traz o conceito de **par Origem × Destino**: dois repositórios do mesmo módulo em bancos
diferentes (por exemplo DBISAM e MySQL) ficam sob um único título na árvore e ganham uma aba
que mostra os dois lado a lado, com Fetch/Pull nos dois de uma vez.

Aplicação nativa em C# / .NET 8 com Avalonia. Não precisa de Node, Rust nem Build Tools:
o SDK do .NET resolve tudo.

## Comandos

```bash
dotnet run --project app      # abre o aplicativo
dotnet build                  # compila tudo
dotnet test                   # 15 testes (parser, grafo, workspace e telas)
dotnet publish app -c Release -r win-x64 --self-contained false -o dist   # gera dist/GRepos.exe
```

## Como usar

1. **+ Repositório** — escolha a pasta; o nome vem do próprio repositório.
2. **+ Grupo** — crie grupos (por módulo, por cliente, por banco) e mova repositórios pelo
   botão ⚙ da barra superior. Grupos recolhem com um clique no título.
3. **Par Origem × Destino** — no ⚙ do repositório, preencha *Par* com a mesma chave nos dois
   repositórios (ex.: `Financeiro`) e marque o papel de cada um. Eles passam a aparecer sob
   um único título e a aba **Par** fica disponível.
4. **Alterações** — preparar/remover por arquivo ou **por bloco** (botão no cabeçalho de cada
   bloco do diff), descartar, commit, emendar.
5. **Histórico** — grafo de commits com raias coloridas, refs, detalhe do commit e diff por
   arquivo. O botão `▥` alterna entre diff lado a lado e unificado.

Preferências (⚙ da sidebar): tema claro/escuro, cor de destaque, densidade das listas,
intervalo de atualização automática e quantidade de commits carregados.

## Onde ficam os dados

O workspace (grupos, repositórios, pares e preferências) fica em
`%APPDATA%\GRepos\workspace.json`. Defina a variável de ambiente `GREPOS_HOME` para usar
outra pasta — útil para instalação portátil ou para testar sem mexer no workspace real.

Nenhuma credencial é armazenada: todas as operações de rede usam o `git` do sistema, que
reaproveita o Git Credential Manager.

## Arquitetura

```
app/
  Services/GitService.cs     chamadas ao git CLI e parsing (status v2, log, branches)
  Services/DiffParser.cs     parser de diff unificado e montagem de patch de bloco
  Services/GraphBuilder.cs   layout de raias do grafo de commits
  Services/WorkspaceStore.cs modelo persistido em JSON (gravação atômica)
  ViewModels/                estado das telas (CommunityToolkit.Mvvm)
  Views/                     janela principal, abas e diálogos (Avalonia)
  Controls/GraphCell.cs      desenho das raias do grafo
tests/                       xunit: parser, grafo, workspace e montagem das telas
```

A camada Git usa o **git CLI** em vez de uma biblioteca: herda credenciais, SSH, hooks e
configuração do usuário sem reimplementar nada.

## Limitações conhecidas

- Sem merge/rebase interativo, resolução de conflitos ou blame — conflitos aparecem
  sinalizados, mas a resolução é feita fora.
- Sem realce de sintaxe no diff (só realce de adição/remoção).
- A aba **Par** compara os commits pelo assunto, não por conteúdo.
