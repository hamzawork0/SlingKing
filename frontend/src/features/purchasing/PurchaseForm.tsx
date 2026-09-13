import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { recordPurchase } from "../../api/purchasing";
import { serverMessage } from "../../api/client";
import { formatInr } from "../../lib/money";
import {
  hasUnparseableCost,
  lineTotal,
  parseCost,
  parseQuantity,
  purchaseTotal,
} from "../../lib/purchaseTotals";
import { SupplierSelect } from "./SupplierSelect";
import { VariantPicker } from "./VariantPicker";

const field =
  "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
  "transition-colors placeholder:text-muted/60 focus:border-saddle";

interface DraftRow {
  id: string;
  variantId: number | null;
  quantity: string;
  unitCost: string;
}

// A stable id, not the array index: removing a middle row shifts every later index, which
// would re-parent each VariantPicker onto a different logical row and carry its open/term
// state along with it.
function newRow(): DraftRow {
  return { id: crypto.randomUUID(), variantId: null, quantity: "", unitCost: "" };
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function PurchaseForm() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [supplierId, setSupplierId] = useState<number | null>(null);
  const [purchaseDate, setPurchaseDate] = useState(today());
  const [notes, setNotes] = useState("");
  const [rows, setRows] = useState<DraftRow[]>([newRow()]);
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: recordPurchase,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["purchases"] });
      // The purchase raised stock, so both stock screens are now stale.
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      queryClient.invalidateQueries({ queryKey: ["products"] });
      navigate("/purchases");
    },
    onError: (err) => setError(serverMessage(err, "That purchase could not be recorded.")),
  });

  function patchRow(index: number, patch: Partial<DraftRow>) {
    setRows((current) => current.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  const draft = rows.map((r) => ({
    quantity: parseQuantity(r.quantity),
    unitCost: parseCost(r.unitCost),
  }));

  const runningTotal = purchaseTotal(draft);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (supplierId === null) {
      setError("Choose who you bought from.");
      return;
    }

    const lines = rows
      .map((row, i) => ({ row, quantity: draft[i].quantity, unitCost: draft[i].unitCost }))
      .filter((l) => l.row.variantId !== null || l.quantity !== null);

    if (lines.length === 0) {
      setError("Add at least one bag to this purchase.");
      return;
    }

    if (lines.some((l) => l.row.variantId === null)) {
      setError("Every line needs a bag. Pick one, or clear the line.");
      return;
    }

    if (lines.some((l) => l.quantity === null)) {
      setError("Enter how many, as a number above zero.");
      return;
    }

    if (hasUnparseableCost(rows.map((r) => r.unitCost))) {
      setError("That cost is not a number. Leave it blank if you do not know it.");
      return;
    }

    const chosen = lines.map((l) => l.row.variantId);
    if (new Set(chosen).size !== chosen.length) {
      setError("That bag appears twice. Put the whole quantity on one line.");
      return;
    }

    save.mutate({
      supplierId,
      purchaseDate: new Date(purchaseDate).toISOString(),
      notes: notes.trim() || null,
      lines: lines.map((l) => ({
        variantId: l.row.variantId!,
        quantity: l.quantity!,
        unitCost: l.unitCost,
      })),
    });
  }

  return (
    <div className="mx-auto max-w-4xl px-6 py-10">
      <h1 className="font-display text-[28px] font-bold tracking-tight">Record a purchase</h1>
      <p className="text-muted mt-1 text-[13px]">
        What you brought back, and what you paid for it.
      </p>

      <div className="topstitch mt-6" />

      <form onSubmit={handleSubmit} className="mt-6">
        <span className="text-[12px] font-medium tracking-wide">Supplier</span>
        <SupplierSelect value={supplierId} onChange={setSupplierId} />

        <div className="mt-4 flex flex-wrap gap-4">
          <div>
            <label className="text-[12px] font-medium tracking-wide" htmlFor="purchase-date">
              Date
            </label>
            {/* A native date input: the platform already knows how to enter a date on a
                phone, and it back-dates without ceremony. */}
            <input
              id="purchase-date"
              type="date"
              value={purchaseDate}
              onChange={(e) => setPurchaseDate(e.target.value)}
              className={`num ${field} w-48`}
            />
          </div>
          <div className="flex-1">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="purchase-notes">
              Notes <span className="text-muted font-normal">(optional)</span>
            </label>
            <input
              id="purchase-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              className={field}
            />
          </div>
        </div>

        <div className="topstitch mt-8" />

        <h2 className="font-display mt-6 text-[16px] font-bold">What you bought</h2>
        <p className="text-muted mt-1 text-[13px]">
          Leave a cost blank if you do not know it. It stays unknown rather than becoming zero.
        </p>

        <table className="mt-4 w-full border-collapse">
          <thead>
            <tr className="border-rule text-muted border-b text-left text-[12px] tracking-wide uppercase">
              <th className="pb-2 font-medium">Bag</th>
              <th className="pb-2 font-medium">How many</th>
              <th className="pb-2 font-medium">Cost each</th>
              <th className="pb-2 text-right font-medium">Line total</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={row.id} className="border-rule border-b">
                <td className="w-1/2 py-2 pr-2 align-top">
                  <VariantPicker
                    value={row.variantId}
                    onChange={(variantId) => patchRow(index, { variantId })}
                    label={`Bag ${index + 1}`}
                  />
                </td>
                <td className="py-2 pr-2 align-top">
                  <input
                    inputMode="numeric"
                    aria-label={`How many ${index + 1}`}
                    value={row.quantity}
                    onChange={(e) => patchRow(index, { quantity: e.target.value })}
                    className="num rounded-stamp border-rule w-20 border px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="py-2 pr-2 align-top">
                  <input
                    inputMode="decimal"
                    aria-label={`Cost each ${index + 1}`}
                    value={row.unitCost}
                    onChange={(e) => patchRow(index, { unitCost: e.target.value })}
                    className="num rounded-stamp border-rule w-28 border px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="num py-3.5 text-right text-[14px] align-top">
                  {formatInr(lineTotal(draft[index].quantity, draft[index].unitCost))}
                </td>
                <td className="py-3.5 text-right align-top">
                  {rows.length > 1 && (
                    <button
                      type="button"
                      aria-label={`Remove line ${index + 1}`}
                      onClick={() => setRows(rows.filter((_, i) => i !== index))}
                      className="text-muted hover:text-flag text-[13px] transition-colors"
                    >
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
          <button
            type="button"
            onClick={() => setRows([...rows, newRow()])}
            className="text-saddle text-[13px] font-medium"
          >
            + Add a line
          </button>

          {/* Visible while lines are entered, because this is the number being checked
              against the bill in hand. A total that appears only after saving arrives too
              late to catch the mistake. */}
          <p className="text-[14px]">
            Total <span className="num font-display text-[18px] font-bold">
              {formatInr(runningTotal)}
            </span>
          </p>
        </div>

        {error && (
          <p role="alert" className="text-flag mt-6 text-[13px]">
            {error}
          </p>
        )}

        <div className="mt-8 flex gap-3">
          <button
            type="submit"
            disabled={save.isPending}
            className="font-display rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
          >
            {save.isPending ? "Recording" : "Record the purchase"}
          </button>
          <button
            type="button"
            onClick={() => navigate("/purchases")}
            className="text-muted text-[14px]"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
