import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createCategory, listCategories, renameCategory } from "../../api/catalog";
import { serverMessage } from "../../api/client";

/**
 * Categories get no screen of their own — the owner will create about five and then mostly
 * leave them alone. Creating and correcting one inline is the whole management interface.
 */
export function CategorySelect({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const [mode, setMode] = useState<"idle" | "adding" | "renaming">("idle");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const categories = useQuery({ queryKey: ["categories"], queryFn: listCategories });

  const selected = categories.data?.find((c) => c.id === value) ?? null;

  function close() {
    setMode("idle");
    setName("");
    setError(null);
  }

  const create = useMutation({
    mutationFn: createCategory,
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      onChange(created.id);
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That category could not be added. It may already exist.")),
  });

  const rename = useMutation({
    mutationFn: (next: string) => renameCategory(value!, next),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      // The id does not change, so the current selection stays correct.
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That category could not be renamed. The name may be taken.")),
  });

  if (mode !== "idle") {
    const isRenaming = mode === "renaming";
    const busy = create.isPending || rename.isPending;

    return (
      <div className="mt-1.5">
        <div className="flex gap-2">
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={isRenaming ? "Category name" : "New category name"}
            aria-label={isRenaming ? "Rename category" : "New category name"}
            className="rounded-stamp border-rule bg-panel focus:border-saddle flex-1 border px-3 py-2.5 text-[14px]"
          />
          <button
            type="button"
            disabled={!name.trim() || busy}
            onClick={() =>
              isRenaming ? rename.mutate(name.trim()) : create.mutate(name.trim())
            }
            className="font-display rounded-stamp bg-saddle px-4 text-[13px] font-semibold text-white disabled:opacity-55"
          >
            {isRenaming ? "Save" : "Add"}
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
        className="rounded-stamp border-rule bg-panel focus:border-saddle flex-1 border px-3 py-2.5 text-[14px]"
      >
        <option value="">Uncategorised</option>
        {categories.data?.map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </select>
      {/* Only offered for a category that actually exists — there is nothing to rename
          while "Uncategorised" is selected. */}
      {selected && (
        <button
          type="button"
          onClick={() => {
            setName(selected.name);
            setMode("renaming");
          }}
          className="text-saddle text-[13px] font-medium"
        >
          Rename
        </button>
      )}
      <button
        type="button"
        onClick={() => {
          setName("");
          setMode("adding");
        }}
        className="text-saddle text-[13px] font-medium"
      >
        + New
      </button>
    </div>
  );
}
