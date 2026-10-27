# MouseModes

App para Windows 10/11 que alterna o mouse e o PC entre dois modos: **Performance** (jogos) e **Dia a dia**.
Troca pela tela principal, por atalho global ou sozinho quando um jogo abre.

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

- Instala só para o seu usuário, sem pedir administrador, e cria o atalho no menu Iniciar.
- Para atualizar, baixe e rode o instalador de novo: ele fecha a versão aberta e mantém suas configurações.
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
- **Fecha programas da sua lista** (ex.: OneDrive, launchers). Jogos da lista e processos do Windows nunca são fechados.

Tudo volta ao normal ao sair do Performance, ao fechar o app ou ao desligar o PC.

### Mais

- **Perfis por jogo** (Ajustes → Jogos): velocidade, aceleração e plano próprios para cada jogo.
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
- **Taxa do monitor**: avisa se algum monitor está abaixo do máximo (ex.: 60 Hz num de 144 Hz) e ajusta com um clique.
- **Medidor de polling rate** e **teste de clique duplo** (switch gasto), lendo o mouse via Raw Input.
- **Diagnóstico** (Ajustes → Geral): registro do que o app fez e do que falhou. Arquivo em `%APPDATA%\MouseModes\log.txt`.
- **Windows para jogos** (no Diagnóstico): mostra Modo de Jogo, gravação em segundo plano, agendamento de GPU acelerado
  e, no Windows 11, otimizações para jogos em janela, com o que costuma ajudar. O app não muda nada disso: o botão
  "Abrir" leva à tela do Windows onde você decide.
- **Restaurar padrões do Windows** (Ajustes → Geral).
- **Exportar e importar** (Ajustes → Geral): leva jogos, perfis, Turbo e ajustes gerais para outro PC num arquivo
  `.xml`. O que só vale neste PC (modo aplicado, planos de energia daqui, estado das notificações) fica de fora.
  Antes de importar, o app mostra quantos jogos e quais programas para fechar vêm no arquivo.

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

**Publicar uma versão**:

1. Atualize o arquivo `VERSION` (ex.: `1.2.0`) e faça commit.
2. Crie e envie a tag: `git tag v1.2.0 && git push origin v1.2.0`.
3. O GitHub Actions compila, confere se a tag bate com `VERSION` e anexa o `MouseModes-Setup.exe` à Release.
   O botão de download do README sempre aponta para o instalador da Release mais recente.

Argumentos do executável: `--tray` (inicia na bandeja), `--quit` (fecha a cópia aberta restaurando tudo) e
`--uninstall-cleanup` (volta ao Dia a dia e tira do "Iniciar com o Windows"), usados pelo instalador.
