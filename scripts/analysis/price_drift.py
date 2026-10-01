"""Run from data/raw/puf/history (folders extracted from the two CMS quarterly zips, plus rxnorm_tty.txt).

How much did drug prices move between what was published during AEP 2025 (Oct 2025, plan year 2025)
and the first 2026 prices (Jan 2026)? Stand-in for a 2026 plan's price: the same plan's 2025 price for the
same drug (RXCUI) when the plan existed in 2025, else the median 2025 price across PDPs."""
import glob, statistics, sys
from collections import defaultdict

OLD, NEW = "SPUF_2025_20251009", "SPUF_2026_20260107"

def one(folder, pattern):
    return [f for f in glob.glob(f"{folder}/*.txt") if pattern in f.lower()][0]

def load(folder):
    plans = {}  # plan key -> formulary id
    with open(one(folder, "plan information"), encoding="cp1252") as f:
        h = f.readline().rstrip("\r\n").split("|"); ix = {c: i for i, c in enumerate(h)}
        for line in f:
            r = line.rstrip("\r\n").split("|")
            if r[0].startswith("S") and r[ix["PLAN_SUPPRESSED_YN"]] != "Y":
                plans[(r[0], r[1], r[2])] = r[ix["FORMULARY_ID"]]
    fids = set(plans.values())
    ndc_rx, tier = {}, {}
    with open(one(folder, "basic drugs formulary")) as f:
        h = f.readline().rstrip("\r\n").split("|"); ix = {c: i for i, c in enumerate(h)}
        for line in f:
            r = line.rstrip("\r\n").split("|")
            if r[0] in fids:
                ndc_rx[r[ix["NDC"]]] = r[ix["RXCUI"]]
                tier[(r[0], r[ix["RXCUI"]])] = r[ix["TIER_LEVEL_VALUE"]]
    price = {}  # (plan key, rxcui) -> unit cost for a 30-day fill
    with open(one(folder, "pricing")) as f:
        h = f.readline().rstrip("\r\n").split("|"); ix = {c: i for i, c in enumerate(h)}
        for line in f:
            if line[0] != "S": continue
            r = line.rstrip("\r\n").split("|")
            if r[ix["DAYS_SUPPLY"]] != "30": continue
            key = (r[0], r[1], r[2])
            rx = ndc_rx.get(r[ix["NDC"]])
            if key in plans and rx: price[(key, rx)] = float(r[ix["UNIT_COST"]])
    return plans, price, tier

tty = {}
for line in open("rxnorm_tty.txt"):
    rx, t, name = line.rstrip("\n").split("|", 2)
    tty[rx] = (t, name)

old_plans, old_price, _ = load(OLD)
new_plans, new_price, new_tier = load(NEW)
old_by_rx = defaultdict(list)
for (k, rx), p in old_price.items(): old_by_rx[rx].append(p)
old_median = {rx: statistics.median(v) for rx, v in old_by_rx.items()}

errors = defaultdict(list)    # kind -> list of stand-in/actual - 1
by_drug = defaultdict(list)   # rxcui -> errors
method = defaultdict(int)
for (k, rx), actual in new_price.items():
    if actual <= 0: continue
    if (k, rx) in old_price: stand, m = old_price[(k, rx)], "same plan"
    elif rx in old_median: stand, m = old_median[rx], "market median"
    else: method["no 2025 price (new drug or strength)"] += 1; continue
    method[m] += 1
    e = stand / actual - 1
    kind = "brand" if tty.get(rx, ("?",))[0] in ("SBD", "BPCK") else "generic"
    errors[kind].append(e); errors["all"].append(e); by_drug[rx].append(e)

def summary(es):
    a = sorted(abs(e) for e in es)
    within = lambda t: sum(1 for x in a if x <= t) / len(a)
    return (f"n={len(a):>7,}  median |error| {statistics.median(a):6.1%}  within 5%: {within(.05):5.1%}  10%: {within(.10):5.1%}"
            f"  25%: {within(.25):5.1%}  stand-in too low (price rose) {sum(1 for e in es if e < -0.01)/len(es):5.1%}"
            f"  too high {sum(1 for e in es if e > 0.01)/len(es):5.1%}")

print(f"2025 PDPs: {len(old_plans)}, 2026 PDPs: {len(new_plans)}, 2026 PDPs that existed in 2025: {len(set(new_plans) & set(old_plans))}")
print("stand-in source:", dict(method))
for kind in ("all", "generic", "brand"): print(f"{kind:8} {summary(errors[kind])}")

drug_med = {rx: statistics.median(v) for rx, v in by_drug.items() if len(v) >= 50}
print("\nBiggest drops 2025→2026 (stand-in would overstate the 2026 price):")
for rx, e in sorted(drug_med.items(), key=lambda x: -x[1])[:12]:
    print(f"  {tty.get(rx, ('?', rx))[1][:70]:70}  2025 price is {e:+.0%} vs 2026")
print("\nBiggest rises 2025→2026 (stand-in would understate the 2026 price):")
for rx, e in sorted(drug_med.items(), key=lambda x: x[1])[:8]:
    print(f"  {tty.get(rx, ('?', rx))[1][:70]:70}  2025 price is {e:+.0%} vs 2026")
