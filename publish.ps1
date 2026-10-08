param(
    [string]$Target = "\\FRARPTNLPAPP01\inetpub\webapps\Afleverlijst"
)

$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot "publish"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Publish-map niet gevonden: $source"
}

New-Item -ItemType Directory -Path $Target -Force | Out-Null

function Copy-PublishPart {
    param(
        [string]$From,
        [string]$To,
        [switch]$Recursive
    )

    $arguments = @($From, $To)
    if ($Recursive) { $arguments += "/E" }
    $arguments += @(
        "/COPY:DAT", "/DCOPY:DAT", "/R:2", "/W:1", "/XF", "*.pdb", "appsettings*.json",
        "/NP", "/NFL", "/NDL", "/NJH", "/NJS"
    )

    & robocopy @arguments
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy mislukt voor $From (exitcode $LASTEXITCODE)."
    }
}

# Alleen de bestanden uit de root van publish; PDB-bestanden zijn niet nodig.
Copy-PublishPart $source $Target

# Benodigde applicatiemappen. Lokalisatie, database en dataset worden bewust niet gekopieerd.
foreach ($folder in @("wwwroot", "runtimes", "Configuration")) {
    $from = Join-Path $source $folder
    if (Test-Path -LiteralPath $from) {
        Copy-PublishPart $from (Join-Path $Target $folder) -Recursive
    }
}

Write-Host "Afleverlijst gepubliceerd naar $Target" -ForegroundColor Green
