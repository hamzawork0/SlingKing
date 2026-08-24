using SlingKing.Domain.Common;

namespace SlingKing.Domain.Operations;

public class ImportBatch
{
    public long Id { get; set; }
    public string FileName { get; set; } = null!;
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Uploaded;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public long? UploadedBy { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
