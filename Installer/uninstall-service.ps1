param([string]$ServiceName = "SilentPrintAgent")

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) { if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }; sc.exe delete $ServiceName }
Write-Host "Service removed. Persistent data was intentionally kept."
