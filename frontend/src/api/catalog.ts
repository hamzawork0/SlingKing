import { api } from "./client";

export interface CategoryOption {
  id: number;
  name: string;
}

export interface VariantView {
  id: number;
  sku: string;
  colour: string | null;
  size: string | null;
  sellingPrice: number;
  lowStockThreshold: number;
  isActive: boolean;
  physicalQty: number;
}

export interface ProductView {
  id: number;
  code: string;
  name: string;
  categoryId: number | null;
  categoryName: string | null;
  description: string | null;
  isActive: boolean;
  totalStock: number;
  variants: VariantView[];
}

/** `id: null` means "create this one". Absence never means delete — omit nothing. */
export interface VariantPayload {
  id: number | null;
  colour: string | null;
  size: string | null;
  sellingPrice: number;
  lowStockThreshold: number;
  isActive: boolean;
}

export interface ProductPayload {
  name: string;
  categoryId: number | null;
  description: string | null;
  variants: VariantPayload[];
}

/**
 * Code and SKU are generated once by the server and are immutable. The server ignores them
 * on update rather than erroring, but there is no reason to send them back.
 */
export function toPayload(product: ProductView): ProductPayload {
  return {
    name: product.name,
    categoryId: product.categoryId,
    description: product.description,
    variants: product.variants.map((v) => ({
      id: v.id,
      colour: v.colour,
      size: v.size,
      sellingPrice: v.sellingPrice,
      lowStockThreshold: v.lowStockThreshold,
      isActive: v.isActive,
    })),
  };
}

export async function listCategories(): Promise<CategoryOption[]> {
  const { data } = await api.get<CategoryOption[]>("/api/v1/categories");
  return data;
}

export async function createCategory(name: string): Promise<CategoryOption> {
  const { data } = await api.post<CategoryOption>("/api/v1/categories", { name });
  return data;
}

export async function renameCategory(id: number, name: string): Promise<CategoryOption> {
  const { data } = await api.put<CategoryOption>(`/api/v1/categories/${id}`, { name });
  return data;
}

export async function listProducts(
  search: string,
  includeInactive: boolean,
): Promise<ProductView[]> {
  const { data } = await api.get<ProductView[]>("/api/v1/products", {
    params: { search: search || undefined, includeInactive: includeInactive || undefined },
  });
  return data;
}

export async function getProduct(id: number): Promise<ProductView> {
  const { data } = await api.get<ProductView>(`/api/v1/products/${id}`);
  return data;
}

export async function createProduct(payload: ProductPayload): Promise<ProductView> {
  const { data } = await api.post<ProductView>("/api/v1/products", payload);
  return data;
}

export async function updateProduct(
  id: number,
  payload: ProductPayload,
): Promise<ProductView> {
  const { data } = await api.put<ProductView>(`/api/v1/products/${id}`, payload);
  return data;
}

export async function deactivateProduct(id: number): Promise<void> {
  await api.post(`/api/v1/products/${id}/deactivate`);
}
