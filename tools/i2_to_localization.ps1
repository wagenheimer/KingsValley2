# Bootstrap offline da localizacao: le o asset do I2 Localization do Unity
# (I2Languages.asset, YAML) e gera um snapshot JSON por idioma em
# src/KingsValley.Desktop/Content/Localization/.
#
# Uso:
#   powershell -File tools/i2_to_localization.ps1
#   powershell -File tools/i2_to_localization.ps1 -AssetPath <caminho> -OutDir <pasta>

[CmdletBinding()]
param(
    [string]$AssetPath = 'E:\GreenSauceGames\KingsValley\Assets\_Game\Resources\I2Languages.asset',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

# Pasta de saida padrao: <repo>/src/KingsValley.Desktop/Content/Localization
if (-not $OutDir) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutDir = Join-Path $repoRoot 'src\KingsValley.Desktop\Content\Localization'
}

if (-not (Test-Path -LiteralPath $AssetPath)) {
    throw "Asset nao encontrado: $AssetPath"
}

# ---------------------------------------------------------------------------
# Escapes de string dupla-aspas do YAML:
#   \xHH (latin-1), \uHHHH (unicode), \U00HHHHHH, \n \t \r \\ \" \/
# Aplicado somente ao conteudo de scalars dupla-aspas.
# ---------------------------------------------------------------------------
function ConvertFrom-I2Escapes {
    param([string]$s)

    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $s.Length) {
        $c = $s[$i]
        if ($c -eq '\' -and ($i + 1) -lt $s.Length) {
            $n = $s[$i + 1]
            if ($n -eq 'x' -and ($i + 3) -lt $s.Length) {
                [void]$sb.Append([char][Convert]::ToInt32($s.Substring($i + 2, 2), 16))
                $i += 4
            }
            elseif ($n -eq 'u' -and ($i + 5) -lt $s.Length) {
                [void]$sb.Append([char][Convert]::ToInt32($s.Substring($i + 2, 4), 16))
                $i += 6
            }
            elseif ($n -eq 'U' -and ($i + 9) -lt $s.Length) {
                [void]$sb.Append([char]::ConvertFromUtf32([Convert]::ToInt32($s.Substring($i + 2, 8), 16)))
                $i += 10
            }
            elseif ($n -eq 'n') { [void]$sb.Append("`n"); $i += 2 }
            elseif ($n -eq 't') { [void]$sb.Append("`t"); $i += 2 }
            elseif ($n -eq 'r') { [void]$sb.Append("`r"); $i += 2 }
            elseif ($n -eq '"') { [void]$sb.Append('"'); $i += 2 }
            elseif ($n -eq '\') { [void]$sb.Append('\'); $i += 2 }
            elseif ($n -eq '/') { [void]$sb.Append('/'); $i += 2 }
            else { [void]$sb.Append($n); $i += 2 }
        }
        else {
            [void]$sb.Append($c)
            $i++
        }
    }
    return $sb.ToString()
}

# ---------------------------------------------------------------------------
# Monta um scalar YAML (flow scalar) a partir dos segmentos de linha.
# Cada segmento tem { t = texto bruto; blank = linha vazia }.
#   - linhas de continuacao dobram para um unico espaco;
#   - uma linha vazia dobra para um newline (\n) — quebra de paragrafo;
#   - estilo: 'double' (escapes), 'single' ('' -> '), 'plain' (literal).
# ---------------------------------------------------------------------------
function ConvertFrom-I2Scalar {
    param($Segmentos)

    $estilo = 'plain'
    if ($Segmentos[0].t -match '^\s*"') { $estilo = 'double' }
    elseif ($Segmentos[0].t -match "^\s*'") { $estilo = 'single' }

    $sb = New-Object System.Text.StringBuilder
    $pendenteNl = $false
    $iniciado = $false
    foreach ($seg in $Segmentos) {
        if ($seg.blank) { $pendenteNl = $true; continue }
        $t = $seg.t.Trim()
        if (-not $iniciado) { [void]$sb.Append($t); $iniciado = $true }
        elseif ($pendenteNl) { [void]$sb.Append("`n"); [void]$sb.Append($t); $pendenteNl = $false }
        else { [void]$sb.Append(' '); [void]$sb.Append($t) }
    }
    $s = $sb.ToString()

    if ($estilo -eq 'double') {
        if ($s.Length -ge 2 -and $s[0] -eq '"' -and $s[$s.Length - 1] -eq '"') {
            $s = $s.Substring(1, $s.Length - 2)
        }
        return (ConvertFrom-I2Escapes $s)
    }
    if ($estilo -eq 'single') {
        if ($s.Length -ge 2 -and $s[0] -eq "'" -and $s[$s.Length - 1] -eq "'") {
            $s = $s.Substring(1, $s.Length - 2)
        }
        return $s.Replace("''", "'")
    }
    return $s
}

