import { describe, expect, it } from "vitest";
import { parseCount, recountSentence } from "./recount";

describe("parseCount", () => {
  it("reads a whole number", () => {
    expect(parseCount("9")).toBe(9);
  });

  it("reads zero, which is a real count", () => {
    expect(parseCount("0")).toBe(0);
  });

  it("treats an empty field as no answer, NOT as zero", () => {
    // Number("") is 0 in JavaScript. Taking that at face value is what repriced a bag to
    // nothing in M2a; here it would silently record a count of zero and wipe the stock.
    expect(parseCount("")).toBeNull();
    expect(parseCount("   ")).toBeNull();
  });

  it("refuses anything that is not a whole non-negative number", () => {
    expect(parseCount("-1")).toBeNull();
    expect(parseCount("2.5")).toBeNull();
    expect(parseCount("nine")).toBeNull();
  });
});

describe("recountSentence", () => {
  it("says nothing at all until a number is entered", () => {
    expect(recountSentence(null, 12)).toBe("");
  });

  it("names the loss when fewer were counted", () => {
    expect(recountSentence(9, 12)).toBe(
      "Counted 9, system says 12 — this records a loss of 3.",
    );
  });

  it("names the gain when more were counted", () => {
    expect(recountSentence(15, 12)).toBe(
      "Counted 15, system says 12 — this records a gain of 3.",
    );
  });

  it("says plainly that nothing happens when the count matches", () => {
    expect(recountSentence(12, 12)).toBe("Counted 12, system says 12 — nothing to record.");
  });
});
