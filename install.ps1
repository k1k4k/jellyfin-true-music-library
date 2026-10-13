<#
.SYNOPSIS
    Installe (ou désinstalle) le plugin Jellyfin True Music Library sans passer par le catalogue de Jellyfin.

.DESCRIPTION
    - Copie Jellyfin.Plugin.TrueMusicLibrary.dll dans <plugins>\True Music Library_<version>\ avec son meta.json,
      comme le fait le catalogue de Jellyfin ; les anciennes versions de ce plugin sont retirées.
    - La DLL vient, au choix : d'un zip de release (-Zip), d'une compilation des sources (-Build, SDK .NET requis),
      sinon de la dernière release GitHub (téléchargée).
    - La bonne variante est choisie selon la version de Jellyfin (12.x ou 10.11.x), lue sur le serveur.
    - Idempotent : relancer le script met à jour le plugin. -Uninstall le retire.
    Redémarrer Jellyfin ensuite (le script peut le faire pour le service Windows avec -RestartService).
    Faites une sauvegarde Jellyfin avant la première réparation de la médiathèque.

.PARAMETER PluginsDir
    Dossier des plugins de Jellyfin. Détection automatique si omis :
      C:\ProgramData\Jellyfin\Server\plugins          (installation Windows)
      %LOCALAPPDATA%\jellyfin\plugins                  (Jellyfin lancé sans service)
    Peut être un chemin UNC vers un serveur Linux (\\SERVEUR\jellyfin\plugins, soit /var/lib/jellyfin/plugins).

.PARAMETER ServerUrl
    Adresse du serveur, pour lire sa version (http://localhost:8096 par défaut). Aucune authentification.

.PARAMETER Target
    jf12 (Jellyfin 12.x) ou jf10 (Jellyfin 10.11.x). Détection automatique si omis.

.PARAMETER Zip
    Zip d'une release (jellyfin-true-music-library_<version>_<cible>.zip) à installer.

.PARAMETER Build
    Compile la DLL depuis ce dossier (SDK .NET 10 pour jf12, .NET 9 pour jf10).

.PARAMETER RestartService
    Redémarre le service Windows de Jellyfin après l'installation (droits administrateur requis).

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Build
    .\install.ps1 -PluginsDir "\\SERVEUR\jellyfin\plugins" -ServerUrl "http://SERVEUR:8096"
    .\install.ps1 -Zip .\jellyfin-true-music-library_0.1.0.0_jf12.zip
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$PluginsDir,
    [string]$ServerUrl = 'http://localhost:8096',
    [ValidateSet('jf12', 'jf10')]
    [string]$Target,
    [string]$Zip,
    [switch]$Build,
    [switch]$RestartService,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$Guid      = '541fb644-193a-4692-b8a4-2593356153a7'
$Name      = 'True Music Library'
$DllName   = 'Jellyfin.Plugin.TrueMusicLibrary.dll'
$Repo      = 'k1k4k/jellyfin-true-music-library'
$Project   = Join-Path $PSScriptRoot 'Jellyfin.Plugin.TrueMusicLibrary\Jellyfin.Plugin.TrueMusicLibrary.csproj'
$Utf8NoBom = New-Object Text.UTF8Encoding($false)

# --- Dossier des plugins -------------------------------------------------------------------------------
if (-not $PluginsDir) {
    $candidates = @(
        "$env:ProgramData\Jellyfin\Server\plugins",
        "$env:LOCALAPPDATA\jellyfin\plugins"
    )
    $PluginsDir = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $PluginsDir) {
        throw "Dossier des plugins Jellyfin introuvable. Indiquez-le avec -PluginsDir (ex. C:\ProgramData\Jellyfin\Server\plugins)."
    }
}
if (-not (Test-Path $PluginsDir)) { throw "Dossier introuvable : $PluginsDir" }

