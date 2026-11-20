# Relatorio de chaves de localizacao.
#
#   (a) chaves usadas no codigo (Loc["..."] / Loc.Format("...")) que FALTAM nos
#       JSONs - grave se faltar no idioma default (en), aviso nos demais;
#   (b) chaves presentes no idioma default (en) mas NAO usadas no codigo
#       (informativo - hoje serao todas, pois o retrofit ainda nao foi feito).
#
# E diagnostico: sempre sai com exit code 0.
#
# Uso:
#   powershell -File tools/localization_report.ps1
#   powershell -File tools/localization_report.ps1 -ListUnused
#   powershell -File tools/localization_report.ps1 -SrcDir <src> -OutDir <Localization>

[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$SrcDir,
    [switch]$ListUnused
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'src\KingsValley.Desktop\Content\Localization' }
if (-not $SrcDir) { $SrcDir = Join-Path $repoRoot 'src' }

if (-not (Test-Path -LiteralPath $OutDir)) { throw "Pasta de localizacao nao encontrada: $OutDir" }
if (-not (Test-Path -LiteralPath $SrcDir)) { throw "Pasta de codigo nao encontrada: $SrcDir" }

# ---------------------------------------------------------------------------
# Le as chaves de um JSON flat (obj.PSObject.Properties preserva a ordem).
# ---------------------------------------------------------------------------
function Get-ChavesJson {
    param([string]$Caminho)

    if (-not (Test-Path -LiteralPath $Caminho)) { return @() }
    $txt = [System.IO.File]::ReadAllText($Caminho, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($txt)) { return @() }
    $obj = $txt | ConvertFrom-Json
    return @($obj.PSObject.Properties.Name)
}

# ---------------------------------------------------------------------------
# 1) Manifest: idioma default e lista de culturas
# ---------------------------------------------------------------------------
$defaultCulture = 'en'
$culturas = @()
$manifestPath = Join-Path $OutDir 'manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $txtManifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    if (-not [string]::IsNullOrWhiteSpace($txtManifest)) {
        $m = $txtManifest | ConvertFrom-Json
        if ($m.default) { $defaultCulture = [string]$m.default }
        $culturas = @($m.languages | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_.code })
    }
}
if ($culturas.Count -eq 0) {
    $culturas = @(Get-ChildItem -LiteralPath $OutDir -Filter *.json -File |
        Where-Object { $_.Name -ne 'manifest.json' } |
        ForEach-Object { $_.BaseName })
}

# ---------------------------------------------------------------------------
# 2) Coletar as chaves usadas no codigo
#    Loc["chave"], Loc['chave'], Loc.Format("chave", ...), Localization.Loc[...]
# ---------------------------------------------------------------------------
$regex = '(?:Localization\.)?\bLoc\s*(?:\[\s*["'']([^"'']+)["'']\s*\]|\.Format\s*\(\s*["'']([^"'']+)["''])'

$arquivos = @(Get-ChildItem -Path $SrcDir -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

# chave -> lista de arquivos onde aparece
$usadas = New-Object System.Collections.Specialized.OrderedDictionary
$ocorrencias = 0
foreach ($arq in $arquivos) {
    $texto = [System.IO.File]::ReadAllText($arq.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($texto, $regex)) {
        $chave = $null
        if ($m.Groups[1].Success) { $chave = $m.Groups[1].Value }
        elseif ($m.Groups[2].Success) { $chave = $m.Groups[2].Value }
        if ([string]::IsNullOrWhiteSpace($chave)) { continue }

        $ocorrencias++
        if ($usadas.Contains($chave)) {
            $onde = $usadas[$chave]
            $nome = Split-Path -Leaf $arq.FullName
            if ($onde -notcontains $nome) { [void]$onde.Add($nome) }
        }
        else {
            $lista = New-Object System.Collections.Generic.List[string]
            [void]$lista.Add((Split-Path -Leaf $arq.FullName))
            $usadas.Add($chave, $lista)
        }
    }
}

# ---------------------------------------------------------------------------
# 3) Chaves por cultura
# ---------------------------------------------------------------------------
$chavesPorCultura = @{}
$ordemPorCultura = @{}
foreach ($c in $culturas) {
    $caminho = Join-Path $OutDir ("$c.json")
    $chaves = @(Get-ChavesJson $caminho)
    $ordemPorCultura[$c] = $chaves
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($k in $chaves) { [void]$set.Add([string]$k) }
    $chavesPorCultura[$c] = $set
}

# ---------------------------------------------------------------------------
# 4) Relatorio
# ---------------------------------------------------------------------------
Write-Output "localizacao: $OutDir"
Write-Output "idioma default: $defaultCulture"
Write-Output "culturas: $($culturas.Count) ($($culturas -join ', '))"
Write-Output "chaves usadas no codigo: $($usadas.Count) ($ocorrencias ocorrencia(s))"
if ($usadas.Count -eq 0) {
    Write-Output '  (nenhuma - o retrofit das telas ainda nao foi feito)'
}
else {
    foreach ($chave in @($usadas.Keys)) {
        Write-Output "  usada: $chave  [$($usadas[$chave] -join ', ')]"
    }
}

Write-Output ''

# (a) chaves usadas ausentes por idioma
$ausentesDefault = @()
if ($chavesPorCultura.ContainsKey($defaultCulture)) {
    $setDefault = $chavesPorCultura[$defaultCulture]
    foreach ($chave in @($usadas.Keys)) {
        if (-not $setDefault.Contains([string]$chave)) { $ausentesDefault += $chave }
    }
}

Write-Output "ausentes em '$defaultCulture' (GRAVE): $($ausentesDefault.Count)"
foreach ($chave in $ausentesDefault) { Write-Output "  FALTA: $chave" }

foreach ($c in $culturas) {
    if ($c -eq $defaultCulture) { continue }
    $set = $chavesPorCultura[$c]
    $ausentes = @()
    foreach ($chave in @($usadas.Keys)) {
        if (-not $set.Contains([string]$chave)) { $ausentes += $chave }
    }
    Write-Output "ausentes em '$c' (aviso): $($ausentes.Count)"
    foreach ($chave in $ausentes) { Write-Output "  FALTA: $chave" }
}

Write-Output ''

# (b) chaves do default nao usadas
if ($chavesPorCultura.ContainsKey($defaultCulture)) {
    $naoUsadas = @()
    foreach ($chave in $ordemPorCultura[$defaultCulture]) {
        if (-not $usadas.Contains([string]$chave)) { $naoUsadas += $chave }
    }
    Write-Output "chaves em '$defaultCulture' nao usadas no codigo: $($naoUsadas.Count) (informativo)"
    if ($ListUnused) {
        foreach ($chave in $naoUsadas) { Write-Output "  nao usada: $chave" }
    }
    else {
        Write-Output '  (use -ListUnused para listar todas)'
    }
}

exit 0
