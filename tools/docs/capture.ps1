# Grava os frames usados na documentação (docs/en/media).
#
# Uso (na raiz do repo):  powershell -File tools/docs/capture.ps1 [-Only knife,door]
#
# Cada demo roda o jogo com as variáveis de diagnóstico do AGENTS.md:
#   KINGSVALLEY_START_SCENE / _LEVEL / _PLAYER / _INPUT / _SKIP_LEVELSTART / _EXIT
#   KINGSVALLEY_CAPTURE_DIR (frames) + _FROM/_TO (janela)   -> o jogo encerra no fim
# Os PNGs saem em docs/en/media/frames/<demo>/ e o GIF é montado por tools/docs/make_gif.py.
param(
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$inputs = Join-Path $PSScriptRoot 'inputs'
$framesRoot = Join-Path $repo 'docs\en\media\frames'
$project = Join-Path $repo 'src\KingsValley.Desktop'

# `-Only a,b` chega como um unico argumento no `powershell -File`, entao separa aqui.
$onlySet = @()
foreach ($o in $Only) { $onlySet += ($o -split ',') }

# name, scene, level, player (room,x,y), input file, exit, from, to, every, gif?
$demos = @(
    @{ n='splash';   scene='splash'; level='1'; player='';           input='';            exit='';  from=0;   to=140; every=2; gif=$true },
    @{ n='title';    scene='splash'; level='1'; player='';           input='';            exit='';  from=380; to=380; every=1; gif=$false },
    @{ n='knife';    scene='level';  level='1'; player='';           input='knife.txt';   exit='';  from=0;   to=200; every=2; gif=$true },
    @{ n='dig';      scene='level';  level='1'; player='1,192,32,Pickaxe,Left'; input='dig.txt';     exit='';  from=0;   to=75;  every=2; gif=$true },
    @{ n='door';     scene='level';  level='2'; player='1,176,80';   input='door.txt';    exit='';  from=0;   to=240; every=2; gif=$true },
    @{ n='enemies';  scene='level';  level='1'; player='';           input='';            exit='';  from=70;  to=340; every=2; gif=$true },
    @{ n='trap';     scene='level';  level='7'; player='2,112,128';  input='trap.txt';    exit='';  from=0;   to=300; every=2; gif=$true },
    @{ n='exit';     scene='level';  level='1'; player='';           input='';            exit='0'; from=0;   to=360; every=3; gif=$true }
)

foreach ($d in $demos) {
    if ($onlySet.Count -gt 0 -and $onlySet -notcontains $d.n) { continue }

    $dir = Join-Path $framesRoot $d.n
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null

    $env:KINGSVALLEY_START_SCENE = $d.scene
    $env:KINGSVALLEY_LEVEL = $d.level
    $env:KINGSVALLEY_SKIP_LEVELSTART = '1'
    $env:KINGSVALLEY_WINDOW = '256x212'
    $env:KINGSVALLEY_CAPTURE_DIR = $dir
    $env:KINGSVALLEY_CAPTURE_FROM = "$($d.from)"
    $env:KINGSVALLEY_CAPTURE_TO = "$($d.to)"
    if ($d.player) { $env:KINGSVALLEY_PLAYER = $d.player } else { Remove-Item Env:KINGSVALLEY_PLAYER -ErrorAction SilentlyContinue }
    if ($d.input) { $env:KINGSVALLEY_INPUT = (Join-Path $inputs $d.input) } else { Remove-Item Env:KINGSVALLEY_INPUT -ErrorAction SilentlyContinue }
    if ($d.exit) { $env:KINGSVALLEY_EXIT = $d.exit } else { Remove-Item Env:KINGSVALLEY_EXIT -ErrorAction SilentlyContinue }
    Remove-Item Env:KINGSVALLEY_SCREENSHOT -ErrorAction SilentlyContinue

    Write-Host "== $($d.n): $($d.scene) stage $($d.level) frames $($d.from)..$($d.to)"
    Push-Location $project
    try { dotnet run --no-build | Out-Host } finally { Pop-Location }

    if ($d.gif) {
        python (Join-Path $PSScriptRoot 'make_gif.py') $dir (Join-Path $repo "docs\en\media\$($d.n).gif") --every $d.every --scale 1 --colors 48
    } else {
        $png = Get-ChildItem $dir -Filter *.png | Select-Object -First 1
        if ($png) { Copy-Item $png.FullName (Join-Path $repo "docs\en\media\$($d.n).png") -Force }
    }
}

Write-Host 'frames + gifs em docs/en/media'