# Dossiers de ce plugin (toutes versions) : meta.json avec notre GUID, ou nom « True Music Library_x » / « TrueMusicLibrary_x ».
function Get-PluginFolders {
    Get-ChildItem -Path $PluginsDir -Directory | Where-Object {
        $meta = Join-Path $_.FullName 'meta.json'
        if (Test-Path $meta) {
            try { return ((Get-Content $meta -Raw -Encoding UTF8 | ConvertFrom-Json).guid -eq $Guid) } catch { }
        }
        return ($_.Name -like "$Name`_*") -or ($_.Name -like 'TrueMusicLibrary_*')
    }
}

function Remove-Folders($folders) {
    $locked = @()
    foreach ($f in $folders) {
        try {
            Remove-Item -Path $f.FullName -Recurse -Force
            Write-Host "Supprimé : $($f.FullName)"
        } catch {
            $locked += $f.FullName
        }
    }
    return $locked
}

if ($Uninstall) {
    $folders = @(Get-PluginFolders)
    if ($folders.Count -eq 0) { Write-Host 'True Music Library n''est pas installé dans ce dossier.'; exit 0 }
    $locked = Remove-Folders $folders
    if ($locked.Count -gt 0) {
        Write-Host "Fichiers utilisés par Jellyfin, à supprimer après l'avoir arrêté : $($locked -join ', ')" -ForegroundColor Yellow
    }
    Write-Host 'Désinstallation terminée. Redémarrez Jellyfin.' -ForegroundColor Green
    Write-Host 'Les données du plugin (analyse, instantanés) restent dans <données Jellyfin>\plugins\configurations\TrueMusicLibrary.' -ForegroundColor Yellow
    exit 0
}

