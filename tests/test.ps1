param([switch]$PreviewUI, [switch]$LiveTranslation)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = Split-Path $compiler
$metadata = "$env:WINDIR\System32\WinMetadata"
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$arguments = @('/nologo', '/target:exe', '/main:RegressionTests', "/out:$artifacts\RegressionTests.exe")
foreach ($name in @('System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Net.Http', 'System.Web.Extensions', 'System.Runtime', 'System.Runtime.WindowsRuntime')) {
    $arguments += "/r:$framework\$name.dll"
}
foreach ($name in @('Windows.Foundation', 'Windows.Media', 'Windows.Graphics', 'Windows.Storage', 'Windows.Globalization')) {
    $arguments += "/r:$metadata\$name.winmd"
}
foreach ($name in @('WindowsBase', 'UIAutomationClient', 'UIAutomationTypes')) {
    $arguments += "/r:$framework\WPF\$name.dll"
}
$arguments += (Join-Path $repo 'OzTranslate.cs')
$arguments += (Join-Path $PSScriptRoot 'RegressionTests.cs')
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$artifacts\RegressionTests.exe" "$artifacts\settings.png" "$PreviewUI" "$LiveTranslation"
exit $LASTEXITCODE
