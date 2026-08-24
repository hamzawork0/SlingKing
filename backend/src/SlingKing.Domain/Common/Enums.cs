namespace SlingKing.Domain.Common;

// Persisted as smallint. Values are permanent — add new members, never renumber.

public enum InventoryTransactionType
{
    OpeningStock = 1,
    Purchase = 2,
    Sale = 3,
    Return = 4,
    Damage = 5,
    Loss = 6,
    PersonalUse = 7,
    Adjustment = 8,
    Reservation = 9,
    ReservationRelease = 10
}

public enum CostLayerSourceType
{
    OpeningStock = 1,
    Purchase = 2,
    ReturnRestock = 3,
    AdjustmentIn = 4
}

public enum InventoryReferenceType
{
    Order = 1,
    OrderItem = 2,
    Purchase = 3,
    Return = 4,
    ImportBatch = 5
}

public enum PurchaseStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

public enum OrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Completed = 6,
    Cancelled = 7,
    Returned = 8
}

public enum PaymentStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}

public enum PaymentDirection
{
    In = 1,
    Refund = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Upi = 2,
    BankTransfer = 3,
    Card = 4,
    CashOnDelivery = 5,
    Other = 6
}

public enum ReturnStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

public enum ReturnItemCondition
{
    Sellable = 1,
    Damaged = 2
}

public enum ImportBatchStatus
{
    Uploaded = 1,
    Validated = 2,
    Committed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum ImportRowStatus
{
    Valid = 1,
    Invalid = 2,
    Committed = 3
}
