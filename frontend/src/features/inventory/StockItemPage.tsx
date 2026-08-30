import { useNavigate, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getStockItem, movementLabels } from "../../api/inventory";
import { formatInr } from "../../lib/money";
import { formatIstDateTime } from "../../lib/dates";
import { RecordMovement } from "./RecordMovement";
import { Recount } from "./Recount";

export function StockItemPage() {
  const { variantId } = useParams();
  const id = Number(variantId);
  const navigate = useNavigate();

  const item = useQuery({
    queryKey: ["stockItem", id],
    queryFn: () => getStockItem(id),
    enabled: Number.isInteger(id),
  });

  if (item.isPending) {
    return <p className="mx-auto max-w-3xl px-6 py-10 text-[13px] text-muted">Loading</p>;
  }

  if (item.isError || !item.data) {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <p role="alert" className="text-[13px] text-flag">
          That item could not be loaded. It may have been retired, or the server may be
          unreachable.
        </p>
        <button
          type="button"
          onClick={() => navigate("/inventory")}
          className="mt-4 text-[14px] text-saddle"
        >
          Back to inventory
        </button>
      </div>
    );
  }

  const stock = item.data;

  // Oldest-first, accumulating, then flipped back — the API sends newest-first because that
  // is the reading order, but a running total only makes sense counted forwards.
  const oldestFirst = [...stock.movements].reverse();
  let carried = 0;
  const withTotals = oldestFirst.map((movement) => {
    carried += movement.physicalDelta;
    return { movement, runningTotal: carried };
  });
  withTotals.reverse();

  return (
    <div className="mx-auto max-w-3xl px-6 py-10">
      <button
        type="button"
        onClick={() => navigate("/inventory")}
        className="text-[13px] text-muted transition-colors hover:text-saddle"
      >
        ← Inventory
      </button>

      <h1 className="font-display mt-3 text-[28px] font-bold tracking-tight">
        {stock.productName}
      </h1>
      <p className="mt-1 text-[13px] text-muted">
        <span className="num">{stock.sku}</span>
        {" · "}
        {[stock.colour, stock.size].filter(Boolean).join(" · ") || "No variation"}
      </p>

      <p className="num font-display mt-6 text-[44px] leading-none font-bold">
        {stock.physicalQty}
      </p>
      <p className="mt-1 text-[13px] text-muted">
        in stock, worth <span className="num">{formatInr(stock.stockValue)}</span>
      </p>

      <div className="topstitch mt-8" />

      <div className="mt-6 grid gap-8 sm:grid-cols-2">
        <section>
          <h2 className="font-display text-[16px] font-bold">Record a movement</h2>
          <p className="mt-1 text-[13px] text-muted">How many came in or went out.</p>
          <RecordMovement variantId={stock.variantId} />
        </section>

        <section>
          <h2 className="font-display text-[16px] font-bold">Recount</h2>
          <p className="mt-1 text-[13px] text-muted">
            How many there are in total, right now.
          </p>
          <Recount variantId={stock.variantId} current={stock.physicalQty} />
        </section>
      </div>

      <div className="topstitch mt-8" />

      <h2 className="font-display mt-6 text-[16px] font-bold">History</h2>
      <p className="mt-1 text-[13px] text-muted">
        Entries cannot be edited or removed. Correct a mistake by recording its opposite —
        both stay here.
      </p>

      {withTotals.length === 0 ? (
        <p className="mt-6 text-[13px] text-muted">
          Nothing recorded yet. Start with the opening stock.
        </p>
      ) : (
        <table className="mt-4 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">When</th>
              <th className="pb-2 font-medium">What</th>
              <th className="pb-2 text-right font-medium">Change</th>
              <th className="pb-2 text-right font-medium">Left</th>
              <th className="pb-2 text-right font-medium">Cost</th>
            </tr>
          </thead>
          <tbody>
            {withTotals.map(({ movement, runningTotal }) => (
              <tr key={movement.id} className="border-b border-rule align-top">
                <td className="num py-2.5 text-[13px] text-muted">
                  {formatIstDateTime(movement.occurredAt)}
                </td>
                <td className="py-2.5 text-[13px]">
                  {movementLabels[movement.type] ?? movement.type}
                  {movement.reason && (
                    <span className="block text-[12px] text-muted">{movement.reason}</span>
                  )}
                </td>
                <td
                  className={`num py-2.5 text-right text-[13px] ${
                    movement.physicalDelta < 0 ? "text-flag" : ""
                  }`}
                >
                  {movement.physicalDelta > 0 ? `+${movement.physicalDelta}` : movement.physicalDelta}
                </td>
                <td className="num py-2.5 text-right text-[13px]">{runningTotal}</td>
                <td className="num py-2.5 text-right text-[13px] text-muted">
                  {formatInr(movement.totalCost)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
