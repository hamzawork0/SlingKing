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
        <div className="flex items-center gap-8 px-6">
          <NavLink
            to="/"
            className="font-display text-[15px] font-bold tracking-[0.14em] uppercase"
          >
            SlingKing
          </NavLink>

          <nav className="flex flex-1 items-stretch">
            {navigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  [
                    "font-display px-4 py-3.5 text-[13px] font-medium tracking-wide transition-colors",
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

          <div className="flex items-center gap-4">
            <span className="text-[12px] text-white/50">{user?.email}</span>
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
