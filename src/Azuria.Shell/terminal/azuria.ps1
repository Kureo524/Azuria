# Terminal d'Azuria : accueil et prompt (DA « Encre »).
# Idée reprise de Caelestia (infos du système à l'ouverture, prompt sobre), écrit ici : aucun de
# ses fichiers n'est utilisé. Compatible PowerShell 5.1 (celui de Windows) et 7.
# Copié par Azuria dans %LOCALAPPDATA%\Azuria\terminal, avec infos.json à côté.

$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# ----- couleurs -----

$AzuriaDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $AzuriaDir) { $AzuriaDir = Join-Path $env:LOCALAPPDATA 'Azuria\terminal' }
$AzuriaInfos = @{}
$raw = Get-Content -Raw -Encoding UTF8 (Join-Path $AzuriaDir 'infos.json')
if ($raw) { ($raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $AzuriaInfos[$_.Name] = $_.Value } }

$E = [char]27
function AzuriaRgb([string]$hex) {
    if ($hex -notmatch '^#?([0-9a-fA-F]{6})$') { return "$E[39m" }
    $v = [Convert]::ToInt32($Matches[1], 16)
    "$E[38;2;$(($v -shr 16) -band 255);$(($v -shr 8) -band 255);$($v -band 255)m"
}
$Az = @{
    Accent     = AzuriaRgb ($(if ($AzuriaInfos.accent) { $AzuriaInfos.accent } else { '#8fa4cc' }))
    AccentText = AzuriaRgb ($(if ($AzuriaInfos.accentText) { $AzuriaInfos.accentText } else { '#adbfe0' }))
    Text       = AzuriaRgb '#e6eaf2'
    Text2      = AzuriaRgb '#a2a6b4'
    Text3      = AzuriaRgb '#868a98'
    Danger     = AzuriaRgb '#d9646c'
    Warning    = AzuriaRgb '#d9aa3a'
    Success    = AzuriaRgb '#93b07e'
    Bold       = "$E[1m"
    Reset      = "$E[0m"
}

# ----- accueil -----

function AzuriaDuration([TimeSpan]$t) {
    if ($t.TotalDays -ge 1) { return '{0} j {1} h' -f [int][Math]::Floor($t.TotalDays), $t.Hours }
    if ($t.TotalHours -ge 1) { return '{0} h {1:00} min' -f [int][Math]::Floor($t.TotalHours), $t.Minutes }
    return '{0} min' -f [int][Math]::Floor($t.TotalMinutes)
}

function AzuriaBar([double]$ratio) {
    # Jauge de 12 cases, à l'accent ; au-delà de 85 %, en couleur d'alerte (DA §3).
    # 0.0 et 1.0, pas 0 et 1 : sinon PowerShell 5.1 choisit Max(int, int) et arrondit le ratio.
    $n = [int][Math]::Round([Math]::Max(0.0, [Math]::Min(1.0, $ratio)) * 12)
    $c = if ($ratio -ge 0.85) { $Az.Warning } else { $Az.Accent }
    "$c$([string]::new([char]0x25AA, $n))$($Az.Text3)$([string]::new([char]0x25AB, 12 - $n))$($Az.Reset)"
}

function Show-AzuriaWelcome {
    $mem = $null
    try {
        Add-Type -AssemblyName Microsoft.VisualBasic
        $ci = New-Object Microsoft.VisualBasic.Devices.ComputerInfo
        $mem = @{ Total = $ci.TotalPhysicalMemory; Free = $ci.AvailablePhysicalMemory }
    } catch {}
    $disk = $null
    try { $disk = New-Object IO.DriveInfo ($env:SystemDrive) } catch {}

    $uptime = ''
    if ($AzuriaInfos.boot) { $uptime = AzuriaDuration ((Get-Date) - [DateTimeOffset]::Parse($AzuriaInfos.boot).LocalDateTime) }
    $ps = "PowerShell $($PSVersionTable.PSVersion.Major).$($PSVersionTable.PSVersion.Minor)"

    $lines = New-Object System.Collections.Generic.List[object]
    $lines.Add(@('', "$($Az.Bold)$($Az.AccentText)$env:USERNAME$($Az.Text3)@$($Az.Bold)$($Az.Text)$env:COMPUTERNAME$($Az.Reset)"))
    $lines.Add(@('', "$($Az.Text3)$([string]::new([char]0x2500, ($env:USERNAME.Length + $env:COMPUTERNAME.Length + 1)))$($Az.Reset)"))
    if ($AzuriaInfos.os)      { $lines.Add(@('Système', "$($AzuriaInfos.os) $($Az.Text3)· build $($AzuriaInfos.build)")) }
    $lines.Add(@('Bureau', "Azuria $($Az.Text3)$($AzuriaInfos.version)"))
    $lines.Add(@('Shell', $ps))
    if ($uptime)              { $lines.Add(@('Allumé', $uptime)) }
    if ($AzuriaInfos.cpu)     { $lines.Add(@('Processeur', "$($AzuriaInfos.cpu) $($Az.Text3)· $($AzuriaInfos.threads) fils")) }
    if ($AzuriaInfos.gpu)     { $lines.Add(@('Graphique', $AzuriaInfos.gpu)) }
    if ($mem) {
        $used = ($mem.Total - $mem.Free) / 1GB; $tot = $mem.Total / 1GB
        $lines.Add(@('Mémoire', ('{0} {1:0.0} / {2:0.0} Go' -f (AzuriaBar ($used / $tot)), $used, $tot)))
    }
    if ($disk -and $disk.IsReady) {
        $used = ($disk.TotalSize - $disk.AvailableFreeSpace) / 1GB; $tot = $disk.TotalSize / 1GB
        $lines.Add(@("Disque $($disk.Name.TrimEnd('\'))", ('{0} {1:0} / {2:0} Go' -f (AzuriaBar ($used / $tot)), $used, $tot)))
    }
    $lines.Add(@('', ''))
    $dots = @($Az.Accent, $Az.AccentText, $Az.Success, $Az.Warning, $Az.Danger, (AzuriaRgb '#a89ad1'), (AzuriaRgb '#8fb3c4'), $Az.Text2) |
        ForEach-Object { "$_$([char]0x25CF)" }
    $lines.Add(@('', (($dots -join ' ') + $Az.Reset)))

    # Le « A » d'Azuria : jambes en texte, barre à l'accent (comme web/logo.svg).
    $t = $Az.Text; $a = $Az.Accent; $r = $Az.Reset
    $logo = @(
        '',
        "$t      ▄▄      $r",
        "$t     ▟██▙     $r",
        "$t    ▟█▘▝█▙    $r",
        "$t   ▟█▘  ▝█▙   $r",
        "$t  ▟█$a▄▄▄▄▄▄$t█▙  $r",
        "$t ▟█▘      ▝█▙ $r",
        "$t▟█▘        ▝█▙$r",
        ''
    )

    Write-Host ''
    $rows = [Math]::Max($logo.Count, $lines.Count)
    for ($i = 0; $i -lt $rows; $i++) {
        $l = if ($i -lt $logo.Count) { $logo[$i] } else { '' }
        $pad = 14 - ($l -replace "$E\[[0-9;]*m", '').Length
        $left = '  ' + $l + (' ' * [Math]::Max(0, $pad))
        $right = ''
        if ($i -lt $lines.Count) {
            $k, $v = $lines[$i]
            $right = if ($k) { $Az.Accent + $k.PadRight(12) + $Az.Text + $v + $Az.Reset } else { $v }
        }
        Write-Host ($left + '   ' + $right)
    }
    Write-Host ''
}

