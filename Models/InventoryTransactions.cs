using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class InventoryTransactions
{
    public int TransactionId { get; set; }

    public int VariantId { get; set; }

    public string TransactionType { get; set; } = null!;

    public int Quantity { get; set; }

    public int? ReferenceId { get; set; }

    public DateTime? TransactionDate { get; set; }

    public int? AccountId { get; set; }

    public string? Note { get; set; }

    public virtual Account? Account { get; set; }

    public virtual ProductVariants Variant { get; set; } = null!;
}
