# Inventory D2 — Shadow relation cleanup

## Lỗi đã phát hiện

Migration `20260718085504_InventoryCountJournalPhaseD2` hiện có các thao tác dư:

```text
AddColumn InventoryTransactions.InventoryLotsLotId
CreateIndex IX_InventoryTransactions_InventoryLotsLotId
AddForeignKey FK_InventoryTransactions_InventoryLots_InventoryLotsLotId
```

Trong khi quan hệ nghiệp vụ thật đã tồn tại từ Phase B:

```text
InventoryTransactions.LotId -> InventoryLots.LotId
```

## Nguyên nhân

`InventoryLots` có navigation collection `InventoryTransactions`, nhưng cấu hình Phase B dùng:

```csharp
entity.HasOne(item => item.Lot)
    .WithMany()
    .HasForeignKey(item => item.LotId);
```

EF Core coi collection mới là một quan hệ thứ hai và sinh shadow FK `InventoryLotsLotId`.

## Sửa trong source

Bản bàn giao đã xóa collection navigation dư khỏi `Models/InventoryLots.cs`. Quan hệ Phase B bằng `LotId` vẫn được giữ nguyên.

## Migration thủ công

Chạy:

```powershell
Add-Migration InventoryCountJournalShadowRelationCleanup
```

Migration được sinh phải có nội dung tương đương:

```text
DropForeignKey FK_InventoryTransactions_InventoryLots_InventoryLotsLotId
DropIndex IX_InventoryTransactions_InventoryLotsLotId
DropColumn InventoryLotsLotId
```

Không được drop các cột thật:

```text
InventoryTransactions.LotId
InventoryTransactions.WarehouseId
InventoryTransactions.UnitCostSnapshot
InventoryTransactions.TotalCostSnapshot
```

Sau khi kiểm tra:

```powershell
Update-Database
```
