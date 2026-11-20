# Guardião da regra "arte de atlas por NOME" (ver AGENTS.md).
#
# A maior parte da arte de atlas das telas/componentes do projeto é ligada em runtime pelo
# GumAtlasBinder (pelo nome do sprite). Alguns sprites ficam FORA do binder por serem
# dependentes de contexto (ícone/badge do power-up, token do card, botões por categoria do
# menu) e ainda guardam `TextureLeft/Top/Width/Height` no XML.
#
# Este script resolve esses rects contra o `.atlas` correspondente e DENUNCIA os que não casam
# com nenhum sprite — sintoma de que o atlas foi reempacotado e a coordenada "driftou".
# Rode depois de `tools/pack_atlases.ps1` (ou antes de um commit que mexa em atlas).
#
#   powershell -File tools/audit_gum_atlas_coords.ps1
#
# Saída esperada: "OK" e o número de sprites ainda com coordenada (arte contextual).

param(
    [string]$Root = (Join-Path $PSScriptRoot '..\src\KingsValley.Desktop\Content')
)

$ErrorActionPreference = 'Stop'

# --- Parse dos atlases (nome -> rect "x,y,w,h" -> sprite) ---
$atlases = @{}
Get-ChildItem "$Root/Atlases" -Filter '*.atlas' | Where-Object { $_.Name -notmatch '@2x' } | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    $map = @{}
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\S' -and $lines[$i] -notmatch '\.png') {
            $name = $lines[$i].Trim()
            if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s*(\d+),(\d+),(\d+),(\d+)\s*$') {
                $map["$($Matches[1]),$($Matches[2]),$($Matches[3]),$($Matches[4])"] = $name
            }
        }
    }
    $atlases[[System.IO.Path]::GetFileNameWithoutExtension($_.Name)] = $map
}

$checked = 0
$drift = @()

Get-ChildItem "$Root/Gum" -Recurse -Include '*.gusx', '*.gucx', '*.gutx' | ForEach-Object {
    $txt = [System.IO.File]::ReadAllText($_.FullName)
    $vars = @{}
    foreach ($m in [regex]::Matches($txt, '<Variable[^>]*Name="([^"]+)"[^>]*>\s*<Value[^>]*>([^<]*)</Value>')) {
        $vars[$m.Groups[1].Value] = $m.Groups[2].Value
    }

    $prefixes = $vars.Keys | Where-Object { $_ -match '\.TextureLeft$' } | ForEach-Object { $_ -replace '\.TextureLeft$', '' } | Select-Object -Unique
    foreach ($p in $prefixes) {
        $src = $vars["$p.SourceFile"]
        if (-not $src) { continue }
        $atlas = [System.IO.Path]::GetFileNameWithoutExtension($src)
        if (-not $atlases.ContainsKey($atlas)) { continue }   # atlas de terceiros (ex.: UISpriteSheet)

        $checked++
        $key = "$($vars["$p.TextureLeft"]),$($vars["$p.TextureTop"]),$($vars["$p.TextureWidth"]),$($vars["$p.TextureHeight"])"
        if (-not $atlases[$atlas].ContainsKey($key)) {
            $drift += [pscustomobject]@{ File = $_.Name; Element = $p; Atlas = $atlas; Rect = $key }
        }
    }
}

if ($drift.Count -gt 0) {
    Write-Output "DRIFT: $($drift.Count) sprite(s) com coordenada que nao casa no atlas (repack?):"
    $drift | Format-Table -AutoSize
    exit 1
}

Write-Output "OK: $checked sprite(s) com coordenada valida contra o atlas (arte contextual)."
exit 0
