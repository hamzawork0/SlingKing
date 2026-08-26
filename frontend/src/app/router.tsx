import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "./AppShell";
import { NotFound } from "./NotFound";
import { ProtectedRoute } from "./ProtectedRoute";
import { LoginPage } from "../features/auth/LoginPage";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import { ProductsPage } from "../features/catalog/ProductsPage";

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage />, errorElement: <NotFound /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    errorElement: <NotFound />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: "products", element: <ProductsPage /> },
      // Catch-all INSIDE the shell, so an unknown URL keeps the top rail and a way back,
      // and still sends a signed-out visitor to the login screen through ProtectedRoute.
      { path: "*", element: <NotFound /> },
    ],
  },
]);