# Escapa uma string como literal JSON (barra invertida, aspas e controles).
function ConvertTo-JsonString {
    param([string]$Value)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Value.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($code -eq 8) { [void]$sb.Append('\b') }
        elseif ($code -eq 9) { [void]$sb.Append('\t') }
        elseif ($code -eq 10) { [void]$sb.Append('\n') }
        elseif ($code -eq 12) { [void]$sb.Append('\f') }
        elseif ($code -eq 13) { [void]$sb.Append('\r') }
        elseif ($code -lt 0x20) { [void]$sb.Append('\u{0:x4}' -f $code) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

# ---------------------------------------------------------------------------
# 1) Idiomas (bloco mLanguages:)
# ---------------------------------------------------------------------------
$linhas = [System.IO.File]::ReadAllLines($AssetPath, [System.Text.Encoding]::UTF8)

$idiomas = New-Object System.Collections.Specialized.OrderedDictionary
$emIdiomas = $false
$nomePendente = $null

foreach ($l in $linhas) {
    if ($l -match '^\s*mLanguages:\s*$') { $emIdiomas = $true; continue }
    if ($emIdiomas) {
        if ($l -match '^    - Name: (.*)$') { $nomePendente = $Matches[1].Trim(); continue }
        if ($l -match '^      Code: (.*)$' -and $nomePendente) {
            $codigo = $Matches[1].Trim()
            if (-not $idiomas.Contains($codigo)) { $idiomas.Add($codigo, $nomePendente) }
            $nomePendente = $null
            continue
        }
        # Sai do bloco ao encontrar uma chave de nivel superior (ex.: IgnoreDeviceLanguage:)
        if ($l -match '^    \S') { break }
    }
}

if ($idiomas.Count -eq 0) { throw "Nenhum idioma encontrado no bloco mLanguages: de $AssetPath" }

$codigos = @($idiomas.Keys)
$n = $codigos.Count

# ---------------------------------------------------------------------------
# 2) Termos (bloco mTerms:, somente TermType: 0)
# ---------------------------------------------------------------------------
# valores[codigo][termo] = valor (ordem de aparicao preservada)
$valores = @{}
foreach ($codigo in $codigos) {
    $valores[$codigo] = New-Object System.Collections.Specialized.OrderedDictionary
}

$totalTermos = 0
$ignorados = 0
$duplicados = 0
$i = 0
while ($i -lt $linhas.Count) {
    if ($linhas[$i] -match '^    - Term: (.*)$') {
        $termo = $Matches[1].Trim()
        $i++
        $tipo = 0
        $valoresIdioma = $null
        while ($i -lt $linhas.Count -and $linhas[$i] -notmatch '^    - Term: ') {
            $linha = $linhas[$i]
            if ($linha -match '^      TermType: (\d+)\s*$') { $tipo = [int]$Matches[1] }
            elseif ($linha -match '^      Languages:\s*$') {
                # Cada valor comeca com '      - '; linhas indentadas com 8 espacos
                # (ou vazias) sao continuacoes do scalar corrente. Guarda os segmentos
                # crus; o scalar e montado depois (folding + estilo).
                $valoresIdioma = New-Object System.Collections.Generic.List[object]
                $segmentos = $null
                $j = $i + 1
                while ($j -lt $linhas.Count) {
                    $lj = $linhas[$j]
                    if ($lj -match '^      - (.*)$') {
                        $segmentos = New-Object System.Collections.Generic.List[object]
                        $segmentos.Add([pscustomobject]@{ t = $Matches[1]; blank = $false })
                        $valoresIdioma.Add($segmentos)
                        $j++
                    }
                    elseif ($lj -match '^        (.+)$') {
                        if ($null -ne $segmentos) {
                            $segmentos.Add([pscustomobject]@{ t = $Matches[1]; blank = $false })
                        }
                        $j++
                    }
                    elseif ($lj -match '^\s*$') {
                        if ($null -ne $segmentos) {
                            $segmentos.Add([pscustomobject]@{ t = ''; blank = $true })
                        }
                        $j++
                    }
                    else { break }
                }
                $valoresIdioma = @($valoresIdioma | ForEach-Object { ConvertFrom-I2Scalar $_ })
                $i = $j
                continue
            }
            $i++
        }

        if ($tipo -ne 0) { $ignorados++; continue }
        $totalTermos++
        if ($null -eq $valoresIdioma -or $valoresIdioma.Count -ne $n) {
            throw "Termo '$termo': esperava $n valores de idioma, encontrou $(if ($null -eq $valoresIdioma) { 0 } else { $valoresIdioma.Count })"
        }
        for ($k = 0; $k -lt $n; $k++) {
            $codigo = $codigos[$k]
            if ($valores[$codigo].Contains($termo)) { $duplicados++; continue }
            $valores[$codigo].Add($termo, $valoresIdioma[$k])
        }
        continue
    }
    $i++
}

if ($totalTermos -eq 0) { throw "Nenhum termo (TermType: 0) encontrado em $AssetPath" }

# ---------------------------------------------------------------------------
# 3) Emitir JSONs (UTF-8 sem BOM, escrito a mao para manter acentos/CJK legiveis)
# ---------------------------------------------------------------------------
$utf8SemBom = New-Object System.Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $OutDir)) {
    [void](New-Item -ItemType Directory -Path $OutDir -Force)
}

