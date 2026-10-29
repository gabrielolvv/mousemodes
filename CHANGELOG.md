# Novidades do MouseModes

**Português** · [English](CHANGELOG.en.md)

Cada versão tem uma seção `## X.Y.Z`, aqui e no `CHANGELOG.en.md`. A Release do GitHub usa a seção da própria versão
dos dois arquivos como texto, primeiro em português e depois em inglês.

## 1.3.0

### Novidades

- **Teste de mira e DPI** (botão na tela inicial e no ícone da bandeja): alvos por 20 s. O app mede se você passa do
  alvo, se para antes, quantas correções faz e a precisão do clique, e sugere quanto subir ou baixar o DPI no programa
  do seu mouse. As rodadas ficam lado a lado para comparar DPIs.
- **Inglês**: todas as telas seguem o idioma do Windows (português ou inglês), com escolha em ícone da bandeja →
  Idioma / Language. As mensagens do registro do Diagnóstico continuam em português. README em inglês
  (`README.en.md`).

### Correções

- O aviso de monitor desligado mandava ligar em Ajustes → Turbo; a opção fica em Ajustes → Geral.

## 1.2.0

### Novidades

- **Buscar jogos** (Ajustes → Jogos → Buscar): lista os jogos instalados pela Steam e pela Epic, além dos programas
  abertos, para incluir vários de uma vez.
- **Mouse do jogo só com o jogo na tela** (Ajustes → Performance, desligado por padrão): no alt-tab e no desktop, o
  mouse volta à velocidade e à aceleração do Dia a dia.
- **Plano de energia "Anterior"** no Dia a dia: ao sair do Performance, volta ao plano que estava ativo antes (inclusive
  o do fabricante ou um personalizado), e não mais sempre ao Equilibrado. É o padrão em instalações novas.
- **Atalho personalizável** (Ajustes → Geral): qualquer combinação com Ctrl ou Alt. Se outro programa já usar, o app
  tenta as opções de antes e avisa qual ficou.
- **Exportar e importar a configuração** (Ajustes → Geral), para levar jogos, perfis e Turbo para outro PC.
- **Windows para jogos** (no Diagnóstico): mostra o Modo de Jogo, a gravação em segundo plano, o agendamento de GPU e as
  otimizações para jogos em janela, com um botão para a tela certa do Windows. O app não muda nada disso sozinho.
- **Aviso de versão nova**, uma vez por dia e nunca durante o jogo (Ajustes → Geral para desligar, ou ícone da
  bandeja → Procurar versão nova).

### Correções

- O bloqueio da tecla Windows não atrasa mais o teclado durante o jogo.
- Ao abrir, o app não regrava mais o mouse nem o plano de energia. O Dia a dia passa a acompanhar o que você muda no
  próprio Windows.
- A troca de modo não congela mais a janela enquanto o plano de energia é trocado, e um plano de energia que trava não
  segura mais o app.
- Em PCs que escondem planos de energia, o app não cria mais uma cópia nova do Desempenho máximo a cada vez, e o
  Equilibrado é achado direto.
- A configuração não se perde se o PC desligar no meio da gravação.

### Download

- A Release traz o SHA-256 do instalador (no texto e no arquivo `MouseModes-Setup.exe.sha256`), para conferir o
  download.

## 1.1.1

- A Release passa a ter só o instalador, com nome fixo: o botão do README sempre baixa a versão mais recente.

## 1.1.0

- Primeira versão: modos Performance e Dia a dia, troca automática por jogo, perfis por jogo, Turbo (prioridade, tecla
  Windows, Teclas de Aderência, notificações, fechar programas), aviso de taxa do monitor, medidor de polling, teste de
  clique, Diagnóstico e instalador sem administrador.
