import { describe, expect, it } from "vitest";
import { toPayload } from "./catalog";
import type { ProductView } from "./catalog";

const product: ProductView = {
  id: 7,
  code: "BAG-000007",
  name: "Leather Sling",
  categoryId: 3,
  categoryName: "Slings",
  description: "Full grain",
  isActive: true,
  totalStock: 0,
  variants: [
    {
      id: 11, sku: "BAG-000007-V01", colour: "Tan", size: null,
      sellingPrice: 1850, lowStockThreshold: 2, isActive: true, physicalQty: 0,
    },
  ],
};

describe("toPayload", () => {
  it("round-trips a fetched product into an editable payload", () => {
    const payload = toPayload(product);

    expect(payload.name).toBe("Leather Sling");
    expect(payload.categoryId).toBe(3);
    expect(payload.variants).toHaveLength(1);
    expect(payload.variants[0].id).toBe(11);
    expect(payload.variants[0].sellingPrice).toBe(1850);
  });

  it("drops code and sku, which the server generates and never accepts back", () => {
    const payload = toPayload(product);

    expect(payload).not.toHaveProperty("code");
    expect(payload.variants[0]).not.toHaveProperty("sku");
  });
});
