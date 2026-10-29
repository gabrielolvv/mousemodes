; Instalador do MouseModes (NSIS 3).
; Instala só para o usuário atual, sem pedir administrador:
;   %LOCALAPPDATA%\Programs\MouseModes
; Gerado pelo build.sh (raiz do repositório).

Unicode true
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef EXE
  !define EXE "..\dist\MouseModes.exe"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\MouseModes-Setup.exe"
!endif

!define APPNAME "MouseModes"
!define PUBLISHER "gabrielolvv"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseModes"
!define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1046 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=1046 "FileDescription" "Instalador do ${APPNAME}"
VIAddVersionKey /LANG=1046 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1046 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1046 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=1046 "LegalCopyright" "${PUBLISHER}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ICON "..\src\app.ico"
!define MUI_UNICON "..\src\app.ico"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Instalar o ${APPNAME} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "O ${APPNAME} alterna o mouse e o PC entre o modo Performance (jogos) e o modo Dia a dia.$\r$\n$\r$\nA instalação é só para o seu usuário e não precisa de administrador. Se o ${APPNAME} estiver aberto, ele será fechado e as configurações do Windows voltam ao modo Dia a dia antes de atualizar."
!insertmacro MUI_PAGE_WELCOME
; Privacidade (exigida pela assinatura de código da SignPath Foundation): o que sai do PC e onde desligar.
; No modo silencioso (atualização sozinha, winget) as páginas não aparecem.
!define MUI_PAGE_HEADER_TEXT "Privacidade"
!define MUI_PAGE_HEADER_SUBTEXT "O que o ${APPNAME} envia pela internet / What it sends over the internet"
!define MUI_LICENSEPAGE_TEXT_TOP "Leia antes de instalar. Role para ver tudo."
!define MUI_LICENSEPAGE_TEXT_BOTTOM "Dá para desligar tudo isso no app, em Ajustes > Privacidade."
!define MUI_LICENSEPAGE_BUTTON "Próximo >"
!insertmacro MUI_PAGE_LICENSE "privacidade.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPNAME}.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir o ${APPNAME} agora"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "PortugueseBR"

; ---------------------------------------------------------------------------

; Fecha qualquer MouseModes aberto (instalado ou solto em outra pasta) usando o próprio exe novo.
Function CloseRunning
  InitPluginsDir
  File "/oname=$PLUGINSDIR\mm-quit.exe" "${EXE}"
  retry:
  ExecWait '"$PLUGINSDIR\mm-quit.exe" --quit' $0
  ${If} $0 != 0
    ; Na atualização sozinha (/S) não há ninguém para clicar: desiste, e a versão aberta continua como está.
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Há um ${APPNAME} antigo aberto que não pôde ser fechado automaticamente.$\r$\n$\r$\nClique com o botão direito no ícone dele perto do relógio, escolha Sair e depois clique em Repetir." /SD IDCANCEL IDRETRY retry
    Abort
  ${EndIf}
FunctionEnd

Section "${APPNAME} (obrigatório)" SecMain
  SectionIn RO
  Call CloseRunning

  SetOutPath "$INSTDIR"
  File "/oname=${APPNAME}.exe" "${EXE}"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"

  CreateDirectory "$SMPROGRAMS"
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${APPNAME}.exe" "" "$INSTDIR\${APPNAME}.exe" 0

  ; Entrada em Configurações > Aplicativos instalados
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${APPNAME}.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Desinstalar.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" 400

  ; Se o "Iniciar com o Windows" já estava ligado (ex.: apontando para Downloads), passa a usar o instalado.
  ReadRegStr $0 HKCU "${RUNKEY}" "${APPNAME}"
  ${If} $0 != ""
    WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${APPNAME}.exe" --tray'
  ${EndIf}
SectionEnd

Section "Atalho na área de trabalho" SecDesktop
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPNAME}.exe" "" "$INSTDIR\${APPNAME}.exe" 0
SectionEnd

Section "Iniciar com o Windows (na bandeja)" SecStartup
  WriteRegStr HKCU "${RUNKEY}" "${APPNAME}" '"$INSTDIR\${APPNAME}.exe" --tray'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "O aplicativo, o atalho no menu Iniciar e o desinstalador."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Cria um atalho do ${APPNAME} na área de trabalho."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "Abre o ${APPNAME} escondido perto do relógio quando o Windows iniciar. Dá para mudar depois em Ajustes > Geral."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function .onInit
  ; Atalho na área de trabalho vem marcado; iniciar com o Windows vem desmarcado.
  SectionSetFlags ${SecStartup} 0
  ; Atualização sozinha (/S): não recria o atalho da área de trabalho de quem o apagou.
  ${If} ${Silent}
  ${AndIfNot} ${FileExists} "$DESKTOP\${APPNAME}.lnk"
    SectionSetFlags ${SecDesktop} 0
  ${EndIf}
FunctionEnd

; Atualização sozinha: o app roda "MouseModes-Setup.exe /S /RELAUNCH" e é fechado pelo instalador; no fim, ele
; abre a versão nova na bandeja. Se a instalação falhar, abre o exe que estiver lá, para o app não ficar fechado.
Function Relaunch
  Exch $R2
  Push $R0
  Push $R1
  ${If} ${Silent}
    ${GetParameters} $R0
    ClearErrors
    ${GetOptions} $R0 "/RELAUNCH" $R1
    ${IfNot} ${Errors}
    ${AndIf} ${FileExists} "$INSTDIR\${APPNAME}.exe"
      Exec '"$INSTDIR\${APPNAME}.exe" --tray $R2'
    ${EndIf}
  ${EndIf}
  Pop $R1
  Pop $R0
  Pop $R2
FunctionEnd

Function .onInstSuccess
  Push "--updated"
  Call Relaunch
FunctionEnd

Function .onInstFailed
  Push ""
  Call Relaunch
FunctionEnd

; ---------------------------------------------------------------------------

Section "Uninstall"
  ; 1) Fecha o app (ele mesmo restaura teclas, notificações e prioridade).
  ExecWait '"$INSTDIR\${APPNAME}.exe" --quit'
  ; 2) Volta mouse e energia ao modo Dia a dia e tira do "Iniciar com o Windows".
  ExecWait '"$INSTDIR\${APPNAME}.exe" --uninstall-cleanup'
  DeleteRegValue HKCU "${RUNKEY}" "${APPNAME}"

  Delete "$INSTDIR\${APPNAME}.exe"
  Delete "$INSTDIR\Desinstalar.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"

  ; Configurações e registro ficam, a menos que a pessoa queira apagar.
  IfSilent done
  MessageBox MB_YESNO|MB_ICONQUESTION "Apagar também as configurações e o registro do ${APPNAME}?$\r$\n($APPDATA\${APPNAME})" IDNO done
    RMDir /r "$APPDATA\${APPNAME}"
  done:
SectionEnd
