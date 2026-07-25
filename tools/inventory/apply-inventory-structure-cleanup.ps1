#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectFile = Join-Path $projectRoot "TMDT_LT.csproj"

if (-not (Test-Path $projectFile)) {
    throw "Không tìm thấy TMDT_LT.csproj. Hãy chạy script từ đúng project đã giải nén ZIP."
}

Write-Host "Inventory structural cleanup" -ForegroundColor Cyan
Write-Host "Project root: $projectRoot"

$programFile = Join-Path $projectRoot "Program.cs"
if (-not (Test-Path $programFile)) {
    throw "Thiếu Program.cs."
}

$programContent = Get-Content $programFile -Raw
$programChanged = $false

if ($programContent -notmatch "using TMDT_LT\.Services\.Inventory;") {
    $usingMarker = "using TMDT_LT.Services;"
    if (-not $programContent.Contains($usingMarker)) {
        throw "Không tìm thấy using TMDT_LT.Services; trong Program.cs."
    }

    $programContent = $programContent.Replace(
        $usingMarker,
        "$usingMarker`r`nusing TMDT_LT.Services.Inventory;")
    $programChanged = $true
}

if ($programContent -notmatch "builder\.Services\.AddInventoryModule\(\);") {
    $serviceMarker =
        "builder\.Services\.AddScoped<\s*IOrderInventoryService,\s*OrderInventoryService>\(\);"

    if ($programContent -notmatch $serviceMarker) {
        throw "Không tìm thấy vị trí đăng ký IOrderInventoryService trong Program.cs."
    }

    $serviceRegistration = @"
builder.Services.AddScoped<
    IOrderInventoryService,
    OrderInventoryService>();

builder.Services.AddInventoryModule();
"@

    $registrationRegex = [regex]::new($serviceMarker)
    $programContent = $registrationRegex.Replace(
        $programContent,
        $serviceRegistration,
        1)
    $programChanged = $true
}

if ($programChanged) {
    Set-Content -Path $programFile -Value $programContent -Encoding UTF8
    Write-Host "Registered the inventory module in Program.cs." -ForegroundColor Green
}
else {
    Write-Host "Inventory module registration is already applied." -ForegroundColor DarkGreen
}

$authController = Join-Path $projectRoot "Controllers\AuthController.cs"
if (-not (Test-Path $authController)) {
    throw "Thiếu Controllers\AuthController.cs."
}

$authContent = Get-Content $authController -Raw
$rawRoleClaim = 'new Claim(ClaimTypes.Role, account.Role ?? "Customer"),'
$canonicalRoleExpression =
    'new Claim(ClaimTypes.Role, string.Equals(account.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Customer"),'
$rawAdminRedirect = 'return account.Role == "Admin"'
$canonicalAdminRedirect =
    'return string.Equals(account.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)'

$authChanged = $false
if ($authContent.Contains($rawRoleClaim)) {
    $authContent = $authContent.Replace($rawRoleClaim, $canonicalRoleExpression)
    $authChanged = $true
}

if ($authContent.Contains($rawAdminRedirect)) {
    $authContent = $authContent.Replace($rawAdminRedirect, $canonicalAdminRedirect)
    $authChanged = $true
}

if ($authChanged) {
    Set-Content -Path $authController -Value $authContent -Encoding UTF8
    Write-Host "Canonicalized Admin role at login." -ForegroundColor Green
}
elseif ($authContent.Contains($canonicalRoleExpression) -and $authContent.Contains($canonicalAdminRedirect)) {
    Write-Host "Admin role normalization is already applied." -ForegroundColor DarkGreen
}
else {
    throw "Không tìm thấy cấu trúc role claim dự kiến trong AuthController.cs. Dừng để tránh sửa sai authentication."
}

$commercePersistence = Join-Path $projectRoot "Data\ApplicationDbContext.CommercePersistence.cs"
if (-not (Test-Path $commercePersistence)) {
    throw "Thiếu Data\ApplicationDbContext.CommercePersistence.cs."
}

$content = Get-Content $commercePersistence -Raw
$oldCall = "ConfigureInventoryPhaseB(modelBuilder);"
$newCall = "ConfigureInventory(modelBuilder);"

