#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectFile = Join-Path $projectRoot 'TMDT_LT.csproj'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Khong tim thay TMDT_LT.csproj. Hay chay script tu goi da giai nen vao dung project."
}

$obsoleteFiles = @(
    'Areas\Admin\Views\Inventory\Distribution.cshtml',
    'wwwroot\css\admin\inventory-dashboard.css',
    'wwwroot\css\admin\inventory-receiving.css',
    'wwwroot\css\admin\inventory-operations.css',
    'wwwroot\css\admin\inventory-distribution.css',
    'wwwroot\js\admin\inventory-distribution.js'
)

$requiredFiles = @(
    'Areas\Admin\Views\Inventory\Dashboard.cshtml',
    'Areas\Admin\Views\Inventory\Receiving.cshtml',
    'Areas\Admin\Views\Inventory\Operations.cshtml',
    'Areas\Admin\Views\Inventory\_InventoryNavigation.cshtml',
    'wwwroot\css\admin\inventory.css',
    'wwwroot\js\admin\inventory-dashboard.js',
    'wwwroot\js\admin\inventory-receiving.js',
    'wwwroot\js\admin\inventory-operations.js'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Thieu file moi bat buoc: $relativePath"
    }
}

$removed = @()
foreach ($relativePath in $obsoleteFiles) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Force
        $removed += $relativePath
    }
}

Write-Host ''
Write-Host 'Da hoan tat don dep giao dien Inventory.' -ForegroundColor Green
if ($removed.Count -gt 0) {
    Write-Host 'Da xoa cac file cu:' -ForegroundColor Cyan
    foreach ($item in $removed) {
        Write-Host " - $item"
    }
}
else {
    Write-Host 'Khong con file cu nao can xoa.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host 'Tiep theo: xoa bin/obj, Clean Solution va Rebuild Solution.' -ForegroundColor Yellow
Write-Host 'Khong can tao migration cho thay doi nay.' -ForegroundColor Yellow
