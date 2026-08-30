/**
 * Null means "no answer yet", never zero. Number("") is 0 in JavaScript, and taking that at
 * face value is what repriced a bag to nothing in M2a — here it would record a count of zero
 * and wipe the stock without the owner typing a digit.
 */
export function parseCount(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isInteger(value) && value >= 0 ? value : null;
}

/** The sentence the owner confirms, instead of confirming a bare number. */
export function recountSentence(counted: number | null, current: number): string {
  if (counted === null) return "";

  const difference = counted - current;
  const preamble = `Counted ${counted}, system says ${current} — `;

  if (difference === 0) return `${preamble}nothing to record.`;

  return difference > 0
    ? `${preamble}this records a gain of ${difference}.`
    : `${preamble}this records a loss of ${-difference}.`;
}
