param(
    [string]$Csv = "$env:TEMP\gum_sprite_map.csv",
    [string]$Out = 'src/KingsValley.Desktop/Content/Gum/atlas-bindings.json'
)

# Fonte da verdade dos papéis LIVRES de contexto (atlas -> elemento -> sprite).
$atlasBindings = [ordered]@{
    'dialogs'  = [ordered]@{
        'Frame' = 'frame_small'; 'FrameBig' = 'frame_big'; 'TitleBand' = 'frame_title'; 'FrameTitle' = 'frame_title'
        'Background' = 'frame_big'; 'NameField' = 'profile_name_field'
        'Normal' = 'btn_med'; 'Focused' = 'btn_med.focused'; 'Disabled' = 'btn_med.disabled'
        'Box' = 'checkbox'; 'Check' = 'check'; 'ThumbArt' = 'volume_handler'
        'TrackBackground' = 'volume_frame'; 'FillInstance' = 'volume_bar'
        'Slot' = 'profile_slot'; 'Selected' = 'profile_selected'; 'TooltipFrame' = 'frame_small'
    }
    'levelhud' = [ordered]@{
        'Banner' = 'orderstart'; 'CoinField' = 'field_toptoolbar'; 'DiamondField' = 'field_toptoolbar'
        'MovesField' = 'field_toptoolbar'; 'TimeField' = 'field_toptoolbar'; 'OrderField' = 'field_toptoolbar'
        'WaterField' = 'field_toptoolbar'; 'DiamondIcon' = 'diamond_icon'; 'GoldIcon' = 'gold_icon_big'
        'Infinity' = 'infinity'; 'Frame' = 'goal_toolbar'; 'Flowers' = 'goal_toolbar_flowers'
        'Fill' = 'powerup_sphere'; 'Lock' = 'lock'; 'Slot' = 'powerup_slot'
    }
    'menu'     = [ordered]@{}
}

# Casos de contexto que não têm `SourceFile` local (herdam do componente) — o CSV não os resolve.
$extraFileBindings = [ordered]@{
    'LevelPowerUpBar.gusx' = [ordered]@{
        'slotShovel.Icon'     = 'levelhud/shovel'
        'slotWheelbarrow.Icon' = 'levelhud/wheelbarrow_big'
        'slotWhirlwind.Icon'  = 'levelhud/whirlwind_big'
    }
}

# fileBindings: arte DEPENDENTE de contexto (mesmo atlas, sprite por instância) — resolvida do CSV.
$fileBindings = [ordered]@{}
if (Test-Path $Csv) {
    foreach ($row in (Import-Csv $Csv)) {
        if ($row.Atlas -notmatch '^(dialogs|levelhud|menu)$') { continue }
        if (-not $row.Sprite) { continue }
        $leaf = $row.Leaf
        $covered = $atlasBindings[$row.Atlas].Contains($leaf) -and $atlasBindings[$row.Atlas][$leaf] -eq $row.Sprite
        if ($covered) { continue }
        if (-not $fileBindings.Contains($row.File)) { $fileBindings[$row.File] = [ordered]@{} }
        $fileBindings[$row.File][$row.Element] = "$($row.Atlas)/$($row.Sprite)"
    }
}

foreach ($file in $extraFileBindings.Keys) {
    if (-not $fileBindings.Contains($file)) { $fileBindings[$file] = [ordered]@{} }
    foreach ($k in $extraFileBindings[$file].Keys) { $fileBindings[$file][$k] = $extraFileBindings[$file][$k] }
}

$json = [ordered]@{
    '_comment'      = 'Gerado por tools/gen_gum_atlas_bindings.ps1 a partir do atlas. atlasBindings = arte livre de contexto (runtime + preview). fileBindings = arte contextual (preview). Ver AGENTS.md.'
    'atlasBindings' = $atlasBindings
    'fileBindings'  = $fileBindings
}

$json | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $Out
Write-Output ("escrito: $Out")
Write-Output ("contextuais (fileBindings): " + (($fileBindings.Keys | ForEach-Object { $fileBindings[$_].Count } | Measure-Object -Sum).Sum))
