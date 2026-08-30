import { api } from "./client";

/**
 * The four types this API accepts. Adjustment is missing on purpose — the recount endpoint
 * is its only producer, so offering it here would offer a second way to change a quantity
 * where the caller does the arithmetic from a number the screen loaded a minute ago.
 */
export const movementTypes = ["OpeningStock", "Damage", "Loss", "PersonalUse"] as const;

export type MovementType = (typeof movementTypes)[number];

export const movementLabels: Record<string, string> = {
  OpeningStock: "Opening stock",
  Damage: "Damaged",
  Loss: "Lost",
  PersonalUse: "Personal use",
  Adjustment: "Recount",
};

/** Outflows must say what happened; the server refuses them otherwise. */
export const outflowTypes: MovementType[] = ["Damage", "Loss", "PersonalUse"];

export interface StockRow {
  variantId: number;
  sku: string;
  productName: string;
  colour: string | null;
  size: string | null;
  physicalQty: number;
  lowStockThreshold: number;
}

export interface MovementView {
  id: number;
  occurredAt: string;
  type: string;
  physicalDelta: number;
  totalCost: number | null;
  reason: string | null;
}

export interface StockItemView extends StockRow {
  stockValue: number | null;
  movements: MovementView[];
}

export interface RecountResult {
  previous: number;
  counted: number;
  difference: number;
  movement: MovementView | null;
}

export interface MovementPayload {
  variantId: number;
  type: MovementType;
  quantity: number;
  unitCost: number | null;
  reason: string | null;
}

export async function listStock(search: string, lowOnly: boolean): Promise<StockRow[]> {
  const { data } = await api.get<StockRow[]>("/api/v1/inventory", {
    params: { search: search || undefined, lowOnly: lowOnly || undefined },
  });
  return data;
}

export async function getStockItem(variantId: number): Promise<StockItemView> {
  const { data } = await api.get<StockItemView>(`/api/v1/inventory/${variantId}`);
  return data;
}

export async function recordMovement(payload: MovementPayload): Promise<MovementView> {
  const { data } = await api.post<MovementView>("/api/v1/inventory/movements", payload);
  return data;
}

export async function recount(
  variantId: number,
  countedQuantity: number,
): Promise<RecountResult> {
  const { data } = await api.post<RecountResult>(
    `/api/v1/inventory/${variantId}/recount`,
    { countedQuantity },
  );
  return data;
}
