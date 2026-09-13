import { api } from "./client";

export interface SupplierOption {
  id: number;
  name: string;
  phone: string | null;
}

export interface PurchaseLineView {
  id: number;
  variantId: number;
  sku: string;
  productName: string;
  colour: string | null;
  size: string | null;
  quantity: number;
  unitCost: number | null;
  lineTotal: number | null;
}

export interface PurchaseView {
  id: number;
  number: string;
  supplierId: number;
  supplierName: string;
  purchaseDate: string;
  status: "Completed" | "Cancelled" | "Draft";
  totalAmount: number | null;
  notes: string | null;
  lines: PurchaseLineView[];
}

export interface PurchasePayload {
  supplierId: number;
  purchaseDate: string;
  notes: string | null;
  lines: { variantId: number; quantity: number; unitCost: number | null }[];
}

export async function listSuppliers(): Promise<SupplierOption[]> {
  const { data } = await api.get<SupplierOption[]>("/api/v1/suppliers");
  return data;
}

export async function createSupplier(
  name: string,
  phone: string | null,
): Promise<SupplierOption> {
  const { data } = await api.post<SupplierOption>("/api/v1/suppliers", { name, phone });
  return data;
}

export async function updateSupplier(
  id: number,
  name: string,
  phone: string | null,
): Promise<SupplierOption> {
  const { data } = await api.put<SupplierOption>(`/api/v1/suppliers/${id}`, { name, phone });
  return data;
}

export async function listPurchases(
  search: string,
  includeCancelled: boolean,
): Promise<PurchaseView[]> {
  const { data } = await api.get<PurchaseView[]>("/api/v1/purchases", {
    params: {
      search: search || undefined,
      includeCancelled: includeCancelled || undefined,
    },
  });
  return data;
}

export async function recordPurchase(payload: PurchasePayload): Promise<PurchaseView> {
  const { data } = await api.post<PurchaseView>("/api/v1/purchases", payload);
  return data;
}

export async function cancelPurchase(id: number): Promise<PurchaseView> {
  const { data } = await api.post<PurchaseView>(`/api/v1/purchases/${id}/cancel`);
  return data;
}
