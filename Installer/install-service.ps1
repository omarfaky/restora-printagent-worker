param([string]$ServiceName = "SilentPrintAgent", [string]$PublishDirectory = "$PSScriptRoot\..\publish", [string]$DataDirectory = "$env:ProgramData\SilentPrintAgent\data", [System.Management.Automation.PSCredential]$ServiceCredential)

$exe = Join-Path $PublishDirectory "SilentPrintAgent.exe"
if (!(Test-Path -LiteralPath $exe)) { throw "Publish the project first; executable not found: $exe" }
$settingsPath = Join-Path $PublishDirectory "appsettings.json"
if (Test-Path -LiteralPath $settingsPath) { throw "Refusing to replace existing configuration: $settingsPath" }
$settingsTemplate = Join-Path $PSScriptRoot "appsettings.production.template.json"
$settings = Get-Content -Raw -LiteralPath $settingsTemplate | ConvertFrom-Json
$settings.PrintAgent.DataDirectory = $DataDirectory
$settings.PrintAgent.LocalToken = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { throw "Service '$ServiceName' already exists." }
$serviceIdentity = if ($ServiceCredential) { $ServiceCredential.UserName } else { "NT AUTHORITY\LOCAL SERVICE" }
Set-Content -LiteralPath $settingsPath -Value ($settings | ConvertTo-Json -Depth 20) -Encoding utf8
$dataGrants = @(
    "$($serviceIdentity):(OI)(CI)F"
    "NT AUTHORITY\SYSTEM:(OI)(CI)F"
    "BUILTIN\Administrators:(OI)(CI)F"
)
$settingsGrants = @(
    "$($serviceIdentity):R"
    "NT AUTHORITY\SYSTEM:F"
    "BUILTIN\Administrators:F"
)
icacls.exe $DataDirectory /inheritance:r /grant:r $dataGrants | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not secure the data directory ACL." }
icacls.exe $settingsPath /inheritance:r /grant:r $settingsGrants | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not secure the settings file ACL." }
if ($ServiceCredential) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName "Silent Print Agent" -StartupType Automatic -Credential $ServiceCredential | Out-Null
} else {
    sc.exe create $ServiceName "binPath= `"$exe`"" "start= auto" "obj= $serviceIdentity" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Windows Service creation failed." }
    sc.exe description $ServiceName "Local-first silent ESC/POS print agent" | Out-Null
}
Write-Host "Service created. Add printer profiles to $settingsPath before starting the service. The generated token is stored there and is not displayed."
