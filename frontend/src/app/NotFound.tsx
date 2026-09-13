import { Link, isRouteErrorResponse, useRouteError } from "react-router-dom";

/**
 * Serves two jobs: the catch-all route for a URL that does not exist, and the
 * router's errorElement when a screen throws. Without it React Router shows its
 * own developer page — a stack trace and a 💿 emoji — to whoever is looking.
 */
export function NotFound() {
  // Null — not undefined — when this renders as the catch-all rather than an error
  // boundary, so this has to be a truthiness test.
  const error = useRouteError();
  const crashed = !!error && !isRouteErrorResponse(error);

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <p className="text-[12px] font-medium tracking-[0.16em] text-muted uppercase">
        {crashed ? "Something broke" : "Not here"}
      </p>
      <h1 className="font-display mt-1 text-[28px] font-bold tracking-tight">
        {crashed ? "This screen failed to load" : "This page does not exist"}
      </h1>
      <p className="mt-2 max-w-prose text-[14px] leading-relaxed text-muted">
        {crashed
          ? "The problem has been logged. Going back to the dashboard is usually enough to carry on."
          : "Products, Inventory, and Purchases are where the daily work happens."}
      </p>

      <div className="topstitch mt-8" />

      <Link
        to="/"
        className="font-display rounded-stamp bg-saddle mt-8 inline-block px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90"
      >
        Back to dashboard
      </Link>
    </div>
  );
}
