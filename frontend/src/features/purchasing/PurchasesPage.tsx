import { Fragment, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cancelPurchase, listPurchases, type PurchaseView } from "../../api/purchasing";
import { serverMessage } from "../../api/client";
import { formatInr } from "../../lib/money";
import { formatIstDate } from "../../lib/dates";

function LineRow({ line }: { line: PurchaseView["lines"][number] }) {
  return (
    <tr>
      <td className="text-muted py-2 pl-10 text-[13px]">
        {line.productName}
        <span className="block text-[12px]">
          {[line.colour, line.size].filter(Boolean).join(" · ") || "No variation"}
        </span>
      </td>
      <td className="num text-muted py-2 text-[13px]">{line.sku}</td>
      <td className="num py-2 text-right text-[13px]">{line.quantity}</td>
      <td className="num py-2 text-right text-[13px]">{formatInr(line.unitCost)}</td>
      <td className="num py-2 text-right text-[13px]">{formatInr(line.lineTotal)}</td>
      <td />
    </tr>
  );
}

export function PurchasesPage() {
  const [search, setSearch] = useState("");
  const [includeCancelled, setIncludeCancelled] = useState(false);
  const [expanded, setExpanded] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const purchases = useQuery({
    queryKey: ["purchases", search, includeCancelled],
    queryFn: () => listPurchases(search, includeCancelled),
  });

  const cancel = useMutation({
    mutationFn: cancelPurchase,
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["purchases"] });
      // Cancelling puts stock back out, so both stock screens are now stale.
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      queryClient.invalidateQueries({ queryKey: ["products"] });
    },
    onError: (err) => setError(serverMessage(err, "That purchase could not be cancelled.")),
  });

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-display text-[28px] font-bold tracking-tight">Purchases</h1>
          <p className="text-muted mt-1 text-[13px]">
            What you bought, from whom, and what it cost.
          </p>
        </div>
        <Link
          to="/purchases/new"
          className="font-display rounded-stamp bg-saddle px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90"
        >
          Record a purchase
        </Link>
      </div>

      <div className="topstitch mt-6" />

      <div className="mt-6 flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search number or supplier"
          aria-label="Search purchases"
          className="rounded-stamp border-rule bg-panel placeholder:text-muted/60 focus:border-saddle border px-3 py-2 text-[14px] transition-colors sm:w-80"
        />
        <label className="text-muted flex items-center gap-2 text-[13px]">
          <input
            type="checkbox"
            checked={includeCancelled}
            onChange={(e) => setIncludeCancelled(e.target.checked)}
          />
          Show cancelled
        </label>
      </div>

      {purchases.isPending && <p className="text-muted mt-8 text-[13px]">Loading</p>}

      {purchases.isError && (
        <p role="alert" className="text-flag mt-8 text-[13px]">
          Could not load purchases. Check that the API is running and try again.
        </p>
      )}

      {error && (
        <p role="alert" className="text-flag mt-8 text-[13px]">
          {error}
        </p>
      )}

      {purchases.data?.length === 0 && (
        <p className="text-muted mt-8 text-[13px]">
          {search || includeCancelled
            ? "Nothing matches that."
            : "Nothing bought yet. Record a purchase when you next come back from the market."}
        </p>
      )}

      {purchases.data && purchases.data.length > 0 && (
        <table className="mt-6 w-full border-collapse">
          <thead>
            <tr className="border-rule text-muted border-b text-left text-[12px] tracking-wide uppercase">
              <th className="pb-2 font-medium">Purchase</th>
              <th className="pb-2 font-medium">Supplier</th>
              <th className="pb-2 text-right font-medium">Items</th>
              <th className="pb-2 text-right font-medium" />
              <th className="pb-2 text-right font-medium">Total</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {purchases.data.map((purchase) => {
              const cancelled = purchase.status === "Cancelled";
              const open = expanded === purchase.id;

              return (
                <Fragment key={purchase.id}>
                  <tr className={`border-rule border-b ${cancelled ? "opacity-55" : ""}`}>
                    <td className="py-2.5">
                      <button
                        type="button"
                        onClick={() => setExpanded(open ? null : purchase.id)}
                        className="text-left text-[14px] font-medium"
                      >
                        {open ? "▾" : "▸"} <span className="num">{purchase.number}</span>
                      </button>
                      <span className="num text-muted block text-[12px]">
                        {formatIstDate(purchase.purchaseDate)}
                        {cancelled && " · cancelled"}
                      </span>
                    </td>
                    <td className="py-2.5 text-[13px]">{purchase.supplierName}</td>
                    <td className="num py-2.5 text-right text-[13px]">
                      {purchase.lines.reduce((sum, l) => sum + l.quantity, 0)}
                    </td>
                    <td />
                    <td
                      className={`num py-2.5 text-right text-[14px] ${cancelled ? "line-through" : ""}`}
                    >
                      {formatInr(purchase.totalAmount)}
                    </td>
                    <td className="py-2.5 text-right">
                      {!cancelled && (
                        <button
                          type="button"
                          disabled={cancel.isPending}
                          onClick={() => {
                            const items = purchase.lines.reduce((s, l) => s + l.quantity, 0);
                            if (
                              window.confirm(
                                `Cancel ${purchase.number}? This takes ${items} back out of stock. ` +
                                  `It is refused if any of them have already been sold, damaged or lost.`,
                              )
                            ) {
                              cancel.mutate(purchase.id);
                            }
                          }}
                          className="text-muted hover:text-flag text-[13px] transition-colors disabled:opacity-55"
                        >
                          Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                  {open &&
                    purchase.lines.map((line) => <LineRow key={line.id} line={line} />)}
                  {open && purchase.notes && (
                    <tr>
                      <td colSpan={6} className="text-muted py-2 pl-10 text-[12px]">
                        {purchase.notes}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
