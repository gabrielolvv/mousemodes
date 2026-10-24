#!/bin/sh
# Gera dist/MouseModes.exe (.NET Framework 4.8, já vem no Windows 10/11) e dist/MouseModes-Setup.exe.
# O instalador tem nome fixo (sem versão) para o link "releases/latest/download/MouseModes-Setup.exe" sempre funcionar.
#
# Requer: mono-devel (mcs + /usr/lib/mono/4.8-api) e nsis (makensis).
#   Ubuntu/Debian: sudo apt-get install mono-devel nsis
#
# Compila contra as referências exatas do .NET 4.8: compilar contra a biblioteca padrão do Mono
# pode gerar chamadas que não existem no Windows (já aconteceu com String.Split).
#
# Uso: ./build.sh            -> app + instalador
#      ./build.sh app        -> só o app
set -e
cd "$(dirname "$0")"

A=/usr/lib/mono/4.8-api
VERSION=$(tr -d ' \r\n' < VERSION)
mkdir -p dist src/obj

cat > src/obj/Version.g.cs <<CS
[assembly: System.Reflection.AssemblyVersion("$VERSION.0")]
[assembly: System.Reflection.AssemblyFileVersion("$VERSION.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$VERSION")]
CS

mcs -target:winexe -platform:anycpu -optimize+ -langversion:7 -warn:4 -warnaserror+ \
    -win32icon:src/app.ico \
    -nostdlib -r:$A/mscorlib.dll -r:$A/System.dll -r:$A/System.Core.dll \
    -r:$A/System.Windows.Forms.dll -r:$A/System.Drawing.dll -r:$A/System.Xml.dll \
    -out:dist/MouseModes.exe src/*.cs src/obj/Version.g.cs
echo "app: dist/MouseModes.exe ($VERSION)"

[ "$1" = "app" ] && exit 0

makensis -V2 -DVERSION="$VERSION" -DEXE=../dist/MouseModes.exe \
    -DOUTFILE="../dist/MouseModes-Setup.exe" installer/MouseModes.nsi
echo "instalador: dist/MouseModes-Setup.exe ($VERSION)"