# ----- prompt -----

# Branche git, lue dans .git/HEAD en remontant les dossiers : pas d'appel à git.exe à chaque prompt.
function AzuriaGitBranch {
    $dir = (Get-Location).ProviderPath
    while ($dir) {
        $git = Join-Path $dir '.git'
        if (Test-Path -LiteralPath $git -PathType Container) {
            $head = Get-Content -LiteralPath (Join-Path $git 'HEAD') -TotalCount 1
            if ($head -match '^ref: refs/heads/(.+)$') { return $Matches[1] }
            if ($head) { return $head.Substring(0, [Math]::Min(7, $head.Length)) }
            return $null
        }
        if (Test-Path -LiteralPath $git -PathType Leaf) { return 'worktree' }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    $null
}

function AzuriaPath {
    $p = (Get-Location).ProviderPath
    $home_ = [Environment]::GetFolderPath('UserProfile')
    if ($p -and $p.StartsWith($home_, [StringComparison]::OrdinalIgnoreCase)) { $p = '~' + $p.Substring($home_.Length) }
    # Chemin long : on garde les trois derniers dossiers.
    $parts = $p -split '[\\/]'
    if ($parts.Count -gt 4) { $p = $parts[0] + '\…\' + ($parts[-3..-1] -join '\') }
    $p
}

function global:prompt {
    $ok = $?
    $code = $global:LASTEXITCODE
    $out = "`n$($Az.Bold)$($Az.AccentText)$(AzuriaPath)$($Az.Reset)"
    $branch = AzuriaGitBranch
    if ($branch) { $out += " $($Az.Text3)sur $($Az.Text2)$branch$($Az.Reset)" }
    $last = Get-History -Count 1
    if ($last -and $last.EndExecutionTime) {
        $took = $last.EndExecutionTime - $last.StartExecutionTime
        if ($took.TotalSeconds -ge 2) { $out += " $($Az.Text3)· $('{0:0.0}' -f $took.TotalSeconds) s$($Az.Reset)" }
    }
    $mark = if ($ok) { $Az.Accent } else { $Az.Danger }
    # OSC 9;9 : Windows Terminal connaît le dossier courant (nouvel onglet, volet dupliqué au même endroit).
    $osc = "$E]9;9;$((Get-Location).ProviderPath)$E\"
    $global:LASTEXITCODE = $code
    "$out`n$osc$mark$([char]0x276F)$($Az.Reset) "
}

# ----- saisie (PSReadLine) -----

if (Get-Module -ListAvailable PSReadLine) {
    Import-Module PSReadLine
    $colors = @{
        Command   = $Az.Accent
        Parameter = $Az.Text2
        Operator  = $Az.Text2
        Variable  = $Az.AccentText
        String    = $Az.Success
        Number    = $Az.Warning
        Type      = $Az.AccentText
        Keyword   = $Az.Accent
        Member    = $Az.Text
        Comment   = $Az.Text3
        Default   = $Az.Text
    }
    try { Set-PSReadLineOption -Colors $colors } catch {}
    try { Set-PSReadLineOption -ContinuationPrompt "$($Az.Text3)$([char]0x2502) $($Az.Reset)" } catch {}
    try { Set-PSReadLineOption -BellStyle None } catch {}
}

$host.UI.RawUI.WindowTitle = 'Azuria'
Clear-Host
Show-AzuriaWelcome
$ErrorActionPreference = 'Continue'
