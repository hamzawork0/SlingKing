import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createProduct,
  getProduct,
  toPayload,
  updateProduct,
  type ProductPayload,
  type VariantPayload,
} from "../../api/catalog";
import { serverMessage } from "../../api/client";
import { CategorySelect } from "./CategorySelect";

const blankVariant: VariantPayload = {
  id: null,
  colour: "",
  size: "",
  sellingPrice: 0,
  lowStockThreshold: 0,
  isActive: true,
};

const emptyProduct: ProductPayload = {
  name: "",
  categoryId: null,
  description: "",
  variants: [blankVariant],
};

const field =
  "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
  "transition-colors placeholder:text-muted/60 focus:border-saddle";

export function ProductForm() {
  const { id } = useParams();
  const productId = id ? Number(id) : null;
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [form, setForm] = useState<ProductPayload>(emptyProduct);
  const [skus, setSkus] = useState<Record<number, string>>({});
  const [code, setCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const existing = useQuery({
    queryKey: ["product", productId],
    queryFn: () => getProduct(productId!),
    enabled: productId !== null,
  });

  useEffect(() => {
    if (!existing.data) return;

    setForm(toPayload(existing.data));
    setCode(existing.data.code);
    setSkus(Object.fromEntries(existing.data.variants.map((v) => [v.id, v.sku])));
  }, [existing.data]);

  const save = useMutation({
    mutationFn: (payload: ProductPayload) =>
      productId === null ? createProduct(payload) : updateProduct(productId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["products"] });
      navigate("/products");
    },
    onError: (err) =>
      setError(
        serverMessage(
          err,
          "That could not be saved. Check that each colour and size appears only once.",
        ),
      ),
  });

  function patchVariant(index: number, patch: Partial<VariantPayload>) {
    setForm((current) => ({
      ...current,
      variants: current.variants.map((v, i) => (i === index ? { ...v, ...patch } : v)),
    }));
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    const variants = form.variants.map((v) => ({
      ...v,
      colour: v.colour?.trim() || null,
      size: v.size?.trim() || null,
    }));

    // Number("") is 0, so a price field cleared mid-edit reads as a real zero rather than as
    // absent. Saving that would quietly reprice a bag to nothing, and the server cannot tell
    // the difference — its check is that the field is present, not that it is sensible.
    const priced = variants.filter((v) => v.isActive);

    if (priced.some((v) => !Number.isFinite(v.sellingPrice) || v.sellingPrice <= 0)) {
      setError("Every colour that is still active needs a price above zero.");
      return;
    }

    if (variants.some((v) => !Number.isInteger(v.lowStockThreshold) || v.lowStockThreshold < 0)) {
      setError("A low stock level cannot be negative.");
      return;
    }

    save.mutate({ ...form, variants });
  }

  if (productId !== null && existing.isPending) {
    return <p className="mx-auto max-w-3xl px-6 py-10 text-[13px] text-muted">Loading</p>;
  }

  if (productId !== null && existing.isError) {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <p role="alert" className="text-[13px] text-flag">
          That product could not be loaded. It may have been removed, or the server may be
          unreachable.
        </p>
        <button
          type="button"
          onClick={() => navigate("/products")}
          className="mt-4 text-[14px] text-saddle"
        >
          Back to products
        </button>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl px-6 py-10">
      <h1 className="font-display text-[28px] font-bold tracking-tight">
        {productId === null ? "New product" : "Edit product"}
      </h1>
      {code && <p className="num mt-1 text-[13px] text-muted">{code}</p>}

      <div className="topstitch mt-6" />

      <form onSubmit={handleSubmit} className="mt-6">
        <label className="text-[12px] font-medium tracking-wide" htmlFor="name">
          Name
        </label>
        <input
          id="name"
          required
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          className={field}
        />

        <div className="mt-4">
          <span className="text-[12px] font-medium tracking-wide">Category</span>
          <CategorySelect
            value={form.categoryId}
            onChange={(categoryId) => setForm({ ...form, categoryId })}
          />
        </div>

        <div className="mt-4">
          <label className="text-[12px] font-medium tracking-wide" htmlFor="description">
            Description
          </label>
          <textarea
            id="description"
            rows={3}
            value={form.description ?? ""}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            className={field}
          />
        </div>

        <div className="topstitch mt-8" />

        <h2 className="font-display mt-6 text-[16px] font-bold">Colours and sizes</h2>
        <p className="mt-1 text-[13px] text-muted">
          Leave both blank if this bag comes one way. Each combination may appear once.
        </p>

        <table className="mt-4 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">Colour</th>
              <th className="pb-2 font-medium">Size</th>
              <th className="pb-2 font-medium">Price</th>
              <th className="pb-2 font-medium">Low at</th>
              <th className="pb-2 font-medium">Active</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {form.variants.map((variant, index) => (
              <tr key={variant.id ?? `new-${index}`} className="border-b border-rule">
                <td className="py-2 pr-2">
                  <input
                    aria-label={`Colour ${index + 1}`}
                    value={variant.colour ?? ""}
                    onChange={(e) => patchVariant(index, { colour: e.target.value })}
                    className="w-full rounded-stamp border border-rule px-2 py-1.5 text-[14px]"
                  />
                  {variant.id !== null && skus[variant.id] && (
                    <span className="num text-[11px] text-muted">{skus[variant.id]}</span>
                  )}
                </td>
                <td className="py-2 pr-2">
                  <input
                    aria-label={`Size ${index + 1}`}
                    value={variant.size ?? ""}
                    onChange={(e) => patchVariant(index, { size: e.target.value })}
                    className="w-full rounded-stamp border border-rule px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="py-2 pr-2">
                  <input
                    type="number"
                    min={0}
                    step="0.01"
                    aria-label={`Price ${index + 1}`}
                    value={variant.sellingPrice}
                    onChange={(e) =>
                      patchVariant(index, { sellingPrice: Number(e.target.value) })
                    }
                    className="num w-28 rounded-stamp border border-rule px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="py-2 pr-2">
                  <input
                    type="number"
                    min={0}
                    aria-label={`Low stock threshold ${index + 1}`}
                    value={variant.lowStockThreshold}
                    onChange={(e) =>
                      patchVariant(index, { lowStockThreshold: Number(e.target.value) })
                    }
                    className="num w-20 rounded-stamp border border-rule px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="py-2">
                  <input
                    type="checkbox"
                    aria-label={`Active ${index + 1}`}
                    checked={variant.isActive}
                    onChange={(e) => patchVariant(index, { isActive: e.target.checked })}
                  />
                </td>
                <td className="py-2 text-right">
                  {variant.id === null && form.variants.length > 1 && (
                    <button
                      type="button"
                      aria-label={`Remove row ${index + 1}`}
                      onClick={() =>
                        setForm({
                          ...form,
                          variants: form.variants.filter((_, i) => i !== index),
                        })
                      }
                      className="text-[13px] text-muted transition-colors hover:text-flag"
                    >
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <button
          type="button"
          onClick={() =>
            setForm({ ...form, variants: [...form.variants, { ...blankVariant }] })
          }
          className="mt-3 text-[13px] font-medium text-saddle"
        >
          + Add a colour or size
        </button>

        {error && (
          <p role="alert" className="mt-6 text-[13px] text-flag">
            {error}
          </p>
        )}

        <div className="mt-8 flex gap-3">
          <button
            type="submit"
            disabled={save.isPending}
            className="font-display rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
          >
            {save.isPending ? "Saving" : "Save"}
          </button>
          <button
            type="button"
            onClick={() => navigate("/products")}
            className="text-[14px] text-muted"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
