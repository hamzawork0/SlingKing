import { useAuth } from "../../app/AuthContext";

export function DashboardPage() {
  const { user } = useAuth();

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <p className="text-[12px] font-medium tracking-[0.16em] uppercase text-muted">
        Signed in as {user?.displayName}
      </p>
      <h1 className="font-display mt-1 text-[28px] font-bold tracking-tight">
        Nothing in the ledger yet
      </h1>
      <p className="mt-2 max-w-prose text-[14px] leading-relaxed text-muted">
        Your database is set up and every table is ready. Products, stock and purchases
        come next — once they land, this page shows what you hold and what it cost.
      </p>

      <div className="topstitch mt-8" />

      <dl className="mt-8 grid gap-px overflow-hidden rounded-stamp border border-rule bg-rule sm:grid-cols-3">
        {[
          { label: "Products", value: "0" },
          { label: "Units in stock", value: "0" },
          { label: "Stock value", value: "—" },
        ].map((tile) => (
          <div key={tile.label} className="bg-panel px-5 py-4">
            <dt className="text-[12px] tracking-wide text-muted">{tile.label}</dt>
            <dd className="num mt-1 text-[24px] font-medium">{tile.value}</dd>
          </div>
        ))}
      </dl>

      <p className="mt-3 text-[12px] text-muted">
        Stock value reads &ldquo;&mdash;&rdquo; when no cost is known. It never reads
        &#8377;0 &mdash; an unknown cost is not a free bag.
      </p>
    </div>
  );
}
