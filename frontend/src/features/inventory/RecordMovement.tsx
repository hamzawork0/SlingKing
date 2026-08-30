import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  movementLabels,
  movementTypes,
  outflowTypes,
  recordMovement,
  type MovementType,
} from "../../api/inventory";
import { serverMessage } from "../../api/client";
import { parseCount } from "./recount";

const field =
  "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
  "transition-colors placeholder:text-muted/60 focus:border-saddle";

export function RecordMovement({ variantId }: { variantId: number }) {
  const [type, setType] = useState<MovementType>("OpeningStock");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const isOutflow = outflowTypes.includes(type);

  const save = useMutation({
    mutationFn: recordMovement,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["stockItem", variantId] });
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      setQuantity("");
      setUnitCost("");
      setReason("");
      setError(null);
    },
    onError: (err) => setError(serverMessage(err, "That movement could not be recorded.")),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    const parsed = parseCount(quantity);

    if (parsed === null || parsed < 1) {
      setError("Enter how many, as a number above zero.");
      return;
    }

    if (isOutflow && !reason.trim()) {
      setError("Say briefly what happened, so this makes sense later.");
      return;
    }

    // An empty cost box means unknown, never zero. Number("") would say the bags were free.
    const cost = unitCost.trim() === "" ? null : Number(unitCost);

    if (cost !== null && (!Number.isFinite(cost) || cost < 0)) {
      setError("A cost cannot be negative.");
      return;
    }

    save.mutate({
      variantId,
      type,
      quantity: parsed,
      unitCost: isOutflow ? null : cost,
      reason: reason.trim() || null,
    });
  }

  return (
    <form onSubmit={handleSubmit} className="mt-4">
      <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-type">
        What happened
      </label>
      <select
        id="movement-type"
        value={type}
        onChange={(e) => setType(e.target.value as MovementType)}
        className={field}
      >
        {movementTypes.map((option) => (
          <option key={option} value={option}>
            {movementLabels[option]}
          </option>
        ))}
      </select>

      <div className="mt-4 flex gap-3">
        <div className="flex-1">
          <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-quantity">
            How many
          </label>
          <input
            id="movement-quantity"
            inputMode="numeric"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className={`num ${field}`}
          />
        </div>

        {/* Only an inflow can carry a cost. The server refuses one on an outflow, so
            offering the box there would be offering a field that cannot be submitted. */}
        {!isOutflow && (
          <div className="flex-1">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-cost">
              Cost each <span className="font-normal text-muted">(optional)</span>
            </label>
            <input
              id="movement-cost"
              inputMode="decimal"
              placeholder="Leave blank if unknown"
              value={unitCost}
              onChange={(e) => setUnitCost(e.target.value)}
              className={`num ${field}`}
            />
          </div>
        )}
      </div>

      <div className="mt-4">
        <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-reason">
          Why {isOutflow ? "" : <span className="font-normal text-muted">(optional)</span>}
        </label>
        <input
          id="movement-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={isOutflow ? "Water damaged in transit" : ""}
          className={field}
        />
      </div>

      {error && (
        <p role="alert" className="mt-3 text-[13px] text-flag">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={save.isPending}
        className="font-display mt-4 rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
      >
        {save.isPending ? "Recording" : "Record"}
      </button>
    </form>
  );
}
