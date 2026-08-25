const inr = new Intl.NumberFormat("en-IN", {
  style: "currency",
  currency: "INR",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Null means the cost is unknown — it is never zero. Rendering it as ₹0.00 would
 * turn absent data into a claim about profit, which is the whole failure mode the
 * optional-cost design exists to avoid.
 */
export function formatInr(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) return "—";
  return inr.format(amount);
}
