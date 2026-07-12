using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public sealed class ReturnInspections
{
    public int InspectionId { get; set; }

    public int ReturnId { get; set; }

    public int OrderId { get; set; }

    public string Status { get; set; }
        = ReturnInspectionStatuses.Completed;

    public string Decision { get; set; }
        = ReturnInspectionDecisions.ManualReview;

    public decimal OriginalOrderAmount { get; set; }

    public decimal EligibleRefundAmount { get; set; }

    public string? SummaryNote { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.Now;

    public DateTime UpdatedAt { get; set; }
        = DateTime.Now;

    public DateTime? InspectedAt { get; set; }

    public string? InspectedBy { get; set; }

    public byte[] RowVersion { get; set; }
        = Array.Empty<byte>();

    public OrderReturns? ReturnRequest { get; set; }

    public Orders? Order { get; set; }

    public ICollection<ReturnInspectionItems> Items
        { get; set; }
        = new List<ReturnInspectionItems>();
}

public sealed class ReturnInspectionItems
{
    public int InspectionItemId { get; set; }

    public int InspectionId { get; set; }

    public int OrderDetailId { get; set; }

    public int ExpectedQuantity { get; set; }

    public int ReceivedQuantity { get; set; }

    public int ApprovedQuantity { get; set; }

    public string ConditionCode { get; set; }
        = ReturnInspectionConditionCodes.Unknown;

    public string Disposition { get; set; }
        = ReturnInspectionDispositions.ManualReview;

    public decimal UnitAmountSnapshot { get; set; }

    public decimal EligibleAmount { get; set; }

    public string? Note { get; set; }

    public byte[] RowVersion { get; set; }
        = Array.Empty<byte>();

    public ReturnInspections? Inspection { get; set; }

    public OrderDetails? OrderDetail { get; set; }
}

public static class ReturnInspectionStatuses
{
    public const string Completed = "Completed";
}

public static class ReturnInspectionDecisions
{
    public const string FullRefund = "FullRefund";

    public const string Reject = "Reject";

    public const string ManualReview = "ManualReview";
}

public static class ReturnInspectionConditionCodes
{
    public const string Unknown = "Unknown";

    public const string Sealed = "Sealed";

    public const string Good = "Good";

    public const string Damaged = "Damaged";

    public const string Missing = "Missing";

    public const string WrongItem = "WrongItem";

    public static bool IsKnown(string? value) =>
        value is Sealed
            or Good
            or Damaged
            or Missing
            or WrongItem;
}

public static class ReturnInspectionDispositions
{
    public const string Restock = "Restock";

    public const string DamagedStock = "DamagedStock";

    public const string Reject = "Reject";

    public const string ManualReview = "ManualReview";

    public static bool IsKnown(string? value) =>
        value is Restock
            or DamagedStock
            or Reject
            or ManualReview;
}
