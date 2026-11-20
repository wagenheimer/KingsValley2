<#
.SYNOPSIS
    Exporta a logo composta (`logo_frame` do Unity) para um JSON que o MonoGame consome.

.DESCRIPTION
    A tela de Loading e o MainMenu do Unity instanciam `logo_frame.prefab`: uma logo de 16 sprites
    em camadas (cavalo-ferradura, personagens, animais, wordmark) animada por 3 Animator
    Controllers independentes.

    Este script le o prefab (hierarquia + posicoes + sprites + ordem de desenho) e os 3 clipes
    (`Logo.anim`, `char1.anim`, `char2.anim`) e emite um JSON "achatado", sem YAML nem GUIDs, que
    o runtime do MonoGame interpreta.

    ## Por que um script (e nao editar o JSON na mao)

    O JSON e um **artefato gerado**. A fonte da verdade continua sendo o projeto Unity: quando a
    arte ou a animacao mudar la, rode este script de novo.

    ## Mapeamento de unidades (por que 1:1)

    O Canvas do Unity usa `m_ReferenceResolution: 1430x768` com `m_MatchWidthOrHeight: 1` (casando
    a ALTURA) — exatamente o espaco de design deste port (`DesignViewport`, zoom = altura/768).
    Os sprites usam `spritePixelsToUnits: 1` e pivo central, entao **1 pixel do PNG = 1 unidade de
    design** e as posicoes do prefab entram direto, sem conversao.

.PARAMETER UnityAssets
    Raiz dos assets do projeto Unity original.

.PARAMETER Output
    Caminho do JSON gerado (relativo a raiz do repo).

.EXAMPLE
    powershell -File tools/export_logo_frame.ps1
#>
[CmdletBinding()]
param(
    [string]$UnityAssets = "E:\GreenSauceGames\KingsValley\Assets\_Game",
    [string]$Output = "src\KingsValley.Desktop\Content\Data\Logo\logo_frame.json"
)

$ErrorActionPreference = "Stop"
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function N([double]$v) {
    # Sempre ponto decimal: o JSON nao aceita virgula e a cultura do Windows usa virgula.
    return $v.ToString("0.######", $inv)
}

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Arquivo nao encontrado: $path" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

# ---------------------------------------------------------------------------
# 1. Parse do prefab
# ---------------------------------------------------------------------------

function Split-Yaml([string]$raw) {
    # Unity YAML = varios documentos "--- !u!<classID> &<fileID>".
    $docs = @{}
    $cur = $null
    $buf = $null
    foreach ($l in ($raw -split "`r?`n")) {
        if ($l -match '^--- !u!(\d+) &(\d+)') {
            if ($cur) { $docs[$cur] = ($buf -join "`n") }
            $cur = "$($Matches[1])/$($Matches[2])"
            $buf = @()
        } elseif ($cur) {
            $buf += $l
        }
    }
    if ($cur) { $docs[$cur] = ($buf -join "`n") }
    return $docs
}

function Field([string]$text, [string]$name) {
    $m = [regex]::Match($text, "(?m)^\s*$([regex]::Escape($name)):\s*(\{.*\}|\S.*)$")
    if ($m.Success) { return $m.Groups[1].Value.Trim() } else { return "" }
}

function Vec2([string]$s) {
    $x = [regex]::Match($s, 'x:\s*([-\d.eE]+)').Groups[1].Value
    $y = [regex]::Match($s, 'y:\s*([-\d.eE]+)').Groups[1].Value
    return @([double]::Parse($x, $inv), [double]::Parse($y, $inv))
}

function QuatZ([string]$s) {
    # Os ossos so giram em Z (qx = qy = 0). Angulo = 2*atan2(qz, qw), em graus.
    $z = [regex]::Match($s, 'z:\s*([-\d.eE]+)').Groups[1].Value
    $w = [regex]::Match($s, 'w:\s*([-\d.eE]+)').Groups[1].Value
    if (-not $z -or -not $w) { return 0.0 }
    $rad = 2.0 * [Math]::Atan2([double]::Parse($z, $inv), [double]::Parse($w, $inv))
    return $rad * 180.0 / [Math]::PI
}

$prefabPath = Join-Path $UnityAssets "Prefabs\Interface\logo_frame.prefab"
$prefab = Split-Yaml (Read-Text $prefabPath)

