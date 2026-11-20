# Sincroniza as coordenadas de PREVIEW dos sprites de atlas do projeto (para o Gum Tool mostrar a arte).
#
# O runtime NÃO depende destas coordenadas: o GumAtlasBinder liga a arte por NOME. Estas coordenadas
# são só o "rect de preview" que o editor visual do Gum usa para desenhar cada sprite. Elas são
# GERADAS a partir de `Content/Gum/atlas-bindings.json` + os `.atlas` — nunca extraídas à mão.
#
#   powershell -File tools/sync_gum_preview_coords.ps1
#
# Rode depois de `tools/pack_atlases.ps1` (atlas reempacotado) ou depois de mexer em atlas-bindings.json.
# Se o atlas mudar e você esquecer de rodar, o JOGO continua certo (binder por nome); só o preview do
# Tool fica desatualizado. `tools/audit_gum_atlas_coords.ps1` denuncia esse drift.

param(
    [string]$Root = (Join-Path $PSScriptRoot '..\src\KingsValley.Desktop\Content')
)

$ErrorActionPreference = 'Stop'

$bindings = Get-Content (Join-Path $Root 'Gum/atlas-bindings.json') -Raw | ConvertFrom-Json

# atlas -> (sprite -> "x,y,w,h")
$atlases = @{}
Get-ChildItem "$Root/Atlases" -Filter '*.atlas' | Where-Object { $_.Name -notmatch '@2x' } | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    $map = @{}
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\S' -and $lines[$i] -notmatch '\.png') {
            $name = $lines[$i].Trim()
            if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s*(\d+),(\d+),(\d+),(\d+)\s*$') {
                $map[$name] = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4])
            }
        }
    }
    $atlases[[System.IO.Path]::GetFileNameWithoutExtension($_.Name)] = $map
}

$changedFiles = 0
$changedVars = 0

Get-ChildItem "$Root/Gum" -Recurse -Include '*.gusx', '*.gucx', '*.gutx' | ForEach-Object {
    $file = $_.Name
    $text = [System.IO.File]::ReadAllText($_.FullName)
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

    $vars = @{}
    foreach ($m in [regex]::Matches($text, '<Variable[^>]*Name="([^"]+)"[^>]*>\s*<Value[^>]*>([^<]*)</Value>')) {
        $vars[$m.Groups[1].Value] = $m.Groups[2].Value
    }

    $prefixes = @($vars.Keys | Where-Object { $_ -match '\.(TextureLeft|SourceFile)$' } |
        ForEach-Object { $_ -replace '\.(TextureLeft|SourceFile)$', '' } | Select-Object -Unique)

    $new = $text
    foreach ($prefix in $prefixes) {
        # Resolve atlas+sprite: primeiro fileBindings (contextual), depois SourceFile local + atlasBindings.
        $atlas = $null; $sprite = $null
        $fb = $bindings.fileBindings.$file
        if ($fb -and $fb.PSObject.Properties.Name -contains $prefix) {
            $parts = $fb.$prefix -split '/'
            $atlas = $parts[0]; $sprite = $parts[1]
        }
        elseif ($vars["$prefix.SourceFile"]) {
            $atlas = [System.IO.Path]::GetFileNameWithoutExtension($vars["$prefix.SourceFile"])
            $leaf = ($prefix -split '\.')[-1]
            $ab = $bindings.atlasBindings.$atlas
            if ($ab -and $ab.PSObject.Properties.Name -contains $leaf) { $sprite = $ab.$leaf }
        }

        if (-not $sprite -or -not $atlases.ContainsKey($atlas) -or -not $atlases[$atlas].ContainsKey($sprite)) { continue }
        $rect = $atlases[$atlas][$sprite]

        $pairs = @{ 'TextureLeft' = $rect[0]; 'TextureTop' = $rect[1]; 'TextureWidth' = $rect[2]; 'TextureHeight' = $rect[3] }
        $inserted = $false
        foreach ($name in $pairs.Keys) {
            $full = "$prefix.$name"
            $value = $pairs[$name]
            if ($vars.ContainsKey($full)) {
                $new = [regex]::Replace($new,
                    "(<Variable[^>]*Name=`"$([regex]::Escape($full))`"[^>]*>\s*<Value[^>]*>)[^<]*(</Value>)",
                    "`${1}$value`${2}")
            }
            else {
                # Insere depois do bloco SourceFile do mesmo prefixo (preview precisa do atlas).
                $anchor = "(<Variable[^>]*Name=`"$([regex]::Escape($prefix))\.SourceFile`"[^>]*>\s*<Value[^>]*>[^<]*</Value>\s*</Variable>)"
                $block = "$nl    <Variable Type=`"int`" Name=`"$full`" SetsValue=`"true`">$nl      <Value xsi:type=`"xsd:int`">$value</Value>$nl    </Variable>"
                $count = [regex]::Matches($new, $anchor).Count
                if ($count -eq 1) {
                    $new = [regex]::Replace($new, $anchor, "`${1}$block")
                    $inserted = $true
                }
            }
            $changedVars++
        }
    }

    if ($new -ne $text) {
        [System.IO.File]::WriteAllText($_.FullName, $new)
        $changedFiles++
    }
}

Write-Output "Preview sincronizado: $changedFiles arquivo(s), $changedVars variavel(is)."