# --- Variante : Jellyfin 12.x ou 10.11.x -----------------------------------------------------------------
if (-not $Target) {
    $version = $null
    try {
        $info = Invoke-RestMethod -Uri ($ServerUrl.TrimEnd('/') + '/System/Info/Public') -TimeoutSec 5
        $version = $info.Version
    } catch {
        # Jellyfin installé sur ce PC (pas pour un dossier distant : ce serait la version d'un autre serveur).
        if (-not $PluginsDir.StartsWith('\') -and -not $PSBoundParameters.ContainsKey('ServerUrl')) {
            $exe = @("$env:ProgramFiles\Jellyfin\Server\jellyfin.dll", "$env:ProgramFiles\Jellyfin\Server\jellyfin.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($exe) { $version = (Get-Item $exe).VersionInfo.ProductVersion }
        }
    }
    if (-not $version) {
        throw "Version de Jellyfin inconnue (serveur $ServerUrl injoignable). Indiquez -Target jf12 (Jellyfin 12.x) ou -Target jf10 (10.11.x)."
    }
    if ($version -match '^10\.11\.') { $Target = 'jf10' }
    elseif ($version -match '^1[2-9]\.') { $Target = 'jf12' }
    else { throw "Jellyfin $version n'est pas pris en charge (12.x ou 10.11.x)." }
    Write-Host "Jellyfin $version : variante $Target"
}
$Abi = if ($Target -eq 'jf10') { '10.11.0.0' } else { '12.0.0.0' }

# --- La DLL ------------------------------------------------------------------------------------------------
$work = Join-Path ([IO.Path]::GetTempPath()) ("tml-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if ($Build) {
        # Un dotnet qui a le SDK voulu (celui du PATH n'a parfois que des runtimes).
        $sdk = if ($Target -eq 'jf10') { '9.' } else { '10.' }
        $dotnet = @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe", "$env:ProgramFiles\dotnet\dotnet.exe") |
            Where-Object { $_ -and (Test-Path $_) } |
            Where-Object { @(& $_ --list-sdks 2>$null) -match "^$([regex]::Escape($sdk))" } |
            Select-Object -First 1
        if (-not $dotnet) { throw "SDK .NET $($sdk)x introuvable (requis pour $Target) : installez-le, ou utilisez -Zip." }
        if (-not (Test-Path $Project)) { throw "Sources introuvables : $Project" }
        $log = & $dotnet build $Project -c Release "-p:JellyfinTarget=$Target" -o $work -nologo -v m 2>&1
        if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 20 | Write-Host; throw 'La compilation a échoué.' }
    } else {
        if (-not $Zip) {
            Write-Host "Recherche de la dernière release de $Repo..."
            $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases" -Headers @{ 'User-Agent' = 'tml-install' }
            $asset = $releases | ForEach-Object { $_.assets } | Where-Object { $_.name -like "*_$Target.zip" } | Select-Object -First 1
            if (-not $asset) { throw "Aucune release publiée pour $Target. Utilisez -Build (sources) ou -Zip." }
            $Zip = Join-Path $work $asset.name
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $Zip -UseBasicParsing
            Write-Host "Téléchargé : $($asset.name)"
        }
        if (-not (Test-Path $Zip)) { throw "Zip introuvable : $Zip" }
        if ((Split-Path $Zip -Leaf) -notlike "*_$Target.zip") {
            Write-Host "Attention : ce zip ne semble pas être la variante $Target." -ForegroundColor Yellow
        }
        Expand-Archive -Path $Zip -DestinationPath $work -Force
    }

    $dll = Join-Path $work $DllName
    if (-not (Test-Path $dll)) { throw "$DllName absent." }
    $version = (Get-Item $dll).VersionInfo.FileVersion

    # --- Copie : <plugins>\True Music Library_<version>\ + meta.json -----------------------------------------
    $old = @(Get-PluginFolders | Where-Object { $_.Name -ne "$Name`_$version" })
    $dest = Join-Path $PluginsDir "$Name`_$version"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    try {
        Copy-Item -Path $dll -Destination (Join-Path $dest $DllName) -Force
    } catch {
        throw "Impossible d'écrire $dest\$DllName : cette version est chargée par Jellyfin. Arrêtez Jellyfin, puis relancez le script."
    }

    $meta = [ordered]@{
        category    = 'Metadata'
        changelog   = ''
        description = 'Un seul onglet « Artistes », avec seulement les artistes principaux des albums, singles et EP.'
        guid        = $Guid
        name        = $Name
        overview    = 'Une médiathèque musicale sans artistes parasites.'
        owner       = 'k1k4k'
        targetAbi   = $Abi
        timestamp   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        version     = $version
        status      = 'Active'
        autoUpdate  = $false
        assemblies  = @()
    }
    [IO.File]::WriteAllText((Join-Path $dest 'meta.json'), ($meta | ConvertTo-Json), $Utf8NoBom)
    Write-Host "Installé : $dest ($Target, version $version)"

    $locked = Remove-Folders $old
    if ($locked.Count -gt 0) {
        Write-Host "Ancienne version encore utilisée par Jellyfin : $($locked -join ', ')." -ForegroundColor Yellow
        Write-Host 'Jellyfin chargera la plus récente ; relancez ce script après le redémarrage pour supprimer l''ancienne.' -ForegroundColor Yellow
    }
} finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($RestartService) {
    $service = Get-Service -Name 'JellyfinServer' -ErrorAction SilentlyContinue
    if ($service) {
        Restart-Service -Name 'JellyfinServer'
        Write-Host 'Service JellyfinServer redémarré.' -ForegroundColor Green
    } else {
        Write-Host 'Service JellyfinServer introuvable : redémarrez Jellyfin vous-même.' -ForegroundColor Yellow
    }
} else {
    Write-Host 'Redémarrez Jellyfin pour charger le plugin (serveur Linux : sudo systemctl restart jellyfin).' -ForegroundColor Green
}
Write-Host 'Puis : Tableau de bord → Extensions → True Music Library → Analyser. Sauvegarde Jellyfin conseillée avant « Réparer ».'
