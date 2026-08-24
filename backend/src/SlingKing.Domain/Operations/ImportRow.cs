using SlingKing.Domain.Common;

namespace SlingKing.Domain.Operations;

public class ImportRow
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>The original row as JSON, so a rejected upload can be explained months later.</summary>
    public string RawData { get; set; } = null!;

    public ImportRowStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public long? ResolvedVariantId { get; set; }

    public ImportBatch? Batch { get; set; }
}
