[CmdletBinding()]
param(
    [string]$Version = $env:RELEGO_VERSION,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoOwner = 'Krusty93'
$RepoName = 'relego'

function Write-Info {
    param([string]$Message)

    Write-Host "install.ps1: $Message"
}

function Normalize-Version {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    return (($Value -replace '^cli/', '') -replace '^v', '')
}

function Get-DefaultBinDir {
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        return [System.IO.Path]::Combine($env:LOCALAPPDATA, 'Programs', 'Relego')
    }

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        return [System.IO.Path]::Combine($env:USERPROFILE, 'AppData', 'Local', 'Programs', 'Relego')
    }

    throw 'Unable to determine the default install directory.'
}

function Get-LatestVersion {
    $headers = @{
        Accept                 = 'application/vnd.github+json'
        'User-Agent'           = 'relego-install-script'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$RepoOwner/$RepoName/releases?per_page=100" -Headers $headers
    $release = $releases | Where-Object { $_.tag_name -like 'cli/v*' } | Select-Object -First 1

    if ($null -eq $release) {
        throw 'Could not resolve the latest CLI version from GitHub Releases.'
    }

    return ($release.tag_name -replace '^cli/v', '')
}

function Get-WindowsRid {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    switch ($architecture) {
        'X64' { return 'win-x64' }
        'Arm64' { return 'win-arm64' }
        default {
            throw "Unsupported Windows architecture: $architecture"
        }
    }
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
}

function Test-PathContainsEntry {
    param(
        [string]$PathValue,
        [string]$Entry
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $false
    }

    $normalizedEntry = [System.IO.Path]::GetFullPath($Entry).TrimEnd('\')

    foreach ($candidate in ($PathValue -split ';')) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if ([System.IO.Path]::GetFullPath($candidate).TrimEnd('\\') -ieq $normalizedEntry) {
            return $true
        }
    }

    return $false
}

function Ensure-UserPathEntry {
    param([string]$Entry)

    if (Test-PathContainsEntry -PathValue $env:Path -Entry $Entry) {
        return $false
    }

    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')

    if ((Test-PathContainsEntry -PathValue $machinePath -Entry $Entry) -or (Test-PathContainsEntry -PathValue $userPath -Entry $Entry)) {
        $env:Path = "${Entry};$env:Path"
        return $false
    }

    $updatedUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $Entry
    }
    else {
        "$userPath;$Entry"
    }

    [Environment]::SetEnvironmentVariable('Path', $updatedUserPath, 'User')
    $env:Path = "${Entry};$env:Path"

    return $true
}

$WindowsRid = Get-WindowsRid

$BinDir = Get-DefaultBinDir
$BinDir = [System.IO.Path]::GetFullPath($BinDir)
$TargetPath = Join-Path $BinDir 'relego.exe'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-LatestVersion
}
else {
    $Version = Normalize-Version $Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Version cannot be empty.'
}

$AssetName = "relego-$Version-$WindowsRid.exe"
$DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/cli%2Fv$Version/$AssetName"
$TempPath = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName() + '.exe')

try {
    if ($DryRun) {
        Write-Info "version=$Version"
        Write-Info "rid=$WindowsRid"
        Write-Info "asset=$AssetName"
        Write-Info "url=$DownloadUrl"
        Write-Info "target_path=$TargetPath"
        exit 0
    }

    Ensure-Directory -Path $BinDir

    Write-Info "Downloading $AssetName"
    Invoke-WebRequest -Uri $DownloadUrl -Headers @{ 'User-Agent' = 'relego-install-script' } -OutFile $TempPath

    Move-Item -Path $TempPath -Destination $TargetPath -Force

    Write-Info "Saved executable to $TargetPath"
    Write-Info "Installed Relego $Version"

    $pathUpdated = Ensure-UserPathEntry -Entry $BinDir

    if ($pathUpdated) {
        Write-Info 'Updated the user PATH. Open a new terminal if `relego` is not yet available.'
    }
}
finally {
    if (Test-Path -LiteralPath $TempPath) {
        Remove-Item -LiteralPath $TempPath -Force
    }
}
