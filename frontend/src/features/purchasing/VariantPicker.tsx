import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { listStock } from "../../api/inventory";

/**
 * The product form's category dropdown picks from about five things; this picks from every
 * colour of every bag in the catalogue, so it is searchable rather than a bare select.
 *
 * It reads the inventory list because that endpoint already returns exactly one row per
 * usable variant — same product-active filter a purchase needs — so no new endpoint exists
 * just to feed a dropdown.
 */
export function VariantPicker({
  value,
  onChange,
  label,
}: {
  value: number | null;
  onChange: (variantId: number | null) => void;
  label: string;
}) {
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);

  const stock = useQuery({ queryKey: ["stock", "", false], queryFn: () => listStock("", false) });

  const chosen = stock.data?.find((row) => row.variantId === value) ?? null;

  const matches = useMemo(() => {
    const needle = term.trim().toLowerCase();
    const rows = stock.data ?? [];

    if (needle === "") return rows.slice(0, 8);

    return rows
      .filter((row) =>
        [row.productName, row.sku, row.colour, row.size]
          .filter(Boolean)
          .join(" ")
          .toLowerCase()
          .includes(needle),
      )
      .slice(0, 8);
  }, [stock.data, term]);

  if (chosen && !open) {
    return (
      <button
        type="button"
        aria-label={label}
        onClick={() => {
          setTerm("");
          setOpen(true);
        }}
        className="rounded-stamp border-rule bg-panel w-full border px-2 py-1.5 text-left text-[14px]"
      >
        {chosen.productName}
        <span className="text-muted block text-[12px]">
          {[chosen.colour, chosen.size].filter(Boolean).join(" · ") || "No variation"} ·{" "}
          <span className="num">{chosen.sku}</span>
        </span>
      </button>
    );
  }

  return (
    <div className="relative">
      <input
        aria-label={label}
        value={term}
        autoFocus={open}
        placeholder="Search a bag"
        onChange={(e) => {
          setTerm(e.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        className="rounded-stamp border-rule bg-panel focus:border-saddle w-full border px-2 py-1.5 text-[14px]"
      />
      {open && (
        <ul className="border-rule bg-panel absolute z-10 mt-1 max-h-64 w-full overflow-auto border shadow-sm">
          {matches.length === 0 && (
            <li className="text-muted px-3 py-2 text-[13px]">
              Nothing matches. Add the bag on the Products screen first.
            </li>
          )}
          {matches.map((row) => (
            <li key={row.variantId}>
              <button
                type="button"
                onClick={() => {
                  onChange(row.variantId);
                  setOpen(false);
                }}
                className="hover:bg-canvas w-full px-3 py-2 text-left text-[13px]"
              >
                {row.productName}
                <span className="text-muted block text-[12px]">
                  {[row.colour, row.size].filter(Boolean).join(" · ") || "No variation"} ·{" "}
                  <span className="num">{row.sku}</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