function Write-JsonMap {
    param([System.Collections.Specialized.OrderedDictionary]$Map)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{')
    $primeiro = $true
    foreach ($chave in $Map.Keys) {
        if (-not $primeiro) { [void]$sb.Append(',') }
        $primeiro = $false
        [void]$sb.Append("`n  ")
        [void]$sb.Append((ConvertTo-JsonString $chave))
        [void]$sb.Append(': ')
        [void]$sb.Append((ConvertTo-JsonString ([string]$Map[$chave])))
    }
    if (-not $primeiro) { [void]$sb.Append("`n") }
    [void]$sb.Append('}')
    return $sb.ToString()
}

foreach ($codigo in $codigos) {
    $caminho = Join-Path $OutDir ("$codigo.json")
    [System.IO.File]::WriteAllText($caminho, (Write-JsonMap $valores[$codigo]), $utf8SemBom)
}

# manifest.json
$sbManifest = New-Object System.Text.StringBuilder
[void]$sbManifest.Append('{')
[void]$sbManifest.Append("`n  ""default"": ""en"",")
[void]$sbManifest.Append("`n  ""languages"": [")
$primeiro = $true
foreach ($codigo in $codigos) {
    if (-not $primeiro) { [void]$sbManifest.Append(',') }
    $primeiro = $false
    [void]$sbManifest.Append("`n    { ""code"": ")
    [void]$sbManifest.Append((ConvertTo-JsonString $codigo))
    [void]$sbManifest.Append(', "name": ')
    [void]$sbManifest.Append((ConvertTo-JsonString $idiomas[$codigo]))
    [void]$sbManifest.Append(' }')
}
[void]$sbManifest.Append("`n  ]")
[void]$sbManifest.Append("`n}")
[System.IO.File]::WriteAllText((Join-Path $OutDir 'manifest.json'), $sbManifest.ToString(), $utf8SemBom)

# ---------------------------------------------------------------------------
# 4) Relatorio
# ---------------------------------------------------------------------------
Write-Output "termos: $totalTermos"
Write-Output "idiomas: $n"
Write-Output "termos de texto: $totalTermos (ignorados: $ignorados de textura)"
if ($duplicados -gt 0) { Write-Output "aviso: $duplicados valores duplicados (primeira ocorrencia mantida)" }
Write-Output "saida: $OutDir"
