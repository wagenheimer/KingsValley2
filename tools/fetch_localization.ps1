# Fetch da planilha de localizacao (CSV publicado do Google Sheets) e
# regeneracao dos JSONs por idioma em src/KingsValley.Desktop/Content/Localization/.
#
# A planilha e a FONTE UNICA. O snapshot gerado vai para o git; o build nunca
# depende de rede. O manifest.json e preservado: idiomas existentes sao
# mantidos e idiomas novos encontrados na planilha sao incluidos no fim.
#
# Layout esperado (I2 Localization):
#   Term,English,German,...,Portuguese (Brazil)
#   back,Back,Zurueck,...,Voltar
# A 1a coluna e o Termo (cabecalho Term/Key/Termo); as demais sao 1 idioma por
# cabecalho (o NOME do idioma do manifest OU o proprio codigo, ex.: pt-BR).
# Colunas extras do I2 (Type, Desc) sao ignoradas.
#
# Politica de idioma sem coluna: o arquivo existente NAO e tocado (nunca zera
# um idioma por a coluna ter sumido) e um aviso e impresso.
#
# Uso:
#   powershell -File tools/fetch_localization.ps1 -Url "<csv publicado>"
#   powershell -File tools/fetch_localization.ps1 -InputFile <caminho.csv>
#   powershell -File tools/fetch_localization.ps1 -InputFile x.csv -OutDir <pasta>

[CmdletBinding()]
param(
    [string]$Url,
    [string]$InputFile,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 0) Origem: -Url (CSV publicado) ou -InputFile (CSV local)
# ---------------------------------------------------------------------------
if (-not $Url -and -not $InputFile) {
    Write-Host ''
    Write-Host 'Erro: informe a origem da planilha (-Url ou -InputFile).' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Como publicar a planilha como CSV (Google Sheets):'
    Write-Host '  Arquivo > Compartilhar > Publicar na web > (aba) > CSV > Publicar'
    Write-Host '  e passe a URL gerada em -Url.'
    Write-Host ''
    Write-Host 'Exemplo:'
    Write-Host '  powershell -File tools/fetch_localization.ps1 -Url "https://docs.google.com/spreadsheets/d/e/.../pub?output=csv"'
    Write-Host ''
    exit 1
}

# Pasta de saida padrao: <repo>/src/KingsValley.Desktop/Content/Localization
if (-not $OutDir) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutDir = Join-Path $repoRoot 'src\KingsValley.Desktop\Content\Localization'
}

# ---------------------------------------------------------------------------
# Escapa uma string como literal JSON (barra invertida, aspas e controles).
# ---------------------------------------------------------------------------
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

# Escreve um mapa chave->valor como JSON flat (mesmo formato da Task 1).
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

# Le um campo do PSCustomObject pelo nome (tolera cabecalho com espacos/dots).
function Get-Campo {
    param($Linha, [string]$Nome)

    $prop = $Linha.PSObject.Properties[$Nome]
    if ($null -eq $prop) { return '' }
    return [string]$prop.Value
}

# ---------------------------------------------------------------------------
# 1) Ler o conteudo (UTF-8, sem BOM)
# ---------------------------------------------------------------------------
$origem = $null
if ($Url) {
    Write-Output "baixando: $Url"
    $resposta = Invoke-WebRequest -Uri $Url -UseBasicParsing
    $csv = [string]$resposta.Content
    $origem = $Url
}
else {
    if (-not (Test-Path -LiteralPath $InputFile)) {
        throw "Arquivo nao encontrado: $InputFile"
    }
    $csv = [System.IO.File]::ReadAllText($InputFile, [System.Text.Encoding]::UTF8)
    $origem = (Resolve-Path -LiteralPath $InputFile).Path
}

if ([string]::IsNullOrWhiteSpace($csv)) { throw "Conteudo CSV vazio: $origem" }
if ($csv.Length -gt 0 -and [int]$csv[0] -eq 0xFEFF) { $csv = $csv.Substring(1) }

