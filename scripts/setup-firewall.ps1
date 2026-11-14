#Requires -RunAsAdministrator
# Allows DupliXon to accept connections from devices on PRIVATE networks only (home/office Wi-Fi).
param([string]$Exe = (Resolve-Path "$PSScriptRoot\..\src\DupliXon.App\bin\Release\net8.0-windows10.0.19041.0\DupliXon.exe" -ErrorAction SilentlyContinue))

Get-NetFirewallRule -DisplayName 'DupliXon*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule

New-NetFirewallRule -DisplayName 'DupliXon (web + señalización)' -Direction Inbound -Protocol TCP -LocalPort 8787 `
    -Profile Private -Action Allow | Out-Null

if ($Exe) {
    # WebRTC uses dynamic UDP ports, so the media rule is scoped to the executable.
    New-NetFirewallRule -DisplayName 'DupliXon (video WebRTC)' -Direction Inbound -Protocol UDP -Program $Exe `
        -Profile Private -Action Allow | Out-Null
} else {
    Write-Warning 'DupliXon.exe no encontrado; compila en Release o pasa -Exe <ruta>. Solo se creó la regla TCP.'
}

Write-Host 'Reglas de firewall listas. Asegúrate de que tu Wi-Fi esté marcada como red PRIVADA.' -ForegroundColor Green
