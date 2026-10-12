import { Badge } from "@/components/ui/badge";

// Maps platform install/health states to badge variants.
// Accepts backend enum numbers too (APIs serialize enums as numbers).
// Never color-only: the text label always carries the meaning (§10).
export function StatusBadge({ status }: { status: string | number }) {
  const label = String(status ?? "");
  const s = label.toUpperCase();
  switch (s) {
    case "INSTALLED":
    case "ACTIVE":
    case "AVAILABLE":
    case "ENABLED":
    case "HEALTHY":
    case "UP":
      return <Badge variant="success">{label}</Badge>;
    case "DISABLED":
    case "DEPRECATED":
    case "PENDING":
    case "INSTALLING":
    case "PROVISIONING":
    case "UNKNOWN":
      return <Badge variant="warning">{label}</Badge>;
    case "FAILED":
    case "NOT_INSTALLED":
    case "DISABLED_MODULE":
    case "DOWN":
      return <Badge variant="destructive">{label}</Badge>;
    default:
      return <Badge variant="secondary">{label}</Badge>;
  }
}