# ---------------------------------------------------------------------------
# 2) Detectar separador (virgula, tab ou ponto-e-virgula)
# ---------------------------------------------------------------------------
$primeiraLinha = @($csv -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })[0]
if (-not $primeiraLinha) { throw "Conteudo CSV vazio: $origem" }

$nTab = ([regex]::Matches($primeiraLinha, "`t")).Count
$nVirg = ([regex]::Matches($primeiraLinha, ',')).Count
$nPontoVirg = ([regex]::Matches($primeiraLinha, ';')).Count

$separador = ','
$nomeSeparador = 'virgula'
if ($nTab -gt $nVirg -and $nTab -gt $nPontoVirg) { $separador = "`t"; $nomeSeparador = 'tab' }
elseif ($nPontoVirg -gt $nVirg -and $nPontoVirg -gt $nTab) { $separador = ';'; $nomeSeparador = 'ponto-e-virgula' }
Write-Output "separador: $nomeSeparador"

$dados = @(ConvertFrom-Csv -InputObject ($csv -split "`r?`n") -Delimiter $separador)
if ($dados.Count -eq 0) { throw "CSV sem linhas de dados: $origem" }

$cabecalhos = @($dados[0].PSObject.Properties.Name)

# Coluna do Termo: cabecalho Term/Key/Termo; senao a primeira coluna.
$colTermo = $null
foreach ($h in $cabecalhos) {
    if ($h -match '^(term|termo|key)$') { $colTermo = $h; break }
}
if (-not $colTermo) { $colTermo = $cabecalhos[0] }

# ---------------------------------------------------------------------------
# 3) Manifest: carrega os idiomas existentes (codigo -> nome), preservando ordem
# ---------------------------------------------------------------------------
$manifestPath = Join-Path $OutDir 'manifest.json'
$manifesto = $null
if (Test-Path -LiteralPath $manifestPath) {
    $txtManifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    if (-not [string]::IsNullOrWhiteSpace($txtManifest)) {
        $manifesto = $txtManifest | ConvertFrom-Json
    }
}

$idiomas = New-Object System.Collections.Specialized.OrderedDictionary
if ($manifesto) {
    foreach ($l in @($manifesto.languages)) {
        if ($null -ne $l) { $idiomas[[string]$l.code] = [string]$l.name }
    }
}

# Lookup cabecalho -> codigo (por nome do manifest e pelo proprio codigo).
$codePorCabecalho = @{}
foreach ($code in @($idiomas.Keys)) {
    $codePorCabecalho[$code.ToLowerInvariant()] = $code
    $nome = ([string]$idiomas[$code]).ToLowerInvariant()
    if (-not $codePorCabecalho.ContainsKey($nome)) { $codePorCabecalho[$nome] = $code }
}

# Colunas extras do I2 que NAO sao idiomas.
$colunasIgnoradas = @('type', 'desc', 'description', 'term type', 'termtype', 'comment', 'comments')

# ---------------------------------------------------------------------------
# 4) Resolver as colunas de idioma (e incluir idiomas novos no manifest)
# ---------------------------------------------------------------------------
$colunasIdioma = New-Object System.Collections.Specialized.OrderedDictionary # cabecalho -> codigo
$idiomasNovos = @()

foreach ($h in $cabecalhos) {
    if ($h -eq $colTermo) { continue }
    $chave = $h.Trim().ToLowerInvariant()
    if ($colunasIgnoradas -contains $chave) { continue }

    if ($codePorCabecalho.ContainsKey($chave)) {
        $code = $codePorCabecalho[$chave]
    }
    else {
        # Idioma novo: inclui no fim do manifest usando o proprio cabecalho como codigo.
        $code = $h.Trim()
        if (-not $idiomas.Contains($code)) {
            $idiomas[$code] = $h.Trim()
            $idiomasNovos += $code
            Write-Output "aviso: idioma novo '$h' incluido no manifest (codigo assumido: '$code')"
        }
        $codePorCabecalho[$chave] = $code
    }

    if (-not $colunasIdioma.Contains($h)) { $colunasIdioma[$h] = $code }
}

