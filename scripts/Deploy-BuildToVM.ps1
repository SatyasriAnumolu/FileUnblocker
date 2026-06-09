<#
.SYNOPSIS
    Deploys a build artifact to a Hyper-V virtual machine.

.DESCRIPTION
  Intended to run as a post-success step in an Azure DevOps pipeline.
    Reverts a Hyper-V VM to a snapshot, starts it, waits for it to boot,
    copies a build installer (EXE or MSI) to the VM, and runs a silent
    installation. Fails the pipeline if any step encounters an error.

.PARAMETER VMName
    Name of the Hyper-V virtual machine.

.PARAMETER SnapshotName
    Name of the VM snapshot (checkpoint) to revert to.

.PARAMETER InstallerPath
    Local (host) path to the installer file (.exe or .msi).

.PARAMETER VMCredential
  PSCredential used to connect to the VM via PowerShell Direct.

.PARAMETER BootTimeoutSeconds
    Seconds to wait for the VM to become reachable after start. Default: 180.

.PARAMETER InstallTimeoutSeconds
  Seconds to wait for the installer to complete. Default: 300.

.EXAMPLE
    $cred = Get-Credential
    .\Deploy-BuildToVM.ps1 `n        -VMName "TestVM" `n        -SnapshotName "Clean-Baseline" `n        -InstallerPath "$(Build.ArtifactStagingDirectory)\MyApp-Setup.exe" `n        -VMCredential $cred
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)][string]$VMName,
    [Parameter(Mandatory = $true)]  [string]$SnapshotName,
    [Parameter(Mandatory = $true)]  [string]$InstallerPath,
    [Parameter(Mandatory = $true)]  [PSCredential]$VMCredential,
    [Parameter(Mandatory = $false)] [int]$BootTimeoutSeconds    = 180,
    [Parameter(Mandatory = $false)] [int]$InstallTimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n##[section]$(Get-Date -Format 'HH:mm:ss') - $Message"
}

function Fail-Pipeline {
    param([string]$Message)
    Write-Host "##vso[task.logissue type=error]$Message"
  Write-Host "##vso[task.complete result=Failed;]DONE"
    exit 1
}

# ---- 1. Validate inputs -------------------------------------------------------
Write-Step "Validating inputs"

if (-not (Test-Path -Path $InstallerPath)) {
    Fail-Pipeline "Installer not found at path: $InstallerPath"
}

$ext = [System.IO.Path]::GetExtension($InstallerPath).ToLower()
if ($ext -notin @('.exe', '.msi')) {
    Fail-Pipeline "Unsupported installer type. Only .exe and .msi are supported."
}

Write-Host "VM Name  : $VMName"
Write-Host "Snapshot : $SnapshotName"
Write-Host "Installer: $InstallerPath ($ext)"

# ---- 2. Locate VM -------------------------------------------------------------
Write-Step "Locating VM"

$vm = Get-VM -Name $VMName -ErrorAction SilentlyContinue
if (-not $vm) {
    Fail-Pipeline "Hyper-V VM not found on this host: $VMName"
}

# ---- 3. Revert to snapshot ----------------------------------------------------
Write-Step "Reverting to snapshot"

$snapshot = Get-VMSnapshot -VMName $VMName -Name $SnapshotName -ErrorAction SilentlyContinue
if (-not $snapshot) {
    Fail-Pipeline "Snapshot not found: $SnapshotName"
}

if ($vm.State -ne 'Off') {
    Write-Host "Stopping VM before restoring snapshot..."
    Stop-VM -Name $VMName -TurnOff -Force
    $null = Wait-VM -VMName $VMName -For Stopped -Timeout ([TimeSpan]::FromSeconds(60))
}

Restore-VMSnapshot -VMName $VMName -Name $SnapshotName -Confirm:$false
Write-Host "Snapshot restored."

# ---- 4. Start VM --------------------------------------------------------------
Write-Step "Starting VM"

Start-VM -Name $VMName
Write-Host "VM start command issued."

# ---- 5. Wait for boot (PowerShell Direct heartbeat) --------------------------
Write-Step "Waiting for VM to boot (timeout: ${BootTimeoutSeconds}s)"

$deadline = (Get-Date).AddSeconds($BootTimeoutSeconds)
$isReady  = $false

while ((Get-Date) -lt $deadline) {
    try {
        $result = Invoke-Command -VMName $VMName -Credential $VMCredential `n    -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop
        if ($result) {
            $isReady = $true
    Write-Host "VM is reachable (name: $result)."
            break
        }
    }
    catch {
        Write-Host "Not ready yet - retrying in 10 s..."
   Start-Sleep -Seconds 10
    }
}

if (-not $isReady) {
    Fail-Pipeline "VM did not become reachable within $BootTimeoutSeconds seconds."
}

# ---- 6. Copy installer and run silent install --------------------------------
Write-Step "Copying installer to VM"

$installerFileName   = [System.IO.Path]::GetFileName($InstallerPath)
$remoteInstallerPath = "C:\Windows\Temp\$installerFileName"

try {
Copy-VMFile -Name $VMName `n      -SourcePath $InstallerPath `n        -DestinationPath $remoteInstallerPath `n        -CreateFullPath `n        -FileSource Host `n        -Force
    Write-Host "Installer copied to $remoteInstallerPath inside VM."
}
catch {
    Fail-Pipeline "Failed to copy installer to VM: $_"
}

Write-Step "Running silent installation inside VM"

$installExitCode = Invoke-Command -VMName $VMName -Credential $VMCredential -ScriptBlock {
    param($RemotePath, $Extension)

    if ($Extension -eq '.msi') {
        $argList = "/i `"$RemotePath`" /qn /norestart /l*v C:\Windows\Temp\install.log"
    $proc = Start-Process msiexec.exe -ArgumentList $argList -Wait -PassThru
    }
    else {
        # .exe - adjust flag for your installer (NSIS: /S  Inno: /SILENT  WiX Burn: /quiet)
        $proc = Start-Process $RemotePath -ArgumentList '/S /quiet /norestart' -Wait -PassThru
    }

    return $proc.ExitCode

} -ArgumentList $remoteInstallerPath, $ext

Write-Host "Installer exited with code: $installExitCode"

# 0    = success
# 3010 = success but reboot required (acceptable in CI)
if ($installExitCode -notin @(0, 3010)) {
    Fail-Pipeline "Installation failed (exit code $installExitCode). Check C:\Windows\Temp\install.log inside the VM."
}

if ($installExitCode -eq 3010) {
    Write-Host "##[warning]Installation succeeded but a reboot is required (exit code 3010)."
}

# ---- 7. Done -----------------------------------------------------------------
Write-Step "Deployment completed successfully"
Write-Host "##vso[task.complete result=Succeeded;]DONE"