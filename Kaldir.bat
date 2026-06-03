@echo off
chcp 65001 >nul
setlocal
echo Oz-translate kaldiriliyor...
taskkill /IM "Oz-translate.exe" /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Oz-translate" /f >nul 2>&1
echo.
echo Program durduruldu ve otomatik baslatma kaldirildi.
echo (Dosyalar klasorde duruyor; istemezseniz klasoru elle silebilirsiniz.)
echo.
pause
endlocal
