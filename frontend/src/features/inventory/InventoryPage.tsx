import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { listStock } from "../../api/inventory";

export function InventoryPage() {
  const [search, setSearch] = useState("");
  const [lowOnly, setLowOnly] = useState(false);

  const stock = useQuery({
    queryKey: ["stock", search, lowOnly],
    queryFn: () => listStock(search, lowOnly),
  });

  const nothingCounted =
    stock.data !== undefined &&
    stock.data.length > 0 &&
    stock.data.every((row) => row.physicalQty === 0);

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <h1 className="font-display text-[28px] font-bold tracking-tight">Inventory</h1>
      <p className="mt-1 text-[13px] text-muted">
        How many of each bag you have, and where they went.
      </p>

      <div className="topstitch mt-6" />

      <div className="mt-6 flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name, code or SKU"
          aria-label="Search stock"
          className="rounded-stamp border border-rule bg-panel px-3 py-2 text-[14px] transition-colors placeholder:text-muted/60 focus:border-saddle sm:w-80"
        />
        <label className="flex items-center gap-2 text-[13px] text-muted">
          <input
            type="checkbox"
            checked={lowOnly}
            onChange={(e) => setLowOnly(e.target.checked)}
          />
          Low stock only
        </label>
      </div>

      {stock.isPending && <p className="mt-8 text-[13px] text-muted">Loading</p>}

      {stock.isError && (
        <p role="alert" className="mt-8 text-[13px] text-flag">
          Could not load stock. Check that the API is running and try again.
        </p>
      )}

      {stock.data?.length === 0 && (
        <p className="mt-8 text-[13px] text-muted">
          {search || lowOnly
            ? "Nothing matches that."
            : "No items yet. Add a product first, then come back and count it."}
        </p>
      )}

      {nothingCounted && (
        <p className="mt-8 text-[13px] text-muted">
          Nothing counted yet. Open an item and record its opening stock.
        </p>
      )}

      {stock.data && stock.data.length > 0 && (
        <table className="mt-6 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">Item</th>
              <th className="pb-2 font-medium">SKU</th>
              <th className="pb-2 text-right font-medium">In stock</th>
              <th className="pb-2 text-right font-medium">Low at</th>
            </tr>
          </thead>
          <tbody>
            {stock.data.map((row) => {
              // A threshold of zero means none was set, so it can never be low. Matches
              // exactly what lowOnly filters on at the server.
              const low = row.lowStockThreshold > 0 && row.physicalQty <= row.lowStockThreshold;

              return (
                <tr key={row.variantId} className="border-b border-rule">
                  <td className="py-2.5">
                    <Link
                      to={`/inventory/${row.variantId}`}
                      className="text-[14px] font-medium hover:text-saddle"
                    >
                      {row.productName}
                    </Link>
                    <span className="block text-[12px] text-muted">
                      {[row.colour, row.size].filter(Boolean).join(" · ") || "No variation"}
                    </span>
                  </td>
                  <td className="num py-2.5 text-[13px] text-muted">{row.sku}</td>
                  <td
                    className={`num py-2.5 text-right text-[14px] ${low ? "font-semibold text-flag" : ""}`}
                  >
                    {row.physicalQty}
                  </td>
                  <td className="num py-2.5 text-right text-[13px] text-muted">
                    {row.lowStockThreshold > 0 ? row.lowStockThreshold : "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
