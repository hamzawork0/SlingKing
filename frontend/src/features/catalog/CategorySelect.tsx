import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createCategory, listCategories } from "../../api/catalog";
import { serverMessage } from "../../api/client";

/**
 * Categories get no screen of their own — the owner will create about five and then never
 * touch them. Creating one inline is the whole management interface.
 */
export function CategorySelect({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const categories = useQuery({ queryKey: ["categories"], queryFn: listCategories });

  const create = useMutation({
    mutationFn: createCategory,
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      onChange(created.id);
      setAdding(false);
      setName("");
      setError(null);
    },
    onError: (err) =>
      setError(serverMessage(err, "That category could not be added. It may already exist.")),
  });

  if (adding) {
    return (
      <div className="mt-1.5">
        <div className="flex gap-2">
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="New category name"
            aria-label="New category name"
            className="flex-1 rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] focus:border-saddle"
          />
          <button
            type="button"
            disabled={!name.trim() || create.isPending}
            onClick={() => create.mutate(name.trim())}
            className="font-display rounded-stamp bg-saddle px-4 text-[13px] font-semibold text-white disabled:opacity-55"
          >
            Add
          </button>
          <button
            type="button"
            onClick={() => {
              setAdding(false);
              setError(null);
            }}
            className="text-[13px] text-muted"
          >
            Cancel
          </button>
        </div>
        {error && (
          <p role="alert" className="mt-2 text-[13px] text-flag">
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
        className="flex-1 rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] focus:border-saddle"
      >
        <option value="">Uncategorised</option>
        {categories.data?.map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </select>
      <button
        type="button"
        onClick={() => setAdding(true)}
        className="text-[13px] font-medium text-saddle"
      >
        + New
      </button>
    </div>
  );
}
