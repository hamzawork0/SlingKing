using System.Linq.Expressions;
using SlingKing.Domain.Catalog;

namespace SlingKing.Application.Catalog;

/// <summary>
/// Rules about variants that more than one module needs. Shared rather than copied: two
/// definitions of "which variants can be used" would drift, and the first symptom would be
/// one screen offering something another screen refuses.
/// </summary>
public static class VariantRules
{
    /// <summary>
    /// A variant may be used only while it AND its parent product are active. Deactivating a
    /// product deliberately leaves its variant rows untouched, so checking the variant alone
    /// lets a retired product's colours keep appearing.
    /// </summary>
    /// <remarks>
    /// An <see cref="Expression"/> rather than a <c>Func</c> so it composes into
    /// <c>.Where()</c> and translates to SQL. Invoking it as a delegate inside another lambda
    /// silently evaluates client-side.
    /// </remarks>
    public static readonly Expression<Func<ProductVariant, bool>> IsReachable =
        v => v.IsActive && v.Product!.IsActive;
}
