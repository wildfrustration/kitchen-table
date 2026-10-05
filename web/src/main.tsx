import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, Navigate, RouterProvider } from "react-router";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ClientPage } from "./broker/ClientPage";
import { NewClientPage, SettingsPage, SnapshotPage } from "./broker/OtherPages";
import { QueuePage } from "./broker/QueuePage";
import { BrokersPage } from "./broker/BrokersPage";
import { BrokerShell, JoinPage, LoginPage } from "./broker/Shell";
import { DonePage, LinkPage, StartPage } from "./patient/PatientPages";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

const router = createBrowserRouter([
  // Patients
  { path: "/start/:slug", element: <StartPage /> },
  { path: "/i/:token", element: <LinkPage purpose="invite" /> },
  { path: "/r/:token", element: <LinkPage purpose="return" /> },
  { path: "/done", element: <DonePage /> },
  // Brokers
  { path: "/login", element: <LoginPage /> },
  { path: "/join/:token", element: <JoinPage /> },
  { path: "/app/snapshots/:id", element: <SnapshotPage /> },
  {
    path: "/app",
    element: <BrokerShell />,
    children: [
      { index: true, element: <QueuePage /> },
      { path: "clients/new", element: <NewClientPage /> },
      { path: "clients/:id", element: <ClientPage /> },
      { path: "brokers", element: <BrokersPage /> },
      { path: "settings", element: <SettingsPage /> },
    ],
  },
  { path: "*", element: <Navigate to="/app" replace /> },
]);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <RouterProvider router={router} />
        <Toaster position="top-center" richColors />
      </TooltipProvider>
    </QueryClientProvider>
  </StrictMode>,
);