if ($content.Contains($oldCall)) {
    $content = $content.Replace($oldCall, $newCall)
    Set-Content -Path $commercePersistence -Value $content -Encoding UTF8
    Write-Host "Updated EF inventory configuration hook." -ForegroundColor Green
}
elseif ($content.Contains($newCall)) {
    Write-Host "EF inventory configuration hook is already clean." -ForegroundColor DarkGreen
}
else {
    throw "Không tìm thấy hook cấu hình inventory cũ hoặc mới. Dừng để tránh sửa sai DbContext."
}

$obsoleteFiles = @(
    "Areas\Admin\Controllers\InventoryBusinessController.cs",
    "Areas\Admin\Controllers\InventoryWarehousePhaseBController.cs",
    "Areas\Admin\Controllers\InventoryFinalizationController.cs",
    "Areas\Admin\Controllers\InventoryControlPhaseDController.cs",
    "Areas\Admin\Controllers\InventoryReceivingPhaseDController.cs",
    "Areas\Admin\Controllers\InventoryOperationsPhaseDController.cs",
    "Areas\Admin\Controllers\InventoryLegacyMutationGuardController.cs",

    "Services\InventoryFinalAuditService.cs",
    "TagHelpers\InventoryBusinessFixBodyTagHelper.cs",

    "Data\ApplicationDbContext.InventoryPhaseB.cs",

    "Models\InventoryCountLines.cs",
    "Models\InventoryCountSessions.cs",
    "Models\InventoryTransactions.InventoryControlPhaseD2.cs",
    "Models\InventoryFinancialExtensions.cs",

    "Areas\Admin\Models\Inventory\InventoryCountRequests.cs",
    "Areas\Admin\Models\Inventory\InventoryReceivingRequests.cs",
    "Areas\Admin\Models\Inventory\InventoryOperationRequests.cs",
    "Areas\Admin\Models\Inventory\InventoryDistributionRequests.cs",

    "Areas\Admin\Views\Inventory\Index.cshtml",
    "Areas\Admin\Views\Inventory\CreatePO.cshtml",
    "Areas\Admin\Views\Inventory\CreateSO.cshtml",

    "wwwroot\js\admin\inventory-business-fix.js",
    "wwwroot\js\admin\inventory-control-phase-d2.js",
    "wwwroot\js\admin\inventory-receiving-phase-d.js",
    "wwwroot\js\admin\inventory-operations-phase-d3.js",
    "wwwroot\js\admin\inventory-distribution-phase-d3.js",

    "wwwroot\css\admin\inventory-control-phase-d2.css",
    "wwwroot\css\admin\inventory-receiving-phase-d.css",
    "wwwroot\css\admin\inventory-operations-phase-d3.css",
    "wwwroot\css\admin\inventory-distribution-phase-d3.css",

    "docs\INVENTORY_PHASE_D1_APPLY.md",
    "docs\INVENTORY_PHASE_D2_APPLY.md",
    "docs\INVENTORY_PHASE_D3_FINAL_APPLY.md",
    "docs\INVENTORY_D2_SHADOW_RELATION_CLEANUP.md",

    "PHASE_D1_FILE_MANIFEST.txt",
    "PHASE_D2_FILE_MANIFEST.txt",
    "PHASE_D3_FINAL_FILE_MANIFEST.txt",

    "Controllers\AuthorizationController.cs",
    "Views\Home\AccessDenied.cshtml",
    "INVENTORY_ACCESS_DENIED_HOTFIX_README.txt"
)

$removed = 0
foreach ($relativePath in $obsoleteFiles) {
    $path = Join-Path $projectRoot $relativePath
    if (Test-Path $path) {
        Remove-Item $path -Force
        $removed++
        Write-Host "Removed: $relativePath" -ForegroundColor DarkYellow
    }
}

$obsoleteDirectories = @(
    "Areas\Admin\Models\Inventory"
)

foreach ($relativePath in $obsoleteDirectories) {
    $path = Join-Path $projectRoot $relativePath
    if (Test-Path $path) {
        $remainingFiles = Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue
        if ($remainingFiles.Count -eq 0) {
            Remove-Item $path -Recurse -Force
        }
    }
}

