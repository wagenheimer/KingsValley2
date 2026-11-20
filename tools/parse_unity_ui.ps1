# Extrai hierarquia de UI (RectTransform + nomes + textos TMP) de um arquivo Unity YAML (.unity/.prefab)
param([Parameter(Mandatory=$true)][string]$Path)

$linhas = [System.IO.File]::ReadAllLines($Path, [System.Text.Encoding]::UTF8)

$nomes = @{}; $rt = @{}; $rtPorGo = @{}; $ativo = @{}; $textos = @{}; $tmpGo = @{}

$curTipo = $null; $curId = $null; $curGO = $null
foreach ($l in $linhas) {
    if ($l -match '^--- !u!224 &(\d+)') { $curTipo = 'RT'; $curId = $Matches[1]; $curGO = $null; $rt[$curId] = @{}; continue }
    if ($l -match '^--- !u!1 &(\d+)')   { $curTipo = 'GO'; $curId = $Matches[1]; continue }
    if ($l -match '^--- !u!114 &(\d+)') { $curTipo = 'MB'; $curId = $Matches[1]; $curGO = $null; continue }
    if ($l -match '^--- ') { $curTipo = $null; continue }

    if ($curTipo -eq 'GO') {
        if ($l -match '^\s+m_Name: (.+)$') { $nomes[$curId] = $Matches[1].Trim() }
        elseif ($l -match '^\s+m_IsActive: (\d+)$') { $ativo[$curId] = ($Matches[1] -eq '1') }
        continue
    }

    if ($curTipo -eq 'MB') {
        if ($l -match '^\s+m_GameObject: \{fileID: (\d+)\}') { $curGO = $Matches[1] }
        elseif ($curGO -and $l -match '^\s+m_text: (.+)$') { $textos[$curGO] = $Matches[1].Trim(); $tmpGo[$curGO] = $true }
        elseif ($curGO -and $l -match '^\s+m_fontSize: ([\d.]+)') { if ($tmpGo[$curGO]) { $textos["$curGO|fs"] = $Matches[1] } }
        continue
    }

    if ($curTipo -eq 'RT') {
        if ($l -match '^\s+m_GameObject: \{fileID: (\d+)\}') { $curGO = $Matches[1]; $rtPorGo[$curGO] = $curId; continue }
        if ($l -match '^\s+m_AnchorMin: \{x: ([\d.\-eE]+), y: ([\d.\-eE]+)\}') { $rt[$curId]["amin"] = @($Matches[1], $Matches[2]); continue }
        if ($l -match '^\s+m_AnchorMax: \{x: ([\d.\-eE]+), y: ([\d.\-eE]+)\}') { $rt[$curId]["amax"] = @($Matches[1], $Matches[2]); continue }
        if ($l -match '^\s+m_AnchoredPosition: \{x: ([\d.\-eE]+), y: ([\d.\-eE]+)\}') { $rt[$curId]["apos"] = @($Matches[1], $Matches[2]); continue }
        if ($l -match '^\s+m_SizeDelta: \{x: ([\d.\-eE]+), y: ([\d.\-eE]+)\}') { $rt[$curId]["size"] = @($Matches[1], $Matches[2]); continue }
        if ($l -match '^\s+m_Pivot: \{x: ([\d.\-eE]+), y: ([\d.\-eE]+)\}') { $rt[$curId]["pivot"] = @($Matches[1], $Matches[2]); continue }
        if ($l -match '^\s+m_Father: \{fileID: (\d+)\}') { $rt[$curId]["father"] = $Matches[1]; $coletando = $null; continue }
    }
}

$goPorRt = @{}
foreach ($goId in $rtPorGo.Keys) { $goPorRt[$rtPorGo[$goId]] = $goId }

function Caminho($rtId) {
    if (-not $rt.ContainsKey($rtId)) { return "?" }
    $goId = $goPorRt[$rtId]; if (-not $goId) { return "?" }
    $nome = $nomes[$goId]; if (-not $nome) { $nome = "?" }
    $pai = $rt[$rtId]["father"]
    if ($pai -and $rt.ContainsKey($pai)) { return (Caminho $pai) + "/" + $nome }
    return $nome
}

"### $([System.IO.Path]::GetFileName($Path))"
foreach ($goId in $rtPorGo.Keys) {
    $rtId = $rtPorGo[$goId]
    if (-not $rt.ContainsKey($rtId)) { continue }
    $r = $rt[$rtId]
    $c = Caminho $rtId
    $extra = ""
    if ($textos.ContainsKey($goId)) { $extra += " texto='$($textos[$goId])'" }
    if ($textos.ContainsKey("$goId|fs")) { $extra += " fs=$($textos["$goId|fs"])" }
    if ($ativo.ContainsKey($goId) -and -not $ativo[$goId]) { $extra += " INATIVO" }
    "$c : apos($($r.apos[0]),$($r.apos[1])) size($($r.size[0]),$($r.size[1])) anchor($($r.amin[0]),$($r.amin[1]))-($($r.amax[0]),$($r.amax[1])) pivot($($r.pivot[0]),$($r.pivot[1]))$extra"
}
