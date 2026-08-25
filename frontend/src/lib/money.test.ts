import { describe, expect, it } from "vitest";
import { formatInr } from "./money";

describe("formatInr", () => {
  it("formats whole rupees with the Indian digit grouping", () => {
    expect(formatInr(184500)).toBe("₹1,84,500.00");
  });

  it("keeps two decimal places", () => {
    expect(formatInr(6100.5)).toBe("₹6,100.50");
  });

  it("renders zero as a real number, not a dash", () => {
    expect(formatInr(0)).toBe("₹0.00");
  });

  it("renders an unknown cost as an em dash, never as zero", () => {
    expect(formatInr(null)).toBe("—");
  });
});