$requiredFiles = @(
    "AGENTS.md",
    "Controllers\AccessController.cs",
    "Views\Access\Denied.cshtml",

    "Areas\Admin\Controllers\InventoryController.cs",
    "Areas\Admin\Controllers\InventoryReceivingController.cs",
    "Areas\Admin\Controllers\InventoryCountingController.cs",
    "Areas\Admin\Controllers\InventoryOperationsController.cs",
    "Areas\Admin\Controllers\InventoryTransferController.cs",
    "Areas\Admin\Controllers\InventoryDistributionController.cs",
    "Areas\Admin\Controllers\InventoryReconciliationController.cs",

    "Areas\Admin\Views\Inventory\Dashboard.cshtml",
    "Areas\Admin\Views\Inventory\Receiving.cshtml",
    "Areas\Admin\Views\Inventory\Operations.cshtml",
    "Areas\Admin\Views\Inventory\Distribution.cshtml",

    "Services\Inventory\InventoryServiceCollectionExtensions.cs",
    "Services\Inventory\InventoryDistributionService.cs",
    "Services\Inventory\Contracts\InventoryReceivingRequests.cs",
    "Services\Inventory\Contracts\InventoryCountRequests.cs",
    "Services\Inventory\Contracts\InventoryOperationRequests.cs",
    "Services\Inventory\Contracts\InventoryDistributionRequests.cs",

    "Models\Inventory\InventoryCountRules.cs",
    "Models\Inventory\InventoryCountSession.cs",
    "Models\Inventory\InventoryCountLine.cs",
    "Models\Inventory\InventoryEntityExtensions.cs",
    "Models\Inventory\InventoryTransactionAuditFields.cs",
    "Models\InventoryLots.cs",

    "Data\ApplicationDbContext.Inventory.cs",

    "wwwroot\js\admin\inventory-dashboard.js",
    "wwwroot\js\admin\inventory-receiving.js",
    "wwwroot\js\admin\inventory-operations.js",
    "wwwroot\js\admin\inventory-distribution.js",
    "wwwroot\css\admin\inventory-dashboard.css",
    "wwwroot\css\admin\inventory-receiving.css",
    "wwwroot\css\admin\inventory-operations.css",
    "wwwroot\css\admin\inventory-distribution.css",

    "docs\modules\inventory\INVENTORY_MODULE_CONTEXT.md"
)

$missing = @()
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path (Join-Path $projectRoot $relativePath))) {
        $missing += $relativePath
    }
}

if ($missing.Count -gt 0) {
    throw "Thiếu file sau khi cleanup:`n - $($missing -join "`n - ")"
}

$compiledSourceRoots = @(
    (Join-Path $projectRoot "Areas\Admin\Controllers"),
    (Join-Path $projectRoot "Services"),
    (Join-Path $projectRoot "Data"),
    (Join-Path $projectRoot "Models")
)

$forbiddenPattern = "(PhaseB|PhaseC|PhaseD|D1Controller|D2Controller|D3Controller|LegacyBusinessFix|InventoryLegacyMutationGuard)"
$violations = @()

foreach ($root in $compiledSourceRoots) {
    if (-not (Test-Path $root)) { continue }

    $matches = Get-ChildItem $root -Recurse -File -Include *.cs |
        Where-Object { $_.FullName -notmatch "\\Migrations\\" } |
        Select-String -Pattern $forbiddenPattern

    foreach ($match in $matches) {
        $violations += "$($match.Path):$($match.LineNumber): $($match.Line.Trim())"
    }
}

if ($violations.Count -gt 0) {
    Write-Warning "Vẫn còn tên triển khai theo giai đoạn trong source đang compile:"
    $violations | ForEach-Object { Write-Warning $_ }
}
else {
    Write-Host "No phase-based production naming remains in compiled inventory source." -ForegroundColor Green
}

Write-Host ""
Write-Host "Removed obsolete files: $removed" -ForegroundColor Cyan
Write-Host "Database migrations were not edited or deleted." -ForegroundColor Cyan
Write-Host "Next: delete bin/obj, Clean Solution, Rebuild Solution." -ForegroundColor Cyan
