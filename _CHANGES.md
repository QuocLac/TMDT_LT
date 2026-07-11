# STEP 01 — FIX CART BUILD

## Branch target

`stabilize-lac12`

## Files included

- `Controllers/CartController.cs`

## Change

Removed one duplicated declaration block inside `UpdateQuantity`:

- `bool isLimitReached`
- `string msg`

No business logic, response JSON, database access, CSS, Razor View, JavaScript, migration, or package configuration was changed.

## Apply

Extract this ZIP into the project root directory containing `TMDT_LT.csproj`.
Allow the operating system to replace the existing file.

Expected destination:

`Controllers/CartController.cs`

## Validation

Run:

```bash
dotnet clean
dotnet restore
dotnet build
```

Then smoke test:

1. Open the cart.
2. Increase an item's quantity within available stock.
3. Increase it above available stock.
4. Confirm the returned quantity is capped at physical stock.
5. Confirm the stock-limit message still appears.
6. Confirm subtotal and item total are still displayed correctly.

## Rollback

Restore `Controllers/CartController.cs` from branch `Lac12` or revert the commit made after applying this ZIP.
