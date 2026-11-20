# Gera atlases 2x SIMULADOS a partir dos atlases 1x, para validar o pipeline de
# multi-resolução (ver "independência de resolução" no AGENTS.md).
#
# O que faz, para cada atlas:
#   1. Amplia o PNG em 200% com interpolação de alta qualidade.
#   2. Reescreve o .atlas dobrando os source rects (as origens são normalizadas 0-1, não mudam).
#
# Os arquivos gerados (*@2x.*) são artefatos de teste — podem ser apagados e regerados.
#
# Uso:
#   pwsh tools/make_atlas_2x.ps1
#   $env:KINGSVALLEY_ART_SCALE = "2"   # faz o jogo carregar os atlases 2x
#   dotnet run --project src/KingsValley.Desktop

param(
    [string]$AtlasesPath = "$PSScriptRoot\..\src\KingsValley.Desktop\Content\Atlases",
    [float]$Scale = 2.0
)

Add-Type -AssemblyName System.Drawing

$suffix = "@{0:0}x" -f $Scale
# Mesmos nomes gerados pelo pack_atlases.ps1 — a regra do AGENTS.md exige que os conjuntos
# 1x/2x/4x tenham OS MESMOS sprites.
$names = @("tokens", "board", "logo", "menu", "dialogs", "levelhud")

foreach ($name in $names) {
    $atlas1x = Join-Path $AtlasesPath "$name.atlas"
    $png1x = Join-Path $AtlasesPath "$name.png"

    if (-not (Test-Path $atlas1x) -or -not (Test-Path $png1x)) {
        Write-Warning "Atlas 1x nao encontrado: $name (esperado $atlas1x)"
        continue
    }

    $atlas2x = Join-Path $AtlasesPath "$name$suffix.atlas"
    $png2x = Join-Path $AtlasesPath "$name$suffix.png"

    # --- 1. PNG ampliado ---
    $src = [System.Drawing.Image]::FromFile($png1x)
    $w = [int][Math]::Round($src.Width * $Scale)
    $h = [int][Math]::Round($src.Height * $Scale)

    $dst = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, 0, 0, $w, $h)
    $g.Dispose()
    $src.Dispose()
    $dst.Save($png2x, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()

    # --- 2. .atlas com os rects dobrados ---
    # Formato Nez: nome / x,y,w,h / origemNormalizada (repetido), com uma linha em branco
    # separando a (inexistente) secao de animacoes.
    $lines = Get-Content -LiteralPath $atlas1x
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0

    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { break }   # fim da secao de sprites

        $out.Add($line)                                       # nome
        $rectParts = $lines[$i + 1].Trim().Split(',')
        # O separador precisa ser TAB real (o loader do Nez nao aceita "\t" literal).
        $x = [int][Math]::Round([int]$rectParts[0] * $Scale)
        $y = [int][Math]::Round([int]$rectParts[1] * $Scale)
        $rw = [int][Math]::Round([int]$rectParts[2] * $Scale)
        $rh = [int][Math]::Round([int]$rectParts[3] * $Scale)
        $out.Add([string]::Concat([char]9, $x, ",", $y, ",", $rw, ",", $rh))
        $out.Add($lines[$i + 2])                              # origem (normalizada -> nao muda)

        $i += 3
    }

    # Cabecalho "scale:" no padrao libGDX nao pode ir no arquivo lido pelo Nez (o loader nao
    # tem suporte a header). A escala e informada ao catalogo pelo jogo (KINGSVALLEY_ART_SCALE).
    Set-Content -LiteralPath $atlas2x -Value $out -Encoding UTF8

    Write-Host "[OK] $name : $png2x ($w x $h) + $atlas2x" -ForegroundColor Green
}

Write-Host "Atlases ${Scale}x gerados." -ForegroundColor Cyan
