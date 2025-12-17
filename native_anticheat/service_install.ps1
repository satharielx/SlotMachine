# PowerShell script to install anticheat helper as a Windows service
# Usage: Run as Administrator
# Edit path variables below before running.

$exePath = "C:\path\to\anticheat.exe" # change this to the built executable path
$serviceName = "SlotNativeAnticheat"

New-Service -Name $serviceName -BinaryPathName "$exePath" -DisplayName "Slot Native Anticheat" -StartupType Automatic
Start-Service -Name $serviceName
Write-Host "Service installed and started: $serviceName"