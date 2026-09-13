import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createSupplier, listSuppliers, updateSupplier } from "../../api/purchasing";
import { serverMessage } from "../../api/client";

const field =
  "rounded-stamp border-rule bg-panel focus:border-saddle border px-3 py-2.5 text-[14px]";

/**
 * Suppliers get no screen of their own — the owner will have about five and then mostly stop
 * thinking about them. Creating and correcting one inline is the whole management interface,
 * exactly as it is for categories.
 */
export function SupplierSelect({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const [mode, setMode] = useState<"idle" | "adding" | "editing">("idle");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const selected = suppliers.data?.find((s) => s.id === value) ?? null;

  function close() {
    setMode("idle");
    setName("");
    setPhone("");
    setError(null);
  }

  const create = useMutation({
    mutationFn: () => createSupplier(name.trim(), phone.trim() || null),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      onChange(created.id);
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That supplier could not be added. It may already exist.")),
  });

  const update = useMutation({
    mutationFn: () => updateSupplier(value!, name.trim(), phone.trim() || null),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      // The id does not change, so the current selection stays correct.
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That supplier could not be saved. The name may be taken.")),
  });

  if (mode !== "idle") {
    const isEditing = mode === "editing";
    const busy = create.isPending || update.isPending;

    return (
      <div className="mt-1.5">
        <div className="flex flex-wrap gap-2">
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={isEditing ? "Supplier name" : "New supplier name"}
            aria-label={isEditing ? "Supplier name" : "New supplier name"}
            className={`${field} flex-1`}
          />
          <input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="Phone (optional)"
            aria-label="Supplier phone"
            className={`num ${field} w-40`}
          />
          <button
            type="button"
            disabled={!name.trim() || busy}
            onClick={() => (isEditing ? update.mutate() : create.mutate())}
            className="font-display rounded-stamp bg-saddle px-4 text-[13px] font-semibold text-white disabled:opacity-55"
          >
            {isEditing ? "Save" : "Add"}
          </button>
          <button type="button" onClick={close} className="text-muted text-[13px]">
            Cancel
          </button>
        </div>
        {error && (
          <p role="alert" className="text-flag mt-2 text-[13px]">
            {error}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="mt-1.5 flex gap-2">
      <select
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
        aria-label="Supplier"
        className={`${field} flex-1`}
      >
        <option value="">Choose a supplier</option>
        {suppliers.data?.map((supplier) => (
          <option key={supplier.id} value={supplier.id}>
            {supplier.name}
          </option>
        ))}
      </select>
      {/* Only offered for a supplier that actually exists — there is nothing to edit while
          no supplier is chosen. */}
      {selected && (
        <button
          type="button"
          onClick={() => {
            setName(selected.name);
            setPhone(selected.phone ?? "");
            setMode("editing");
          }}
          className="text-saddle text-[13px] font-medium"
        >
          Edit
        </button>
      )}
      <button
        type="button"
        onClick={() => {
          setName("");
          setPhone("");
          setMode("adding");
        }}
        className="text-saddle text-[13px] font-medium"
      >
        + New
      </button>
    </div>
  );
}
