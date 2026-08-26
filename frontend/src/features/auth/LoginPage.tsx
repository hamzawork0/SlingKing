import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../app/AuthContext";
import { isNetworkError } from "../../api/client";

export function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      await signIn(email, password);
      navigate("/", { replace: true });
    } catch (err) {
      // One message for every REJECTED credential: the API does not distinguish
      // "no such user" from "wrong password", and neither should the screen.
      // An unreachable server is a different fact, and saying the password is wrong
      // when it is not sends the owner hunting for a reset that will not help.
      setError(
        isNetworkError(err)
          ? "Cannot reach the server. Check your connection and try again."
          : "Email or password is incorrect.",
      );
    } finally {
      setIsSubmitting(false);
    }
  }

  const field =
    "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
    "transition-colors placeholder:text-muted/60 focus:border-saddle";

  return (
    <div className="flex h-full items-center justify-center bg-canvas px-4 py-10">
      <form
        onSubmit={handleSubmit}
        className="relative w-full max-w-[380px] rounded-stamp border border-rule bg-panel px-9 pb-9 pt-11 shadow-[0_1px_2px_rgba(20,26,23,0.06)]"
      >
        {/* The punched eyelet. */}
        <span
          aria-hidden
          className="absolute left-1/2 top-4 h-3 w-3 -translate-x-1/2 rounded-full border border-rule bg-canvas shadow-[inset_0_1px_1px_rgba(20,26,23,0.12)]"
        />

        {/* The topstitch, inset from the card edge, drawing itself on load. */}
        <svg
          aria-hidden
          className="pointer-events-none absolute inset-0 h-full w-full"
          preserveAspectRatio="none"
        >
          <rect
            x="10"
            y="10"
            width="calc(100% - 20px)"
            height="calc(100% - 20px)"
            rx="2"
            fill="none"
            stroke="var(--color-saddle)"
            strokeWidth="1"
            strokeDasharray="5 4"
            opacity="0.5"
            className="stitch"
          />
        </svg>

        <div className="relative">
          <h1 className="font-display text-[22px] font-bold tracking-[0.12em] uppercase">
            SlingKing
          </h1>
          <p className="mt-1 text-[13px] text-muted">Sign in to the stock ledger.</p>

          <div className="mt-7">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="email">
              Email
            </label>
            <input
              id="email"
              type="email"
              required
              autoFocus
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className={field}
            />
          </div>

          <div className="mt-4">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={field}
            />
          </div>

          {error && (
            <p role="alert" className="mt-4 text-[13px] text-flag">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={isSubmitting}
            className="font-display mt-7 w-full rounded-stamp bg-saddle px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
          >
            {isSubmitting ? "Signing in" : "Sign in"}
          </button>
        </div>
      </form>
    </div>
  );
}
