import { describe, expect, it } from "vitest";
import { hasUnparseableCost, lineTotal, parseCost, parseQuantity, purchaseTotal } from "./purchaseTotals";

describe("parseQuantity", () => {
  it("reads a whole number", () => {
    expect(parseQuantity("12")).toBe(12);
  });

  it("treats an empty field as no answer, NOT as zero", () => {
    // Number("") is 0 in JavaScript. Taking that at face value repriced a bag to nothing in
    // M2a and would silently record a purchase of zero bags here.
    expect(parseQuantity("")).toBeNull();
    expect(parseQuantity("   ")).toBeNull();
  });

  it("refuses zero, negatives and fractions", () => {
    expect(parseQuantity("0")).toBeNull();
    expect(parseQuantity("-3")).toBeNull();
    expect(parseQuantity("2.5")).toBeNull();
    expect(parseQuantity("twelve")).toBeNull();
  });
});

describe("parseCost", () => {
  it("reads a decimal cost", () => {
    expect(parseCost("450.50")).toBe(450.5);
  });

  it("reads zero, which is a real cost", () => {
    // A genuinely free sample is not the same as an unknown cost.
    expect(parseCost("0")).toBe(0);
  });

  it("treats an empty field as unknown, not as zero", () => {
    expect(parseCost("")).toBeNull();
    expect(parseCost("   ")).toBeNull();
  });

  it("refuses negatives and nonsense", () => {
    expect(parseCost("-1")).toBeNull();
    expect(parseCost("four fifty")).toBeNull();
  });
});

describe("hasUnparseableCost", () => {
  // The seam: parseCost("1,200") is null for the same reason parseCost("") is null, but only
  // one of those means "unknown". This is what PurchaseForm.handleSubmit must refuse on.
  it("flags a comma-grouped cost — the exact formatting formatInr itself renders", () => {
    expect(hasUnparseableCost(["1,200"])).toBe(true);
  });

  it("flags a negative cost", () => {
    expect(hasUnparseableCost(["-500"])).toBe(true);
  });

  it("does not flag a blank box — that is the deliberate 'unknown' state", () => {
    expect(hasUnparseableCost([""])).toBe(false);
    expect(hasUnparseableCost(["   "])).toBe(false);
  });

  it("does not flag a cost that parses cleanly", () => {
    expect(hasUnparseableCost(["450.50", "0"])).toBe(false);
  });

  it("flags the whole set when any one row is bad", () => {
    expect(hasUnparseableCost(["450", "1,200", ""])).toBe(true);
  });
});

describe("lineTotal", () => {
  it("multiplies quantity by cost", () => {
    expect(lineTotal(12, 450)).toBe(5400);
  });

  it("is unknown when the cost is unknown", () => {
    expect(lineTotal(12, null)).toBeNull();
  });

  it("is unknown when the quantity is not yet entered", () => {
    expect(lineTotal(null, 450)).toBeNull();
  });
});

describe("purchaseTotal", () => {
  it("sums a fully costed purchase", () => {
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: 8, unitCost: 480 },
    ])).toBe(9240);
  });

  it("is unknown when any line's cost is unknown", () => {
    // 5400 is the sum of the line we know. Showing it would understate what is about to be
    // paid, against a bill the owner is holding in her hand.
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: 8, unitCost: null },
    ])).toBeNull();
  });

  it("is unknown for an empty purchase", () => {
    expect(purchaseTotal([])).toBeNull();
  });

  it("ignores lines with no quantity yet", () => {
    // A half-typed row must not blank the running total the owner is watching.
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: null, unitCost: null },
    ])).toBe(5400);
  });
});
