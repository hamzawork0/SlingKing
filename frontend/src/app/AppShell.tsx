import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";

const navigation = [
  { to: "/", label: "Dashboard", end: true },
  { to: "/products", label: "Products" },
  { to: "/inventory", label: "Inventory" },
  { to: "/purchases", label: "Purchases" },
];

export function AppShell() {
  const { user, signOut } = useAuth();

  return (
    <div className="flex h-full flex-col bg-canvas">
      <header className="bg-thread text-white">
        {/* Wraps to two rows on a phone: identity and sign-out first, then the rail.
            No hamburger, no JS — four links scroll sideways more cheaply than a menu. */}
        <div className="flex flex-wrap items-center gap-x-6 px-4 sm:gap-8 sm:px-6">
          <NavLink
            to="/"
            className="font-display py-3 text-[15px] font-bold tracking-[0.14em] uppercase sm:py-0"
          >
            SlingKing
          </NavLink>

          {/* Escapes the container's px-4 on mobile so the four links fit the viewport
              exactly. Left overflowing they cleared the screen but left a scrollbar strip
              across the rail, which reads as breakage rather than as a bar. */}
          <nav className="order-3 -mx-4 flex w-[calc(100%+2rem)] items-stretch overflow-x-auto px-1 sm:order-none sm:mx-0 sm:w-auto sm:flex-1 sm:overflow-x-visible sm:px-0">
            {navigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  [
                    "font-display px-3 py-3.5 text-[13px] font-medium tracking-wide whitespace-nowrap transition-colors sm:px-4",
                    isActive
                      ? "text-white shadow-[inset_0_-2px_0_0_var(--color-saddle)]"
                      : "text-white/55 hover:text-white/90",
                  ].join(" ")
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="order-2 ml-auto flex items-center gap-4 sm:order-none">
            {/* Hidden on a phone: Sign out is the control that must stay reachable,
                and the dashboard already says who is signed in. */}
            <span className="hidden text-[12px] text-white/50 sm:inline">{user?.email}</span>
            <button
              type="button"
              onClick={() => void signOut()}
              className="font-display text-[13px] font-medium text-white/75 transition-colors hover:text-white"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      {/* The stitch line where the strap meets the panel. */}
      <div className="topstitch" />

      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
