import { Fragment, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { deactivateProduct, listProducts, type ProductView } from "../../api/catalog";
import { formatInr } from "../../lib/money";

function VariantRow({ variant }: { variant: ProductView["variants"][number] }) {
  return (
    <tr className={variant.isActive ? "" : "opacity-50"}>
      <td className="py-2 pl-10 text-[13px] text-muted">
        {[variant.colour, variant.size].filter(Boolean).join(" · ") || "No variation"}
        {!variant.isActive && " (retired)"}
      </td>
      <td className="num py-2 text-[13px] text-muted">{variant.sku}</td>
      <td className="num py-2 text-right text-[13px]">{formatInr(variant.sellingPrice)}</td>
      <td className="num py-2 text-right text-[13px]">{variant.physicalQty}</td>
      <td />
    </tr>
  );
}

export function ProductsPage() {
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [expanded, setExpanded] = useState<number | null>(null);
  const queryClient = useQueryClient();

  const products = useQuery({
    queryKey: ["products", search, includeInactive],
    queryFn: () => listProducts(search, includeInactive),
  });

  const retire = useMutation({
    mutationFn: deactivateProduct,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["products"] }),
  });

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-display text-[28px] font-bold tracking-tight">Products</h1>
          <p className="mt-1 text-[13px] text-muted">
            Everything you sell, and every colour and size it comes in.
          </p>
        </div>
        <Link
          to="/products/new"
          className="font-display rounded-stamp bg-saddle px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90"
        >
          New product
        </Link>
      </div>

      <div className="topstitch mt-6" />

      <div className="mt-6 flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name, code or SKU"
          aria-label="Search products"
          className="rounded-stamp border border-rule bg-panel px-3 py-2 text-[14px] transition-colors placeholder:text-muted/60 focus:border-saddle sm:w-80"
        />
        <label className="flex items-center gap-2 text-[13px] text-muted">
          <input
            type="checkbox"
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.target.checked)}
          />
          Show retired
        </label>
      </div>

      {products.isPending && <p className="mt-8 text-[13px] text-muted">Loading</p>}

      {products.isError && (
        <p role="alert" className="mt-8 text-[13px] text-flag">
          Could not load products. Check that the API is running and try again.
        </p>
      )}

      {products.data?.length === 0 && (
        <div className="mt-10">
          <h2 className="font-display text-[20px] font-bold">
            {search ? "Nothing matches that" : "No products yet"}
          </h2>
          <p className="mt-2 max-w-prose text-[14px] leading-relaxed text-muted">
            {search
              ? "Try a shorter search — it matches names, product codes and SKUs."
              : "Add your first bag and SlingKing will give it a code. Colours and sizes come next, each with its own SKU and price."}
          </p>
        </div>
      )}

      {products.data && products.data.length > 0 && (
        <table className="mt-8 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">Product</th>
              <th className="pb-2 font-medium">Code</th>
              <th className="pb-2 text-right font-medium">Price</th>
              <th className="pb-2 text-right font-medium">Stock</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {products.data.map((product) => {
              const active = product.variants.filter((v) => v.isActive);
              const prices = active.map((v) => v.sellingPrice);
              const low = prices.length ? Math.min(...prices) : null;
              const high = prices.length ? Math.max(...prices) : null;
              const isOpen = expanded === product.id;

              return (
                // Fragment, not <>, because the row and its expanded variants are siblings
                // inside a map and need one key between them.
                <Fragment key={product.id}>
                  <tr className={`border-b border-rule ${product.isActive ? "" : "opacity-50"}`}>
                    <td className="py-3">
                      <button
                        type="button"
                        onClick={() => setExpanded(isOpen ? null : product.id)}
                        aria-expanded={isOpen}
                        className="text-left text-[14px] font-medium"
                      >
                        {isOpen ? "▾" : "▸"} {product.name}
                        {!product.isActive && " (retired)"}
                      </button>
                      <span className="ml-2 text-[12px] text-muted">
                        {product.categoryName ?? "Uncategorised"} ·{" "}
                        {active.length} {active.length === 1 ? "variation" : "variations"}
                      </span>
                    </td>
                    <td className="num py-3 text-[13px] text-muted">{product.code}</td>
                    <td className="num py-3 text-right text-[13px]">
                      {low === null
                        ? "—"
                        : low === high
                          ? formatInr(low)
                          : `${formatInr(low)} – ${formatInr(high!)}`}
                    </td>
                    <td className="num py-3 text-right text-[13px]">{product.totalStock}</td>
                    <td className="py-3 text-right">
                      <Link
                        to={`/products/${product.id}`}
                        className="text-[13px] font-medium text-saddle"
                      >
                        Edit
                      </Link>
                      {product.isActive && (
                        <button
                          type="button"
                          onClick={() => retire.mutate(product.id)}
                          className="ml-4 text-[13px] text-muted transition-colors hover:text-flag"
                        >
                          Retire
                        </button>
                      )}
                    </td>
                  </tr>
                  {isOpen &&
                    product.variants.map((variant) => (
                      <VariantRow key={variant.id} variant={variant} />
                    ))}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