# 1.1 Nomes dos GameObjects
$goName = @{}
foreach ($k in $prefab.Keys) {
    if ($k -like '1/*') { $goName[$k.Split('/')[1]] = (Field $prefab[$k] 'm_Name') }
}

# 1.2 Transforms: filho -> pai, posicao local, rotacao local, ordem dos irmaos
$tfByGo = @{}
$tfInfo = @{}
$childrenOf = @{}
foreach ($k in ($prefab.Keys | Where-Object { $_ -like '4/*' -or $_ -like '224/*' })) {
    $id = $k.Split('/')[1]
    $t = $prefab[$k]
    $go = [regex]::Match($t, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    $fa = [regex]::Match($t, 'm_Father: \{fileID: (\d+)\}').Groups[1].Value

    $tfInfo[$id] = @{
        Go    = $go
        Za    = $fa
        Pos   = (Vec2 (Field $t 'm_LocalPosition'))
        RotZ  = (QuatZ (Field $t 'm_LocalRotation'))
        Kids  = @([regex]::Matches($t, '(?m)^  - \{fileID: (\d+)\}') | ForEach-Object { $_.Groups[1].Value })
    }
    $tfByGo[$go] = $id

    if ($fa -ne '0' -and $fa -ne '') {
        if (-not $childrenOf.ContainsKey($fa)) { $childrenOf[$fa] = @() }
        $childrenOf[$fa] += $id
    }
}

# 1.3 SpriteRenderers: sprite (guid -> caminho no Content), ordem de desenho
$spriteByGo = @{}
foreach ($k in ($prefab.Keys | Where-Object { $_ -like '212/*' })) {
    $t = $prefab[$k]
    $go = [regex]::Match($t, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    $g = [regex]::Match($t, 'm_Sprite: \{fileID: (\d+), guid: ([0-9a-f]+)').Groups[2].Value
    $spriteByGo[$go] = @{
        Guid  = $g
        Order = [int](Field $t 'm_SortingOrder')
    }
}

# 1.4 guid -> caminho relativo ao Content ("Textures/...")
$guidToContent = @{}
Get-ChildItem (Join-Path $UnityAssets "Art") -Recurse -Filter "*.png.meta" -ErrorAction SilentlyContinue | ForEach-Object {
    $head = (Get-Content $_.FullName -TotalCount 3) -join "`n"
    if ($head -match 'guid: ([0-9a-f]{32})') {
        $rel = $_.FullName.Replace((Join-Path $UnityAssets "Art") + "\", "").Replace('.png.meta', '.png')
        $guidToContent[$Matches[1]] = $rel.Replace('\', '/')
    }
}

# 1.5 Nos opcionais de Collector's Edition (CEEnableDisable)
$ceScriptGuid = '55f412ebe411001429b37b3d2b16f36a'
$ceOnlyByGo = @{}
foreach ($k in ($prefab.Keys | Where-Object { $_ -like '114/*' })) {
    $t = $prefab[$k]
    $g = [regex]::Match($t, 'm_Script: \{fileID: \d+, guid: ([0-9a-f]+)').Groups[1].Value
    if ($g -eq $ceScriptGuid) {
        $go = [regex]::Match($t, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
        $ceOnlyByGo[$go] = $true
    }
}

# 1.6 Percorre a arvore (depth-first) montando a lista plana com indice do pai
$nodes = New-Object System.Collections.Generic.List[hashtable]
$indexById = @{}

function Add-Node([string]$tfId, [int]$parent) {
    $tf = $tfInfo[$tfId]
    $go = $tf.Go
    $name = if ($goName.ContainsKey($go)) { $goName[$go] } else { '' }

    $sprite = $null
    $order = 0
    if ($spriteByGo.ContainsKey($go)) {
        $s = $spriteByGo[$go]
        $order = $s.Order
        if ($guidToContent.ContainsKey($s.Guid)) {
            # O prefab do Unity fica em referenciado por GUID; o caminho final e sempre
            # Art/Textures/<resto> -> Content/Textures/<resto>.
            $sprite = 'Textures/' + $guidToContent[$s.Guid].Substring('Textures/'.Length)
        } else {
            throw "Sprite sem arquivo: guid $($s.Guid) em '$name'"
        }
    }

    $idx = $nodes.Count
    $nodes.Add(@{
        Name    = $name
        Parent  = $parent
        X       = $tf.Pos[0]
        Y       = $tf.Pos[1]
        RotZ    = $tf.RotZ
        Order   = $order
        Sprite  = $sprite
        CeOnly  = [bool]$ceOnlyByGo.ContainsKey($go)
    })
    $indexById[$tfId] = $idx

    if ($tf.Kids.Count -gt 0) {
        # Ordem declarada no prefab (m_Children) = ordem dos irmaos no Unity. E ela que desempata
        # a ordem de desenho quando dois sprites tem o mesmo m_SortingOrder.
        foreach ($c in $tf.Kids) { Add-Node $c $idx }
    }
}

# A raiz e o (unico) Transform sem pai. O @(...) e obrigatorio: indexar um unico item no
# PowerShell devolveria o CARACTERE 0 da string em vez do elemento.
$rootTf = @($tfInfo.Keys | Where-Object { $tfInfo[$_].Za -eq '0' -or $tfInfo[$_].Za -eq '' })[0]
Write-Host "[export] raiz: $rootTf ($($goName[$tfInfo[$rootTf].Go]))"
Add-Node $rootTf -1

Write-Host "[export] $($nodes.Count) nos, $($indexById.Count) transforms indexados."

# ---------------------------------------------------------------------------
# 2. Parse dos clipes de animacao
# ---------------------------------------------------------------------------

# Secao m_EditorCurves: visao escalar e COMPLETA do clipe (posicao, euler, IsActive e campos de
# MonoBehaviour). As secoes tipadas (m_PositionCurves/m_EulerCurves/m_FloatCurves) sao a mesma
# coisa em outro formato — ler as duas duplica as chaves, entao lemos so a EditorCurves.
function Editor-Curves([string]$raw) {
    $start = $raw.IndexOf("  m_EditorCurves:")
    if ($start -lt 0) { return @() }
    $rest = $raw.Substring($start)
    $end = [regex]::Match($rest.Substring(("  m_EditorCurves:").Length), "(?m)^  m_\w+:")
    if ($end.Success) { $rest = $rest.Substring(0, ("  m_EditorCurves:").Length + $end.Index) }

    $out = @()
    $blocks = [regex]::Matches($rest, '(?ms)^  - serializedVersion: 2\s*\r?\n    curve:.*?(?=^  - serializedVersion: 2|\z)')
    foreach ($b in $blocks) {
        $txt = $b.Value
        $attr = [regex]::Match($txt, '(?m)^    attribute: (.+?)\r?$').Groups[1].Value.Trim()
        $path = [regex]::Match($txt, '(?m)^    path: (.+?)\r?$').Groups[1].Value.Trim()
        $cls = [regex]::Match($txt, '(?m)^    classID: (\d+)').Groups[1].Value
        if (-not $attr) { continue }

        $keys = @()
        foreach ($k in [regex]::Matches($txt, 'time: ([-\d.eE]+)\s*\r?\n\s*value: ([-\d.eE]+)')) {
            $keys += , @([double]::Parse($k.Groups[1].Value, $inv), [double]::Parse($k.Groups[2].Value, $inv))
        }
        if ($keys.Count -eq 0) { continue }

        $out += @{ Attr = $attr; Path = $path; ClassId = $cls; Keys = $keys }
    }
    return $out
}

# Canais que sabemos animar. O resto (campos de MonoBehaviour: brilho 2DxFX etc.) fica fora.
function Channel-Of([string]$attr, [string]$cls) {
    switch ($attr) {
        'm_LocalPosition.x' { return 'posX' }
        'm_LocalPosition.y' { return 'posY' }
        'localEulerAnglesRaw.z' { return 'rotZ' }
        'm_IsActive' { if ($cls -eq '1') { return 'active' } else { return $null } }
        default { return $null }
    }
}

function Find-NodeIndex([int]$from, [string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $from }
    $cur = $from
    foreach ($seg in $path.Split('/')) {
        $found = -1
        for ($i = 0; $i -lt $nodes.Count; $i++) {
            if ($nodes[$i].Parent -eq $cur -and $nodes[$i].Name -eq $seg) { $found = $i; break }
        }
        if ($found -lt 0) { return -1 }
        $cur = $found
    }
    return $cur
}

$clips = @(
    @{ Name = 'Logo';  File = 'Animations\Logo\Logo.anim';  Root = 'logo_frame' },
    @{ Name = 'char1'; File = 'Animations\Char\char1.anim'; Root = 'char1' },
    @{ Name = 'char2'; File = 'Animations\Char\char2.anim'; Root = 'char2' }
)

$clipData = @()
foreach ($c in $clips) {
    $raw = Read-Text (Join-Path $UnityAssets $c.File)
    $loop = [regex]::Match($raw, 'm_LoopTime:\s*(\d)').Groups[1].Value
    $sample = [double]::Parse([regex]::Match($raw, 'm_SampleRate:\s*([\d.]+)').Groups[1].Value, $inv)

    $rootIdx = -1
    for ($i = 0; $i -lt $nodes.Count; $i++) { if ($nodes[$i].Name -eq $c.Root -and $nodes[$i].Parent -ge 0) { $rootIdx = $i; break } }
    if ($rootIdx -lt 0) { $rootIdx = 0 }

    $curves = @()
    $duration = 0.0
    foreach ($e in (Editor-Curves $raw)) {
        $ch = Channel-Of $e.Attr $e.ClassId
        if (-not $ch) { continue }
        $ni = Find-NodeIndex $rootIdx $e.Path
        if ($ni -lt 0) { throw "Path '$($e.Path)' do clipe $($c.Name) nao existe no prefab." }

        $last = $e.Keys[$e.Keys.Count - 1][0]
        if ($last -gt $duration) { $duration = $last }
        $curves += @{ Node = $ni; Channel = $ch; Keys = $e.Keys }
    }

    $clipData += @{ Name = $c.Name; Root = $rootIdx; Duration = $duration; Loop = ($loop -eq '1'); Curves = $curves }
    Write-Host ("[export] clipe {0}: {1} curvas, duracao {2}s, loop={3}" -f $c.Name, $curves.Count, (N $duration), ($loop -eq '1'))
}

# ---------------------------------------------------------------------------
# 3. Emite o JSON
# ---------------------------------------------------------------------------

$sb = New-Object System.Text.StringBuilder
function W([string]$s) { [void]$sb.AppendLine($s) }

W '{'
W '  "//": "GERADO por tools/export_logo_frame.ps1 a partir de logo_frame.prefab + os clipes do Unity. NAO editar a mao.",'
W '  "units": "1 unidade de design = 1 pixel do PNG do Unity (Canvas 1430x768, match height, PPU 1)",'
W '  "nodes": ['
for ($i = 0; $i -lt $nodes.Count; $i++) {
    $n = $nodes[$i]
    $sprite = if ($n.Sprite) { '"' + $n.Sprite + '"' } else { 'null' }
    $comma = if ($i -lt $nodes.Count - 1) { ',' } else { '' }
    W ("    {{ ""name"": ""{0}"", ""parent"": {1}, ""pos"": [{2}, {3}], ""rotZ"": {4}, ""order"": {5}, ""sprite"": {6}, ""optional"": {7} }}{8}" -f `
        $n.Name, $n.Parent, (N $n.X), (N $n.Y), (N $n.RotZ), $n.Order, $sprite, ($n.CeOnly.ToString().ToLower()), $comma)
}
W '  ],'
W '  "clips": ['
for ($ci = 0; $ci -lt $clipData.Count; $ci++) {
    $c = $clipData[$ci]
    $comma = if ($ci -lt $clipData.Count - 1) { ',' } else { '' }
    W ("    {{ ""name"": ""{0}"", ""root"": {1}, ""duration"": {2}, ""loop"": {3}, ""curves"": [" -f `
        $c.Name, $c.Root, (N $c.Duration), ($c.Loop.ToString().ToLower()))
    for ($vi = 0; $vi -lt $c.Curves.Count; $vi++) {
        $v = $c.Curves[$vi]
        $keys = ($v.Keys | ForEach-Object { '[' + (N $_[0]) + ', ' + (N $_[1]) + ']' }) -join ', '
        $vcomma = if ($vi -lt $c.Curves.Count - 1) { ',' } else { '' }
        W ("      {{ ""node"": {0}, ""channel"": ""{1}"", ""keys"": [{2}] }}{3}" -f $v.Node, $v.Channel, $keys, $vcomma)
    }
    W ("    ]}}{0}" -f $comma)
}
W '  ]'
W '}'

$outPath = Join-Path (Get-Location) $Output
$dir = Split-Path -Parent $outPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[export] escrito: $outPath"
