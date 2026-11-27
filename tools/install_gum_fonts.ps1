# Instala as fontes do projeto (per-usuario, SEM admin) para o Gum Tool/`gumcli` renderizarem a
# fonte certa no editor.
#
# Por que e necessario: no Gum, o `CustomFontFile` e o fluxo antigo de bitmap `.fnt` (o file picker
# do "Use Custom Font" so aceita `.fnt`). O caminho com WYSIWYG no editor e o dropdown `Font` com
# uma FAMILIA INSTALADA. As telas/componentes do projeto usam `Font` = o nome da familia
# (see the Fonts section in AGENTS.md).
#
# Como funciona: copia o TTF/OTF para %LOCALAPPDATA%\Microsoft\Windows\Fonts e registra em
# HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts. Nao precisa de administrador e e
# reversivel (basta remover o arquivo + o valor do registro; ou reinstalar/desinstalar pelo
# painel de Fontes do Windows).
#
# Uso:  powershell -File tools/install_gum_fonts.ps1

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'src\KingsValley.Desktop\Content\Gum\Fonts'
$targetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
$registryKey = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'

# Arquivo no repo -> nome de registro (o rotulo que aparece no painel de Fontes)
$fonts = @(
    @{ File = 'Berlin Sans FB Demi Bold.ttf'; Label = 'Berlin Sans FB Demi (TrueType)' },
    @{ File = 'KOMIKAX_.ttf';                 Label = 'Komika Axis (TrueType)' },
    @{ File = 'RaspoutineDemiBold_TB.otf';    Label = 'Raspoutine DemiBold (OpenType)' }
)

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

foreach ($font in $fonts) {
    $from = Join-Path $sourceDir $font.File
    if (-not (Test-Path $from)) {
        Write-Warning "Fonte nao encontrada, ignorando: $from"
        continue
    }

    $to = Join-Path $targetDir $font.File

    try {
        # Se o arquivo ja esta instalado ele pode estar em uso (carregado no processo atual).
        if (-not (Test-Path $to)) {
            Copy-Item $from $to -Force
        }
    }
    catch {
        Write-Warning "Nao consegui copiar '$($font.File)' (provavelmente ja instalada/em uso): $($_.Exception.Message)"
    }

    New-ItemProperty -Path $registryKey -Name $font.Label -Value $to -PropertyType String -Force | Out-Null
    Write-Host "[install_gum_fonts] $($font.Label) -> $to"
}

Write-Host '[install_gum_fonts] Pronto. Reinicie o Gum Tool (e o jogo) para ele enxergar as fontes.'
Write-Host '[install_gum_fonts] Depois rode: dotnet gumcli fonts src/KingsValley.Desktop/Content/Gum/GumProject.gumx'
