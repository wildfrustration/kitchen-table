import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { LogOutIcon, ShieldCheckIcon } from "lucide-react";
import { type ReactNode, useState } from "react";
import { Link, NavLink, Navigate, Outlet, useNavigate, useParams } from "react-router";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { api, ApiError, unwrap } from "@/api/client";
import { Field } from "@/components/Field";
import { Logo, LogoMark } from "@/components/Logo";
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
  const isAdmin = me.data.role === "AgencyAdmin";

  return (
    <div className="min-h-svh bg-muted/30">
      <header className="border-b bg-background print:hidden">
        <div className="mx-auto flex max-w-7xl items-center gap-6 px-6 py-3">
          <Link to="/app">
            <Logo />
          </Link>
          <nav className="flex gap-1">
            <NavLink to="/app" end className={tab}>
              Clients
            </NavLink>
            {isAdmin && (
              <NavLink to="/app/brokers" className={tab}>
                Brokers
              </NavLink>
            )}
            <NavLink to="/app/settings" className={tab}>
              Settings
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="flex items-center gap-1.5 text-muted-foreground">
              <span>{me.data.displayName}</span>
              {isAdmin && (
                // Admins see every client in the agency; the shield says so wherever they are.
                <Tooltip>
                  <TooltipTrigger
                    aria-label="Admin account"
                    className="rounded-sm text-amber-600 outline-none focus-visible:ring-3 focus-visible:ring-ring/50 dark:text-amber-400"
                  >
                    <ShieldCheckIcon className="size-4" />
                  </TooltipTrigger>
                  <TooltipContent>
                    Admin account: you can see every client in {me.data.agency.name} and manage its brokers.
                  </TooltipContent>
                </Tooltip>
              )}
              <span>· {me.data.agency.name}</span>
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

  const loginError = login.error instanceof ApiError && login.error.status === 403 ? login.error.message : "Email or password is wrong.";

  return (
    <AuthBackdrop>
      <Card className="w-full bg-card/85 shadow-xl shadow-primary/10 backdrop-blur">
        <CardHeader>
          <CardTitle className="text-xl">Welcome back</CardTitle>
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
            <Field label="Password" htmlFor="password" error={login.isError ? loginError : undefined}>
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
    </AuthBackdrop>
  );
}

/** The login and join pages: logo teal washing in from the corners behind a single card. */
function AuthBackdrop({ children }: { children: ReactNode }) {
  return (
    <div className="bg-login relative grid min-h-svh place-items-center overflow-hidden p-4">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-[radial-gradient(color-mix(in_oklch,var(--foreground)_14%,transparent)_1px,transparent_1px)] [background-size:22px_22px] [mask-image:radial-gradient(ellipse_at_center,black_25%,transparent_75%)]"
      />
      <div className="relative flex w-full max-w-sm flex-col items-center gap-6">
        <div className="flex flex-col items-center gap-2">
          <LogoMark className="size-12" />
          <span className="text-2xl font-semibold tracking-tight">Kitchen Table</span>
        </div>
        {children}
        <p className="text-sm text-muted-foreground">Medicare Part D quoting for independent brokers.</p>
      </div>
    </div>
  );
}

/** /join/:token — an invited broker sets up their login and lands in the agency. */
export function JoinPage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const invite = useQuery({ queryKey: ["join", token], queryFn: () => unwrap(api.POST("/api/join/lookup", { body: { token } })), retry: false });
  const [name, setName] = useState<string | null>(null);
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const displayName = name ?? invite.data?.displayName ?? "";
  const join = useMutation({
    mutationFn: () => unwrap(api.POST("/api/join", { body: { token, displayName, phone, password } })),
    onSuccess: () => {
      qc.removeQueries({ queryKey: ["me"] });
      toast.success(`Welcome to ${invite.data!.agencyName}. Start by picking the carriers you're appointed with.`);
      navigate("/app/settings");
    },
  });
  const errors = join.error instanceof ApiError ? join.error.fieldErrors : {};
  const spent = invite.isError || (join.error instanceof ApiError && join.error.status === 404);

  if (invite.isLoading) return <AuthBackdrop>{null}</AuthBackdrop>;
  if (spent || !invite.data) {
    return (
      <AuthBackdrop>
        <Card className="w-full bg-card/85 shadow-xl shadow-primary/10 backdrop-blur">
          <CardHeader>
            <CardTitle className="text-xl">This invite link doesn't work any more</CardTitle>
            <CardDescription>Invite links work once, for 7 days. Ask your agency admin to send you a new one.</CardDescription>
          </CardHeader>
          <CardContent>
            <Button variant="outline" render={<Link to="/login" />}>
              Already set up? Sign in
            </Button>
          </CardContent>
        </Card>
      </AuthBackdrop>
    );
  }

  const i = invite.data;
  const admin = i.role === "AgencyAdmin";
  return (
    <AuthBackdrop>
      <Card className="w-full bg-card/85 shadow-xl shadow-primary/10 backdrop-blur">
        <CardHeader>
          <CardTitle className="text-xl">Join {i.agencyName}</CardTitle>
          <CardDescription>
            {i.invitedBy} invited you as {admin ? "an admin" : "a broker"}. Set up your login to get started.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="grid gap-4"
            onSubmit={(e) => {
              e.preventDefault();
              join.mutate();
            }}
          >
            {admin && (
              <p className="flex gap-2 rounded-md border border-amber-200 bg-amber-50 p-2.5 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950/60 dark:text-amber-100">
                <ShieldCheckIcon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
                This is an admin account: you'll see every client in {i.agencyName} and manage its brokers.
              </p>
            )}
            <Field label="Your name" htmlFor="name" hint="Clients see it on your intake page." error={errors.DisplayName?.[0]}>
              <Input id="name" autoComplete="name" value={displayName} onChange={(e) => setName(e.target.value)} />
            </Field>
            <Field label="Email" htmlFor="email" error={errors.Email?.[0]}>
              <Input id="email" type="email" autoComplete="username" value={i.email} readOnly className="bg-muted/50" />
            </Field>
            <Field label="Phone (optional)" htmlFor="phone" hint="Lets clients call you from your intake page.">
              <Input id="phone" type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
            </Field>
            <Field label="Password" htmlFor="password" hint="At least 10 characters." error={errors.Password?.join(" ")}>
              <Input id="password" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </Field>
            {join.isError && Object.keys(errors).length === 0 && <p className="text-sm text-destructive">{join.error.message}</p>}
            <Button type="submit" disabled={join.isPending || !displayName.trim() || !password}>
              {join.isPending ? "Setting up…" : "Create my login"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </AuthBackdrop>
  );
}
