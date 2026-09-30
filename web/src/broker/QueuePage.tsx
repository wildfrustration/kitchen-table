import { useQuery } from "@tanstack/react-query";
import { CopyIcon, PlusIcon } from "lucide-react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { api, unwrap, type S } from "@/api/client";
import { relativeTime } from "@/lib/format";
import { useMe } from "./Shell";

export const STATUSES: S["ClientStatus"][] = ["New", "Reviewed", "Contacted", "Enrolled", "Closed"];

const SOURCE_LABEL: Record<S["ClientSource"], string> = { PublicLink: "Public link", Invite: "Invite", Broker: "Entered by you" };

export function publicLink(slug: string) {
  return `${window.location.origin}/start/${slug}`;
}

export function QueuePage() {
  const me = useMe();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const status = (params.get("status") ?? "all") as S["ClientStatus"] | "all";
  const clients = useQuery({
    queryKey: ["clients", status],
    queryFn: () => unwrap(api.GET("/api/clients", { params: { query: { status: status === "all" ? undefined : status } } })),
    refetchInterval: 30_000,
  });
  const isAdmin = me.data?.role === "AgencyAdmin";
  const newCount = clients.data?.filter((c) => c.isNew).length ?? 0;

  return (
    <div className="grid gap-5">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Clients</h1>
          <p className="text-sm text-muted-foreground">{newCount > 0 ? `${newCount} new or updated intake${newCount > 1 ? "s" : ""} to review` : "Nothing new to review"}</p>
        </div>
        <div className="flex gap-2">
          {me.data && (
            <Button
              variant="outline"
              onClick={() => {
                navigator.clipboard.writeText(publicLink(me.data!.publicSlug));
                toast.success("Your public intake link is copied.");
              }}
            >
              <CopyIcon /> Copy my public link
            </Button>
          )}
          <Button render={<Link to="/app/clients/new" />}>
            <PlusIcon /> New client
          </Button>
        </div>
      </div>

      <Tabs value={status} onValueChange={(v) => setParams(v === "all" ? {} : { status: v as string })}>
        <TabsList>
          <TabsTrigger value="all">All</TabsTrigger>
          {STATUSES.map((s) => (
            <TabsTrigger key={s} value={s}>
              {s}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <Card className="py-0">
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="pl-4">Client</TableHead>
                <TableHead>ZIP</TableHead>
                <TableHead>Drugs</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Came in through</TableHead>
                {isAdmin && <TableHead>Broker</TableHead>}
                <TableHead className="pr-4">Submitted</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {clients.data?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="py-10 text-center text-muted-foreground">
                    No clients here yet. Share your public link or add a client.
                  </TableCell>
                </TableRow>
              )}
              {clients.data?.map((c) => (
                <TableRow key={c.id} className="cursor-pointer" onClick={() => navigate(`/app/clients/${c.id}`)}>
                  <TableCell className="pl-4 font-medium">
                    <span className="flex items-center gap-2">
                      {c.name}
                      {c.isNew && <Badge>New</Badge>}
                    </span>
                  </TableCell>
                  <TableCell>{c.zip}</TableCell>
                  <TableCell>{c.drugCount}</TableCell>
                  <TableCell>
                    <Badge variant="outline">{c.status}</Badge>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{SOURCE_LABEL[c.source]}</TableCell>
                  {isAdmin && <TableCell className="text-muted-foreground">{c.brokerName}</TableCell>}
                  <TableCell className="pr-4 text-muted-foreground">{relativeTime(c.submittedAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
