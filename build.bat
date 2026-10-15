@echo off
REM Oz-translate derleyici. Windows'un yerleşik C# derleyicisini kullanir, ek kurulum gerekmez.
setlocal
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WM=C:\Windows\System32\WinMetadata
set DIR=%~dp0

"%FW%\csc.exe" /nologo /target:winexe /out:"%DIR%Oz-translate.exe" ^
  /r:"%FW%\System.dll" ^
  /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" ^
  /r:"%FW%\System.Net.Http.dll" ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  /r:"%FW%\System.Runtime.dll" ^
  /r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\WPF\UIAutomationClient.dll" ^
  /r:"%FW%\WPF\UIAutomationTypes.dll" ^
  /r:"%WM%\Windows.Foundation.winmd" ^
  /r:"%WM%\Windows.Media.winmd" ^
  /r:"%WM%\Windows.Graphics.winmd" ^
  /r:"%WM%\Windows.Storage.winmd" ^
  /r:"%WM%\Windows.Globalization.winmd" ^
  "%DIR%OzTranslate.cs"

set BUILD_RESULT=%ERRORLEVEL%
if %ERRORLEVEL%==0 (
  echo.
  echo Derleme basarili: "%DIR%Oz-translate.exe"
) else (
  echo.
  echo HATA: Derleme basarisiz. Yukaridaki mesajlara bakin.
)
endlocal & exit /b %BUILD_RESULT%