if ($colunasIdioma.Count -eq 0) {
    throw "Nenhuma coluna de idioma reconhecida. Cabecalhos: $($cabecalhos -join ', ')"
}

# ---------------------------------------------------------------------------
# 5) Montar os valores termo -> valor por idioma (ordem de aparicao)
# ---------------------------------------------------------------------------
$valores = @{}
foreach ($code in @($idiomas.Keys)) {
    $valores[$code] = New-Object System.Collections.Specialized.OrderedDictionary
}

$totalTermos = 0
$usadas = @($colunasIdioma.Keys)
foreach ($linha in $dados) {
    $termo = (Get-Campo $linha $colTermo)
    if ([string]::IsNullOrWhiteSpace($termo)) { continue }
    $termo = $termo.Trim()
    $totalTermos++

    foreach ($h in $usadas) {
        $code = $colunasIdioma[$h]
        $valor = Get-Campo $linha $h
        if ($null -eq $valor) { $valor = '' }
        $valores[$code][$termo] = $valor
    }
}

if ($totalTermos -eq 0) {
    throw "Nenhum termo encontrado (coluna '$colTermo' vazia). Confira o layout da planilha."
}

# ---------------------------------------------------------------------------
# 6) Emitir os JSONs (UTF-8 sem BOM) e o manifest atualizado
# ---------------------------------------------------------------------------
$utf8SemBom = New-Object System.Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $OutDir)) {
    [void](New-Item -ItemType Directory -Path $OutDir -Force)
}

$codigosComColuna = New-Object System.Collections.Generic.HashSet[string]
foreach ($h in @($colunasIdioma.Keys)) { [void]$codigosComColuna.Add([string]$colunasIdioma[$h]) }

$regenerados = 0
$mantidos = @()
foreach ($code in @($idiomas.Keys)) {
    if (-not $codigosComColuna.Contains([string]$code)) { $mantidos += $code; continue }
    $caminho = Join-Path $OutDir ("$code.json")
    [System.IO.File]::WriteAllText($caminho, (Write-JsonMap $valores[$code]), $utf8SemBom)
    $regenerados++
}

# manifest.json (preserva o default; mantem a lista existente + novos no fim)
$default = 'en'
if ($manifesto -and $manifesto.default) { $default = [string]$manifesto.default }
if (-not $idiomas.Contains($default)) {
    foreach ($c in @($idiomas.Keys)) { $default = [string]$c; break }
}

$sbManifest = New-Object System.Text.StringBuilder
[void]$sbManifest.Append('{')
[void]$sbManifest.Append("`n  ""default"": ")
[void]$sbManifest.Append((ConvertTo-JsonString $default))
[void]$sbManifest.Append(',')
[void]$sbManifest.Append("`n  ""languages"": [")
$primeiro = $true
foreach ($code in @($idiomas.Keys)) {
    if (-not $primeiro) { [void]$sbManifest.Append(',') }
    $primeiro = $false
    [void]$sbManifest.Append("`n    { ""code"": ")
    [void]$sbManifest.Append((ConvertTo-JsonString $code))
    [void]$sbManifest.Append(', "name": ')
    [void]$sbManifest.Append((ConvertTo-JsonString ([string]$idiomas[$code])))
    [void]$sbManifest.Append(' }')
}
[void]$sbManifest.Append("`n  ]")
[void]$sbManifest.Append("`n}")
[System.IO.File]::WriteAllText($manifestPath, $sbManifest.ToString(), $utf8SemBom)

# ---------------------------------------------------------------------------
# 7) Relatorio
# ---------------------------------------------------------------------------
Write-Output "termos: $totalTermos"
Write-Output "idiomas: $($idiomas.Count)"
Write-Output "arquivos regerados: $regenerados"
if ($idiomasNovos.Count -gt 0) {
    Write-Output "idiomas novos no manifest: $($idiomasNovos -join ', ')"
}
foreach ($code in $mantidos) {
    Write-Output "aviso: idioma '$code' sem coluna na planilha - arquivo existente mantido intacto"
}
Write-Output "saida: $OutDir"
