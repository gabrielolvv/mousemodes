# MouseModes

**Português** · [English](README.en.md)

App para Windows 10/11 que alterna o mouse e o PC entre dois modos: **Performance** (jogos) e **Dia a dia**.
Troca pela tela principal, por atalho global ou sozinho quando um jogo abre.

**Site:** [gabrielolvv.github.io/mousemodes](https://gabrielolvv.github.io/mousemodes/): o que o app faz, telas e download.

<p align="center">
  <a href="https://github.com/gabrielolvv/mousemodes/releases/latest/download/MouseModes-Setup.exe">
    <img alt="Baixar instalador para Windows" src="https://img.shields.io/badge/Baixar%20instalador-Windows%2010%2F11-E84C28?style=for-the-badge&logo=windows&logoColor=white" height="48">
  </a>
</p>

![Telas do MouseModes](docs/preview.png)

## Instalar

1. Clique em **Baixar instalador** acima.
2. Abra o `MouseModes-Setup.exe` e clique em **Próximo** até o fim.

Na primeira vez o Windows pode mostrar "O Windows protegeu o computador", porque o app não é assinado digitalmente:
clique em **Mais informações → Executar assim mesmo**.

Para conferir que o instalador baixado é o mesmo que o GitHub gerou, compare o SHA-256 dele com o da página da Release
(também no arquivo `MouseModes-Setup.exe.sha256`). No PowerShell, na pasta do download:

```powershell
(Get-FileHash .\MouseModes-Setup.exe -Algorithm SHA256).Hash
```

- Instala só para o seu usuário, sem pedir administrador, e cria o atalho no menu Iniciar.
- Atualiza sozinho: uma vez por dia o app consulta a página de releases do GitHub, sem enviar nada do seu PC. Quando
  sai versão nova, ele baixa o instalador da Release, confere o SHA-256 publicado nela e instala por cima, nunca
  durante o jogo: o app fecha, instala e abre de novo na bandeja em alguns segundos, com as mesmas configurações. Se
  algo não bater, ele não instala e só avisa. Dá para desligar em Ajustes → Privacidade (aí ele só avisa) ou procurar na
  hora pelo ícone da bandeja → Procurar versão nova. Para atualizar à mão, baixe e rode o instalador de novo.
- Para desinstalar: Configurações → Aplicativos instalados → MouseModes. O mouse e a energia voltam ao modo Dia a dia.

## O que cada modo faz

| | Performance (jogos) | Dia a dia |
|---|---|---|
| Velocidade do ponteiro | 10/20 (1:1, sem multiplicador) | a que você já usava |
| Aceleração ("Aprimorar precisão do ponteiro") | desligada | a que você já usava |
| Plano de energia | Alto desempenho ou Desempenho máximo | o que você já usava ("Anterior") |
| Suspensão seletiva de USB | desativada (mouse nunca "adormece") | padrão do Windows |

Tudo é editável em **Ajustes**. Fechar a janela no X não encerra o app: ele fica no ícone perto do relógio.

O Dia a dia acompanha o próprio Windows: se você mudar a velocidade ou a aceleração no Painel de Controle,
é isso que volta depois do Performance. Ao abrir, o app não regrava nada; só desfaz um Performance que ficou
aplicado porque ele foi fechado à força ou o PC desligou no meio.

### Turbo (só no modo Performance)

- **Prioridade alta para o jogo**, devolvida ao sair. Alguns anti-cheats bloqueiam; o app então ignora e registra no Diagnóstico.
- **Bloqueia a tecla Windows**, só com um jogo da lista em primeiro plano.
- **Sem aviso das Teclas de Aderência** (Shift 5x e afins).
- **Silencia as notificações do Windows.** Se o app travar com elas silenciadas, ele restaura ao abrir de novo.
- **Segundo plano calmo** (desligado por padrão): em vez de fechar, deixa os outros programas abertos com prioridade
  abaixo do normal e, no Windows 11, no modo de eficiência. Nos processadores Intel híbridos (12ª geração ou mais
  nova), eles também vão para os núcleos de eficiência (E), e os de desempenho (P) ficam para o jogo. Ajuda nas
  travadinhas quando o processador está no limite (navegador, Steam baixando, OneDrive sincronizando). Não mexe no
  jogo, no programa em primeiro plano (no alt-tab ele volta ao normal), nos processos do Windows nem em voz, música,
  transmissão, overlay, software do mouse e anti-cheat (Discord, Spotify, OBS, NVIDIA, G HUB, Razer etc.). Um jogo
  aberto pela Steam acalmada volta ao normal na hora. Se o app travar com o segundo plano acalmado, ele desfaz ao
  abrir de novo.
- **Fecha programas da sua lista** (ex.: OneDrive, launchers). Jogos da lista e processos do Windows nunca são fechados.

Tudo volta ao normal ao sair do Performance, ao fechar o app ou ao desligar o PC.

### Mais

- **Perfis por jogo** (Ajustes → Jogos): velocidade, aceleração, plano e resolução próprios para cada jogo. Com a
  resolução (ex.: 1280×960 para o 4:3 esticado no CS2 ou no Valorant), o monitor principal vai para ela, na maior taxa
  que aceita, quando o jogo abre no Performance, e volta à de sempre quando o jogo fecha, ao sair do Performance ou ao
  fechar o app. A troca não é gravada no Windows: reiniciar o PC também desfaz, e se o app travar ele desfaz ao abrir
  de novo. Para esticar, a escala da placa de vídeo precisa estar em "Tela cheia".
- **Mouse do jogo só com o jogo na tela** (Ajustes → Performance, desligado por padrão): no alt-tab e no desktop,
  o mouse volta à velocidade e aceleração do Dia a dia; ao voltar ao jogo, usa o do Performance. Energia, USB e
  Turbo não mudam.
- **Troca automática**: compara o *começo* do nome do processo (`FiveM` pega `FiveM_b3095_GTAProcess`).
  A troca manual vence a automática.
- **Buscar jogos** (Ajustes → Jogos → Buscar): lista os jogos instalados pela Steam e pela Epic, além dos programas
  abertos, para incluir vários de uma vez. O app escolhe como processo o maior `.exe` da pasta do jogo, sem
  desinstaladores, launchers e anti-cheat; se errar, dá para corrigir o nome à mão.
- **Atalho global** (Ajustes → Geral): clique no campo e aperte a combinação, com Ctrl ou Alt (padrão: Ctrl+Alt+M).
  Se ela já for usada por outro programa, o app tenta Ctrl+Alt+M, Ctrl+Shift+M, Ctrl+Alt+F12 e Ctrl+Shift+F12 e mostra
  na tela inicial qual ficou.
- **Troca por comando**: `MouseModes.exe --performance`, `--daily` ou `--toggle` troca o modo do app aberto, como o
  atalho. Serve para um botão do Stream Deck, um atalho na área de trabalho ou o Agendador de Tarefas. Se o app não
  estiver aberto, ele abre na bandeja já no modo pedido.
- **Taxa do monitor**: avisa se algum monitor está abaixo do máximo (ex.: 60 Hz num de 144 Hz) e ajusta com um clique.
- **Medidor de polling rate** e **teste de clique duplo** (switch gasto), lendo o mouse via Raw Input.
- **Teste de mira e DPI** (tela inicial ou ícone da bandeja): 20 s de alvos. O app mede se você passa do alvo, se
  para antes, quantas correções faz e a precisão do clique, e sugere subir ou baixar o DPI no programa do mouse.
  Trocou o DPI? Faça de novo: as rodadas ficam lado a lado enquanto o app estiver aberto.
- **Conversor de sensibilidade** (tela inicial → Sensibilidade, ou ícone da bandeja): digite a sensibilidade e o DPI
  do seu jogo e veja o cm/360 (quanto o mouse anda para uma volta completa) e o valor que dá a mesma mira em CS2,
  Apex, TF2, Valorant, Overwatch 2 e Call of Duty. Trocou o DPI? Informe o novo e o app recalcula a sensibilidade de
  cada jogo para a mira não mudar. Para outro jogo, basta informar o yaw dele.
- **DPI real do mouse** (conversor de sensibilidade → Medir o DPI real, ou ícone da bandeja): o sensor raramente
  entrega o DPI anunciado. Com uma régua na mesa, segure o botão esquerdo no quadro, arraste o mouse em linha reta
  pela distância (10 cm) e solte; o app conta as leituras do sensor pelo Raw Input e mostra o DPI de verdade (ex.: 800
  no programa do mouse, 763 na régua). Depois, o conversor mostra a volta (cm/360) com o DPI real. A velocidade do
  Windows não entra.
- **Raio-X da partida**: enquanto um jogo da lista estiver aberto no Performance, o app anota a cada 3 s quanto
  processador e quantos dados cada programa em segundo plano usou, e mede a rede com um ping por segundo ao roteador e
  ao 1.1.1.1 (Cloudflare). Ao fechar o jogo, um aviso mostra quem mais pesou (ex.: chrome com média de 18% e pico de
  61%, OneDrive com 2,1 GB) e, com um clique, põe o programa na lista para fechar ao jogar ou liga o segundo plano
  calmo. A parte da rede mostra ping, jitter e perda, os piores picos e de onde vieram: do Wi-Fi ou da sua rede (o
  roteador também piorou) ou da internet (até o roteador estava tudo bem), e quem usava muitos dados na hora. Fica no
  ícone da bandeja → Raio-X da última partida. Só lê, sem administrador; mede quem pesou, não o FPS.
- **Caça-travadinhas** (no Raio-X da partida; Ajustes → Geral → Permitir): mede cada quadro que o jogo entrega para a
  tela (eventos de Present do DirectX 9 ao 12, como o PresentMon) e mostra no fim da partida o FPS médio, o 1% low e
  as travadinhas, com o que se destacou na hora: um programa usando muito processador, outro lendo/gravando no disco
  ou a internet sem responder. Se nada em segundo plano explica, ele diz que pode ser do próprio jogo (shaders,
  carregamento). Não entra no jogo. O Windows só deixa ler os quadros para administradores ou para o grupo
  "Performance Log Users": o botão Permitir põe a sua conta no grupo uma vez (pede o administrador) e vale depois de
  sair e entrar de novo na conta; o mesmo botão tira. Jogos em Vulkan ou OpenGL ainda não são lidos.
- **Diagnóstico** (Ajustes → Geral): registro do que o app fez e do que falhou. Arquivo em `%APPDATA%\MouseModes\log.txt`.
- **Windows para jogos** (no Diagnóstico): mostra Modo de Jogo, gravação em segundo plano, agendamento de GPU acelerado
  e, no Windows 11, otimizações para jogos em janela, com o que costuma ajudar. O app não muda nada disso: o botão
  "Abrir" leva à tela do Windows onde você decide.
- **Restaurar padrões do Windows** (Ajustes → Geral).
- **Exportar e importar** (Ajustes → Geral): leva jogos, perfis, Turbo e ajustes gerais para outro PC num arquivo
  `.xml`. O que só vale neste PC (modo aplicado, planos de energia daqui, estado das notificações) fica de fora.
  Antes de importar, o app mostra quantos jogos e quais programas para fechar vêm no arquivo.

- **Idioma**: a tela segue o idioma do Windows (português num Windows em português, inglês nos outros). Dá para
  escolher no ícone da bandeja → Idioma / Language; vale na próxima vez que o app abrir. Todas as telas e o
  registro do Diagnóstico estão traduzidos.

Configuração em `%APPDATA%\MouseModes\config.xml`.

## O que realmente tira delay (e o que não)

- **Polling rate** é o que mais pesa e é configurado **no mouse** (software da marca). 1000 Hz = uma leitura por ms;
  125 Hz = uma a cada 8 ms. Use o medidor para conferir.
- **Aceleração desligada + velocidade 10** deixa o movimento previsível. Jogos com *raw input* (CS2, Valorant, Apex)
  já ignoram essas opções dentro da partida; elas valem para menus, desktop e jogos sem raw input.
- **Plano de energia** e **USB sem suspensão** evitam que CPU e porta USB "durmam" e causem micro travadas.
- FPS alto, monitor na taxa máxima e mouse com fio/2.4 GHz (não Bluetooth) pesam mais que qualquer ajuste de software.
- O app não faz tweaks de registro "milagrosos" (MouseDataQueueSize, bcdedit etc.): o ganho é duvidoso e às vezes piora.

## Desenvolvimento

```
src/                 código C# (WinForms, .NET Framework 4.8)
tests/               testes automáticos (xUnit)
installer/           script do instalador (NSIS)
build.sh             gera dist/MouseModes.exe e dist/MouseModes-Setup.exe
VERSION              versão do app e do instalador
.github/workflows/   CI: build a cada push e Release a cada tag
```

**Build oficial** (o que vai para a Release), em Linux/WSL:

```sh
sudo apt-get install mono-devel nsis
./build.sh          # app + instalador
./build.sh app      # só o app
```

Ele compila contra as referências exatas do .NET 4.8 (`/usr/lib/mono/4.8-api`). Compilar contra a biblioteca
padrão do Mono pode gerar chamadas que não existem no Windows.

**No Windows** (Visual Studio, Rider ou terminal):

```powershell
dotnet build src/MouseModes.csproj -c Release
# exe em src\bin\Release\net48\MouseModes.exe
dotnet test tests/MouseModes.Tests/MouseModes.Tests.csproj -c Release
```

Os testes só leem o Windows (mouse, registro): nenhum aplica mouse, plano de energia ou USB. Os arquivos que eles
criam e o log ficam numa pasta temporária. O plano de energia passa por um aplicador de mentira
(`ModeManager.PowerApplier`); um teste novo que mexa na fila precisa conferir isso antes de enfileirar pedidos.

Os textos da tela ficam no código com as duas versões lado a lado: `L.T("Ajustes", "Settings")` (ver `src/L.cs`).

**Publicar uma versão**:

1. Atualize o arquivo `VERSION` (ex.: `1.2.0`) e escreva a seção `## 1.2.0` com o que mudou no `CHANGELOG.md` e, em
   inglês, no `CHANGELOG.en.md`. As duas viram o texto da Release, e o CI recusa a tag se faltar uma delas. Faça commit.
2. Crie e envie a tag: `git tag v1.2.0 && git push origin v1.2.0`.
3. O GitHub Actions compila, roda os testes, confere se a tag bate com `VERSION` e anexa à Release o
   `MouseModes-Setup.exe` e o `MouseModes-Setup.exe.sha256` (o SHA-256 também vai no texto da Release).
   O botão de download do README sempre aponta para o instalador da Release mais recente.

As actions do CI são fixadas pelo commit, com a versão num comentário. Uma vez por mês o Dependabot
(`.github/dependabot.yml`) confere se saiu versão nova e, se saiu, abre um PR trocando o SHA e o comentário. Para
atualizar à mão, troque o SHA pelo da versão nova (`git ls-remote --tags https://github.com/<dono>/<action>`).

Argumentos do executável: `--tray` (inicia na bandeja), `--quit` (fecha a cópia aberta restaurando tudo) e
`--uninstall-cleanup` (volta ao Dia a dia e tira do "Iniciar com o Windows"), usados pelo instalador; e
`--performance`, `--daily` e `--toggle` (trocam o modo). Com outra cópia aberta, o comando vai para ela e o exe sai
com código 0, ou 1 se a cópia aberta for de uma versão sem esses comandos. Na atualização sozinha, o app roda o
instalador com `/S /RELAUNCH`, e o instalador abre a versão nova com `--tray --updated`.

## Política de assinatura de código

O instalador do MouseModes ainda não é assinado digitalmente; a assinatura de código está nos planos. O projeto
pediu a assinatura gratuita para open source da SignPath Foundation, mas ela ainda não foi concedida: a fundação
pede que o projeto tenha mais uso público antes, e o pedido vai ser refeito mais tarde. Enquanto isso, o Windows
pode mostrar o aviso "O Windows protegeu o computador", e o SHA-256 publicado em cada Release permite conferir que o
instalador é o que o build do CI deste repositório gerou.

Equipe e papéis:

- Commits e revisão: [gabrielolvv](https://github.com/gabrielolvv)
- Aprovação de cada assinatura: [gabrielolvv](https://github.com/gabrielolvv)

Quando houver assinatura, só vai ser assinado o que sai do build automático deste repositório, a partir do código
público, com aprovação manual a cada versão.

## Privacidade

O MouseModes não tem conta, telemetria nem anúncios. Ele só fala com outros sistemas nestes casos, e todos dá para
desligar em Ajustes → Privacidade:

- **Versão nova**: uma vez por dia, uma consulta à página de versões do GitHub (api.github.com). Ao atualizar, o
  instalador é baixado da Release do GitHub. Vão só o endereço IP e o nome do programa, como em qualquer acesso a um
  site.
- **Raio-X da rede**: durante a partida (Performance com um jogo da lista aberto), um ping por segundo ao seu roteador
  e ao 1.1.1.1 (Cloudflare), sem conteúdo.

As configurações e o registro do que o app faz ficam neste PC, em `%APPDATA%\MouseModes`. O instalador mostra este
aviso antes de instalar.

## Licença

[MIT](LICENSE): use, modifique e distribua à vontade, mantendo o aviso de copyright. O app vem sem garantia.
