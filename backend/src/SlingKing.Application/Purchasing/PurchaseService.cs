using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Catalog;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Application.Inventory;
using SlingKing.Domain.Common;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Application.Purchasing;

public sealed class PurchaseService : IPurchaseService
{
    private readonly IAppDbContext _db;
    private readonly IStockService _stock;
    private readonly INumberSequenceAllocator _sequences;
    private readonly IClock _clock;

    public PurchaseService(
        IAppDbContext db,
        IStockService stock,
        INumberSequenceAllocator sequences,
        IClock clock)
    {
        _db = db;
        _stock = stock;
        _sequences = sequences;
        _clock = clock;
    }

    /// <summary>
    /// Resolves every line's variant and returns a label per id for readable refusals. Throws
    /// 404 if any line names a variant that does not exist or whose product is retired.
    /// </summary>
    private async Task<Dictionary<long, string>> ResolveVariantLabelsAsync(
        IReadOnlyList<PurchaseLineInput> lines, CancellationToken cancellationToken)
    {
        var ids = lines.Select(l => l.VariantId).Distinct().ToList();

        var found = await _db.ProductVariants
            .AsNoTracking()
            .Where(VariantRules.IsReachable)
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Colour, v.Size, v.Sku })
            .ToListAsync(cancellationToken);

        if (found.Count != ids.Count)
        {
            throw AppException.NotFound("That item no longer exists.");
        }

        return found.ToDictionary(
            v => v.Id,
            v => string.Join(" · ", new[] { v.Colour, v.Size }.Where(s => s is not null))
                is { Length: > 0 } label ? label : v.Sku);
    }

    public async Task<PurchaseView> RecordAsync(
        PurchaseInput input, CancellationToken cancellationToken = default)
    {
        if (input.Lines.Count == 0)
        {
            throw AppException.Invalid("Add at least one bag to this purchase.");
        }

        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
            {
                throw AppException.Invalid("Enter how many, as a number above zero.");
            }

            if (line.UnitCost < 0)
            {
                throw AppException.Invalid("A cost cannot be negative.");
            }
        }

        var supplierExists = await _db.Suppliers
            .AnyAsync(s => s.Id == input.SupplierId && s.IsActive, cancellationToken);

        if (!supplierExists)
        {
            throw AppException.NotFound("That supplier no longer exists.");
        }

        var labels = await ResolveVariantLabelsAsync(input.Lines, cancellationToken);
        PurchaseTotals.RejectDuplicateVariants(input.Lines, labels);

        var now = _clock.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Inside the transaction on purpose: the allocator's UPDATE rolls back with everything
        // else, so a failed purchase returns its number to the sequence.
        var number = await _sequences.NextAsync("purchase", cancellationToken);

        var purchase = new Purchase
        {
            Number = number,
            SupplierId = input.SupplierId,
            // The owner's record of when she bought. It may be back-dated; the cost layers are
            // still stamped now, so a back-dated purchase never reorders the FIFO queue and
            // never rewrites a cost that has already been recorded.
            PurchaseDate = input.PurchaseDate,
            Status = PurchaseStatus.Completed,
            TotalAmount = PurchaseTotals.Total(input.Lines),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedAt = now,
            CompletedAt = now,
            UpdatedAt = now
        };

        _db.Purchases.Add(purchase);

        var items = input.Lines
            .Select(l => new PurchaseItem
            {
                Purchase = purchase,
                VariantId = l.VariantId,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                LineTotal = PurchaseTotals.LineTotal(l.Quantity, l.UnitCost)
            })
            .ToList();

        _db.PurchaseItems.AddRange(items);

        // The lines need ids before their layers can point at them.
        await _db.SaveChangesAsync(cancellationToken);

        await _stock.ApplyPurchaseAsync(
            purchase.Id,
            items.Select(i => new PurchaseStockLine(
                i.Id, i.VariantId, i.Quantity, i.UnitCost)).ToList(),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(purchase.Id, cancellationToken);
    }

    public async Task<PurchaseView> CancelAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var purchase = await _db.Purchases
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw AppException.NotFound("That purchase does not exist.");

        if (purchase.Status == PurchaseStatus.Cancelled)
        {
            throw AppException.Conflict("That purchase is already cancelled.");
        }

        var items = await _db.PurchaseItems
            .Where(i => i.PurchaseId == id)
            .Select(i => new PurchaseStockLine(i.Id, i.VariantId, i.Quantity, i.UnitCost))
            .ToListAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _stock.ReversePurchaseAsync(id, items, cancellationToken);

        purchase.Status = PurchaseStatus.Cancelled;
        purchase.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(id, cancellationToken);
    }

    private async Task<PurchaseView> ViewAsync(long id, CancellationToken cancellationToken)
    {
        var header = await _db.Purchases
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Number, p.SupplierId, SupplierName = p.Supplier!.Name,
                p.PurchaseDate, p.Status, p.TotalAmount, p.Notes
            })
            .SingleAsync(cancellationToken);

        // ponytail: EF8 will not translate an OrderBy applied on top of a Join projected into
        // a record constructor (same limitation noted in StockService.ListAsync). Materialise
        // first and finish the ordering in memory — a purchase has a handful of lines.
        var lines = (await _db.PurchaseItems
            .AsNoTracking()
            .Where(i => i.PurchaseId == id)
            .Join(_db.ProductVariants, i => i.VariantId, v => v.Id, (i, v) => new PurchaseLineView(
                i.Id, v.Id, v.Sku, v.Product!.Name, v.Colour, v.Size,
                i.Quantity, i.UnitCost, i.LineTotal))
            .ToListAsync(cancellationToken))
            .OrderBy(l => l.Sku)
            .ToList();

        return new PurchaseView(
            header.Id, header.Number, header.SupplierId, header.SupplierName,
            header.PurchaseDate, header.Status, header.TotalAmount, header.Notes, lines);
    }
}
