@echo off
chcp 65001 >nul
setlocal
set DIR=%~dp0
set EXE=%DIR%Oz-translate.exe

echo ===========================================
echo   Oz-translate Kurulum
echo ===========================================
echo.

if not exist "%EXE%" (
  echo Program bulunamadi, derleniyor...
  call "%DIR%build.bat"
)
if not exist "%EXE%" (
  echo.
  echo HATA: Derleme basarisiz oldu. Cikiliyor.
  pause
  exit /b 1
)

echo Windows acilisina ekleniyor (otomatik baslatma)...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Oz-translate" /t REG_SZ /d "\"%EXE%\"" /f >nul

echo Program baslatiliyor...
start "" "%EXE%"

echo.
echo ===========================================
echo  Kuruldu ve calisiyor!
echo.
echo  - Saat yanindaki tepside mavi "O" ikonu var.
echo  - Fareyi ingilizce yazinin uzerine getirin,
echo    yaninda Turkce cevirisi belirsin.
echo  - Daha fazla satir icin TAB tusuna basin.
echo  - Acip/kapatmak icin: Ctrl + Alt + Z
echo    (veya tepsi ikonuna cift tiklayin)
echo  - Bilgisayar her acildiginda kendi baslar.
echo ===========================================
echo.
pause
endlocal
