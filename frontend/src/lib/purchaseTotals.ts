/** One line as the form holds it while being typed: both fields may still be empty. */
export interface DraftLine {
  quantity: number | null;
  unitCost: number | null;
}

/**
 * Null means "not entered yet", never zero. Number("") is 0 in JavaScript, and taking that at
 * face value would record a purchase of nothing without the owner typing a digit.
 */
export function parseQuantity(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isInteger(value) && value > 0 ? value : null;
}

/**
 * Null means the cost is unknown. Zero is a real answer — a free sample is not the same as a
 * cost nobody recorded, which is the distinction the whole optional-cost design rests on.
 */
export function parseCost(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isFinite(value) && value >= 0 ? value : null;
}

export function lineTotal(quantity: number | null, unitCost: number | null): number | null {
  if (quantity === null || unitCost === null) return null;
  return quantity * unitCost;
}

/**
 * Null when any line that has a quantity lacks a cost, and null when nothing is entered.
 * Never a partial sum: a total that omits a line understates what is about to be paid, against
 * a bill the owner is checking it against.
 *
 * Lines with no quantity yet are skipped rather than counted as unknown — a half-typed row
 * must not blank the number she is watching.
 */
export function purchaseTotal(lines: DraftLine[]): number | null {
  const entered = lines.filter((l) => l.quantity !== null);

  if (entered.length === 0 || entered.some((l) => l.unitCost === null)) return null;

  return entered.reduce((sum, l) => sum + l.quantity! * l.unitCost!, 0);
}
