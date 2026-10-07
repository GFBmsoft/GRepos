## 1.0.0.32 — 07/10/2026
- Terminal embutido: texto fosco aparece apagado, como nos outros terminais. A sugestão que o Claude Code oferece para completar com Tab deixa de se confundir com o que você digitou.

## 1.0.0.31 — 06/10/2026
- Com muitos repositórios o aplicativo abre sem travar: o painel aparece na hora e cada repositório mostra a branch e as pendências assim que responde, em vez de todos esperarem o último.
- **Atualizar** clicado durante a varredura automática deixa de ser ignorado, e o painel passa a acompanhar a varredura automática.
- Barra de status: o clique em procurar atualização e em baixar a versão nova pega de primeira (a dica do botão abria por cima dele).
- Procurar atualização avisa quando a consulta falha, em vez de dizer que você já está na versão mais recente.

## 1.0.0.30 — 06/10/2026
- Preferências: nova seção **Notas da versão**, com o que mudou em cada versão. Ela vem dentro do aplicativo e funciona sem internet.

## 1.0.0.29 — 06/10/2026
- Diff no desenho do GitHub: realce de sintaxe (Delphi, C#, XML, SQL, JSON e outras), coluna do `+` e `−` separada do código, número da linha alterada em destaque e faixa em cada bloco.
- Contador `+N −N` com os cinco quadrinhos no cabeçalho do arquivo e botão **Arquivo inteiro**, que mostra o arquivo todo com as alterações marcadas.
- **Diff externo**: informe nas Preferências a pasta de instalação (ou o `.exe`) da sua ferramenta — Beyond Compare, WinMerge, Meld, KDiff3, P4Merge, TortoiseGitMerge e VS Code são reconhecidos — e abra pelo botão do painel de diferenças ou pelo clique direito.
- Preferências separadas por seção, com a lista à esquerda.

## 1.0.0.28 — 06/10/2026
- **Ignorar alterações** só nesta máquina: por arquivo, pelos selecionados ou todos, sem mexer no repositório. **Ver ignorados** traz de volta.
- Arquivo novo pode ir para o `.gitignore` pelo clique direito.
- Windows 10: a barra de título passa a seguir o tema escuro.

## 1.0.0.27 — 02/10/2026
- **Enviar** funciona em branch vinculada a outra de nome diferente (feature criada a partir de `origin/develop`): cria a remota de mesmo nome e refaz o vínculo.

## 1.0.0.26 — 02/10/2026
- Alterações: seleção de vários arquivos com Ctrl/Shift para preparar ou descartar só eles, **Reverter** (voltar ao último commit ou ao remoto), commit com Ctrl+Enter e emendar já com a mensagem anterior.
- Conflitos: **Meu**, **Deles** ou resolvido por arquivo, e faixa de merge/rebase em andamento com Continuar e Abortar.
- Diff: preparar ou remover só as linhas escolhidas.
- Histórico: copiar hash, criar branch ou tag no commit, cherry-pick, reverter commit, reset (soft, mixed, hard), busca por mensagem, autor ou hash e reorganizar commits (rebase interativo sem editor).
- Desfazer a última ação, histórico do arquivo e autoria das linhas (blame).
- Opções de trazer todas as tags e de sincronizar todas as branches em Obter e Puxar.

## 1.0.0.25 — 02/10/2026
- Consultas ao GitHub são refeitas uma vez quando demoram, e a mensagem de erro sai em português.

## 1.0.0.24 — 02/10/2026
- **Clonar** um repositório do GitHub direto pela tela de adicionar.

## 1.0.0.23 — 02/10/2026
- Caminhos com acento aparecem certos e podem ser preparados.
- Link do remoto mais limpo, sem botões ao lado.

## 1.0.0.22 — 02/10/2026
- O diff mostra os acentos de arquivos em ANSI (Windows-1252), como os fontes Delphi.

## 1.0.0.21 — 01/10/2026
- **Terminal** Git Bash embutido no aplicativo, com botão para abri-lo em janela separada.

## 1.0.0.20 — 01/10/2026
- A tela de branches abre com as pastas recolhidas.

## 1.0.0.19 — 01/10/2026
- Cartão de perfil compacto, com o gráfico de contribuições.
- Grupos recolhíveis no painel.

## 1.0.0.18 — 01/10/2026
- Cartões do painel com o contorno inteiro.
- A contagem de linhas considera só os seus commits.

## 1.0.0.17 — 01/10/2026
- Várias contas do GitHub, com a conta escolhida por repositório.
- Foto no cartão de perfil e painel que não pisca ao atualizar.

## 1.0.0.16 — 01/10/2026
- O cartão do painel expande para mostrar a esteira.
- A pílula da branch não some mais ao recolher e abrir o grupo.

## 1.0.0.15 — 01/10/2026
- Pílula com a branch atual em cada repositório da árvore.
- Tela de branches organizada por pasta, com suporte a git-flow.

## 1.0.0.13 — 30/09/2026
- Esteira mostra as últimas execuções, recolhe o resto e filtra por situação.
- Painel separado por grupo, com botão de atualizar discreto.

## 1.0.0.12 — 30/09/2026
- O aplicativo abre no **Painel**, com cartões clicáveis que preenchem a largura da janela.

## 1.0.0.11 — 30/09/2026
- **Novidades**: changelog dentro do aplicativo, lido das releases do GitHub.
- Aba **Leia-me** com o README do repositório renderizado, incluindo tabelas e imagens.
- Cartão de perfil no painel geral e contagem de pull requests abertos em cada cartão.
- Estilo minimalista da árvore, com a opção de voltar às pílulas.
- A esteira passa a mostrar também os builds disparados por tag.

## 1.0.0.10 — 30/09/2026
- **Painel** de repositórios por grupo e para o workspace inteiro: branch, pendências e situação da esteira em cartões.
- A atualização automática deixou de piscar o botão "Atualizar todos".

## 1.0.0.9 — 30/09/2026
- O caminho do modo portátil não some mais da tela quando a troca falha.
- Rodapé alinhado, e o ícone de atualização acende quando há versão nova.

## 1.0.0.8 — 30/09/2026
- Ícone no rodapé, ao lado da versão, para procurar atualização na hora.

## 1.0.0.7 — 30/09/2026
- A esteira se atualiza sozinha enquanto a janela está aberta, sem perder a rolagem nem a seleção.

## 1.0.0.6 — 30/09/2026
- Botões do corpo dos diálogos menores e discretos.
- Barra de rolagem fina.

## 1.0.0.5 — 30/09/2026
- **Modo portátil** opcional: guardar as configurações junto do executável.

## 1.0.0.4 — 30/09/2026
- A atualização automática reconhece o executável de arquivo único mesmo quando ele foi renomeado.
- **Procurar agora** nas Preferências.

## 1.0.0.3 — 30/09/2026
- Rodapé de Configurar Repositório centralizado.

## 1.0.0.2 — 30/09/2026
- Ctrl+F leva ao filtro de repositórios; Esc limpa.
- **Atualização automática**: o aplicativo avisa quando sai versão nova, baixa, troca e reabre.
- A versão aparece na barra de status.

## 1.0.0.1 — 30/09/2026
- Primeira versão publicada: grupos na sidebar, par Origem × Destino, alterações com diff e preparação por bloco, histórico com grafo, branches, stash e esteira do GitHub Actions.
- Dois executáveis por release: o que depende do .NET 8 e o autossuficiente.
