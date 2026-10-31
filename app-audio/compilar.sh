#!/bin/sh
# Compila o Tela ao Vivo Áudio com o compilador C# do Visual Studio, pro .NET Framework 4.8
# (que já vem no Windows 10/11). Saída: ../TelaAoVivoAudio.exe (servido pelo site pra download).
set -e
cd "$(dirname "$0")"
CSC="${CSC:-/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/Roslyn/csc.exe}"
FW='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
"$CSC" -nologo -noconfig -nostdlib -optimize+ -target:winexe -platform:anycpu -langversion:7.3 \
  -win32icon:icone.ico -win32manifest:app.manifest -out:../TelaAoVivoAudio.exe \
  -r:"$FW\\mscorlib.dll" -r:"$FW\\System.dll" -r:"$FW\\System.Core.dll" -r:"$FW\\System.Drawing.dll" \
  -r:"$FW\\System.Windows.Forms.dll" -r:"$FW\\System.Web.Extensions.dll" \
  Programa.cs Audio.cs Motor.cs Janela.cs
ls -la ../TelaAoVivoAudio.exe
