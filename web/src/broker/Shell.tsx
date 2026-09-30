import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { LogOutIcon } from "lucide-react";
import { useState } from "react";
import { Link, NavLink, Navigate, Outlet, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { api, unwrap } from "@/api/client";
import { Field } from "@/components/Field";
import { cn } from "@/lib/utils";

export function useMe() {
  return useQuery({
    queryKey: ["me"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/auth/me");
      return response.ok ? data! : null;
    },
    staleTime: 5 * 60_000,
  });
}

/** Everything under /app needs a broker session. */
export function BrokerShell() {
  const me = useMe();
  const qc = useQueryClient();
  const navigate = useNavigate();

  if (me.isLoading) return <Skeleton className="m-8 h-10 w-64" />;
  if (!me.data) return <Navigate to="/login" replace />;

  const logout = async () => {
    await api.POST("/api/auth/logout");
    qc.clear();
    navigate("/login");
  };

  const tab = ({ isActive }: { isActive: boolean }) =>
    cn("rounded-md px-3 py-1.5 text-sm font-medium", isActive ? "bg-muted text-foreground" : "text-muted-foreground hover:text-foreground");

  return (
    <div className="min-h-svh bg-muted/30">
      <header className="border-b bg-background print:hidden">
        <div className="mx-auto flex max-w-7xl items-center gap-6 px-6 py-3">
          <Link to="/app" className="font-semibold">
            Kitchen Table
          </Link>
          <nav className="flex gap-1">
            <NavLink to="/app" end className={tab}>
              Clients
            </NavLink>
            <NavLink to="/app/settings" className={tab}>
              Settings
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="text-muted-foreground">
              {me.data.displayName} · {me.data.agency.name}
            </span>
            <Button variant="ghost" size="sm" onClick={logout}>
              <LogOutIcon /> Sign out
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-7xl px-6 py-6">
        <Outlet />
      </main>
    </div>
  );
}

export function LoginPage() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [remember, setRemember] = useState(false);
  const login = useMutation({
    mutationFn: () => unwrap(api.POST("/api/auth/login", { body: { email, password, rememberMe: remember } })),
    onSuccess: () => {
      // Drop the cached "not signed in" answer so the workspace asks again with the new cookie.
      qc.removeQueries({ queryKey: ["me"] });
      navigate("/app");
    },
  });

  return (
    <div className="grid min-h-svh place-items-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Kitchen Table</CardTitle>
          <CardDescription>Sign in to your broker workspace.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="grid gap-4"
            onSubmit={(e) => {
              e.preventDefault();
              login.mutate();
            }}
          >
            <Field label="Email" htmlFor="email">
              <Input id="email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />
            </Field>
            <Field label="Password" htmlFor="password" error={login.isError ? "Email or password is wrong." : undefined}>
              <Input id="password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </Field>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={remember} onCheckedChange={setRemember} /> Keep me signed in for 14 days
            </label>
            <Button type="submit" disabled={login.isPending || !email || !password}>
              {login.isPending ? "Signing in…" : "Sign in"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
