# Gera um .ico multi-resolucao a partir de um PNG quadrado (icone do executavel).
#   uso: .\make_ico.ps1 <png-origem> <ico-destino> [tamanhos]
# Os tamanhos padrao cobrem o que o Windows usa na barra de tarefas, no Explorer e na lista de apps.
param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$DestIco,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $SourcePng)) { throw "PNG nao encontrado: $SourcePng" }

# --- Lê a origem (aceita PNG com alfa) ---
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePng).Path)
try {
    $frames = foreach ($size in $Sizes) {
        # Redimensiona preservando o aspect ratio e centralizando (o icone e quadrado, mas
        # um PNG nao-quadrado nao deve ser distorcido).
        $scale = [Math]::Min($size / $src.Width, $size / $src.Height)
        $w = [Math]::Max(1, [int][Math]::Round($src.Width * $scale))
        $h = [Math]::Max(1, [int][Math]::Round($src.Height * $scale))

        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode     = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode       = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $dx = [int](($size - $w) / 2); $dy = [int](($size - $h) / 2)
            $g.DrawImage($src, (New-Object System.Drawing.Rectangle($dx, $dy, $w, $h)))
        } finally { $g.Dispose() }

        # Converte para PNG em memoria (o formato de entrada das entradas ICO).
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [PSCustomObject]@{ Size = $size; Data = $ms.ToArray() }
    }

    # --- Monta o .ico (formato ICO: cabecalho + ICONDIRENTRY[] + imagens PNG) ---
    $count = $frames.Count
    $header = New-Object byte[] 6
    $header[0] = 0; $header[1] = 0;   # reservado
    $header[2] = 1; $header[3] = 0;   # 1 = icone
    $header[4] = [byte]$count; $header[5] = 0

    $entrySize = 16
    $offset = 6 + ($entrySize * $count)

    $entries = New-Object System.Collections.Generic.List[byte]
    foreach ($f in $frames) {
        $len = $f.Data.Length
        $b = New-Object byte[] $entrySize
        # largura/altura: 256 e' gravado como 0 (1 byte)
        $b[0] = if ($f.Size -ge 256) { 0 } else { [byte]$f.Size }
        $b[1] = if ($f.Size -ge 256) { 0 } else { [byte]$f.Size }
        $b[2] = 0                        # paleta (0 = truecolor)
        $b[3] = 0                        # reservado
        $b[4] = 1; $b[5] = 0            # planos
        $b[6] = 32; $b[7] = 0           # bits por pixel
        [Array]::Copy([BitConverter]::GetBytes([UInt32]$len), 0, $b, 8, 4)
        [Array]::Copy([BitConverter]::GetBytes([UInt32]$offset), 0, $b, 12, 4)
        foreach ($v in $b) { $entries.Add($v) }
        $offset += $len
    }

    $out = New-Object System.Collections.Generic.List[byte]
    $out.AddRange($header)
    $out.AddRange($entries.ToArray())
    foreach ($f in $frames) { $out.AddRange($f.Data) }

    $destDir = Split-Path -Parent (Resolve-Path (Split-Path -Parent $DestIco)).Path
    [System.IO.File]::WriteAllBytes($DestIco, $out.ToArray())

    "Icone gerado: $DestIco"
    "  resolucoes: $($frames.Size -join ', ')"
    "  tamanho: $([math]::Round((Get-Item $DestIco).Length / 1KB, 1)) KB"
}
finally { $src.Dispose() }
