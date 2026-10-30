param([string]$EPBasics = "$PSScriptRoot/build/dependencies/electricalprogressivebasics")
$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path $PSScriptRoot 'assets/epmulticharger'
foreach ($dir in 'blocktypes','recipes/grid','shapes/block','lang') { New-Item -ItemType Directory -Path (Join-Path $assetRoot $dir) -Force | Out-Null }
$block = Get-Content -LiteralPath "$EPBasics/assets/electricalprogressivebasics/blocktypes/charger.json" -Raw | ConvertFrom-Json -AsHashtable
$block.code = 'multicharger'
$block.class = 'BlockEPMultiCharger'
$block.entityClass = 'BEEPMultiCharger'
$block.attributes.handbook = @{ extraSections = @(@{ title = 'epmulticharger:handbook-title'; text = 'epmulticharger:handbook-text' }) }
foreach ($side in 'north','east','south','west') {
    $block.shapebytype["*-$side"].base = 'epmulticharger:block/multicharger-{state}'
    $block.lod2shapeByType["*-$side"].base = 'epmulticharger:block/multicharger-{state}'
}
$block.collisionBox = @{ x1 = 0; y1 = 0; z1 = 0; x2 = 1; y2 = 0.25; z2 = 1 }
$block.selectionBox = $block.collisionBox.Clone()
$block | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath "$assetRoot/blocktypes/multicharger.json"

@{
    ingredientPattern = 'PCP,IGI'; width = 3; height = 2
    ingredients = @{
        C = @{ type = 'block'; code = 'electricalprogressivebasics:charger-disabled-south' }
        P = @{ type = 'item'; code = 'game:metalplate-iron' }
        I = @{ type = 'item'; code = 'electricalprogressivebasics:coil' }
        G = @{ type = 'block'; code = 'game:glass-plain' }
    }
    output = @{ type = 'block'; code = 'epmulticharger:multicharger-disabled-south' }
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$assetRoot/recipes/grid/multicharger.json"

function Cube($name, $from, $to, $texture) {
    $faces = @{}
    foreach ($face in 'north','east','south','west','up','down') { $faces[$face] = @{ texture = "#$texture"; uv = @(0,0,16,16) } }
    return @{ name = $name; from = $from; to = $to; faces = $faces }
}
foreach ($state in 'disabled','enabled','burned') {
    $elements = @( (Cube 'base' @(0,0,0) @(16,2,16) 'iron') )
    $elements += (Cube 'divider-x' @(7.7,2,0) @(8.3,4,16) 'copper')
    $elements += (Cube 'divider-z' @(0,2,7.7) @(16,4,8.3) 'copper')
    for ($i = 0; $i -lt 4; $i++) {
        $x = ($i % 2) * 8
        $z = [Math]::Floor($i / 2) * 8
        $elements += (Cube "bay-$i" @((1+$x),2,(1+$z)) @((7+$x),3,(7+$z)) 'plate')
        $elements += (Cube "indicator-$i" @((1+$x),3,(1+$z)) @((2+$x),3.4,(2+$z)) 'indicator')
    }
    $indicator = if ($state -eq 'enabled') { 'game:block/metal/plate/gold' } elseif ($state -eq 'burned') { 'game:block/metal/plate/lead' } else { 'game:block/metal/plate/copper' }
    @{
        textureWidth = 16; textureHeight = 16
        textures = @{ iron = 'game:block/metal/plate/iron'; copper = 'game:block/metal/plate/copper'; plate = 'game:block/metal/plate/steel'; indicator = $indicator }
        elements = $elements
    } | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath "$assetRoot/shapes/block/multicharger-$state.json"
}
@{
    'block-multicharger-disabled-*' = 'Four Bay Charger'
    'block-multicharger-enabled-*' = 'Four Bay Charger'
    'block-multicharger-burned-*' = 'Burned Four Bay Charger'
    'interact' = 'Insert/remove item in targeted bay'
    'handbook-title' = 'Four-item charging queue'
    'handbook-text' = 'Right-click one of the four bays to insert or retrieve an item. Holds four items; charges the first unfinished bay at standard charger speed and power (up to 200 W). Completed items remain until retrieved. Connect power underneath. Standard EP voltage and burnout rules still apply.'
} | ConvertTo-Json | Set-Content -LiteralPath "$assetRoot/lang/en.json"
