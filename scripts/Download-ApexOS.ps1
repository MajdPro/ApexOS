param(
    [string]$Branch = 'main',
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\ApexOS-source.zip'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'Download-ApexOS.ps1 is intended to run on Windows.'
}
if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -match '[\\/]') {
    throw 'Branch must be a single Git branch name.'
}

$output = [IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $output
$temporary = Join-Path $directory ('.ApexOS-source-' + [guid]::NewGuid().ToString('N') + '.zip')
$uri = 'https://github.com/MajdAljord/ApexOS/archive/refs/heads/{0}.zip' -f [Uri]::EscapeDataString($Branch)

if ((Test-Path -LiteralPath $output) -and -not $Force) {
    throw "File already exists: $output. Choose another -OutputPath or pass -Force to replace it."
}

try {
    New-Item -Path $directory -ItemType Directory -Force | Out-Null
    Write-Output "Downloading Apex OS source archive from $uri"
    Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing -Headers @{ 'User-Agent' = 'ApexOS-Windows-Downloader' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        $paths = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if (-not ($paths | Where-Object { $_ -match '^[^/]+/README\.md$' })) {
            throw 'The downloaded archive does not contain the expected ApexOS README.md.'
        }
        if (-not ($paths | Where-Object { $_ -match '^[^/]+/scripts/BuildApexPlaybook\.ps1$' })) {
            throw 'The downloaded archive does not contain scripts/BuildApexPlaybook.ps1.'
        }
    } finally {
        $archive.Dispose()
    }

    if ((Test-Path -LiteralPath $output) -and $Force) {
        Remove-Item -LiteralPath $output -Force
    }
    Move-Item -LiteralPath $temporary -Destination $output
    $size = (Get-Item -LiteralPath $output).Length
    Write-Output "Apex OS source ZIP downloaded and verified: $output ($size bytes)"
    Write-Output 'This is the source archive, not a built AME Wizard .apbx playbook.'
} catch {
    throw "Apex OS download failed: $($_.Exception.Message)"
} finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}