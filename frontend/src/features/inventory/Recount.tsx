import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { recount } from "../../api/inventory";
import { serverMessage } from "../../api/client";
import { parseCount, recountSentence } from "./recount";

export function Recount({ variantId, current }: { variantId: number; current: number }) {
  const [counted, setCounted] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const parsed = parseCount(counted);
  const sentence = recountSentence(parsed, current);

  const save = useMutation({
    mutationFn: (value: number) => recount(variantId, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["stockItem", variantId] });
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      setCounted("");
      setError(null);
    },
    onError: (err) => setError(serverMessage(err, "That recount could not be saved.")),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (parsed === null) {
      setError("Enter the number you counted.");
      return;
    }

    save.mutate(parsed);
  }

  return (
    <form onSubmit={handleSubmit} className="mt-4">
      <label className="text-[12px] font-medium tracking-wide" htmlFor="recount-counted">
        How many did you count
      </label>
      <input
        id="recount-counted"
        inputMode="numeric"
        value={counted}
        onChange={(e) => setCounted(e.target.value)}
        className="num mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] transition-colors focus:border-saddle"
      />

      {/* You confirm the sentence, not the number. The two forms on this page take numbers
          that mean opposite things, and this is the line that says which one this is. */}
      {sentence && <p className="mt-3 text-[13px] text-muted">{sentence}</p>}

      {error && (
        <p role="alert" className="mt-3 text-[13px] text-flag">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={save.isPending || parsed === null || parsed === current}
        className="font-display mt-4 rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
      >
        {save.isPending ? "Saving" : "Save the count"}
      </button>
    </form>
  );
}
