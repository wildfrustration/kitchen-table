# Plan Finder validation

Each file in `personas/` is one person — ZIP, drugs, pharmacy, Extra Help — plus the yearly drug cost
Medicare.gov Plan Finder showed for specific plans. `pland validate` re-quotes every persona against the
active data and prints the difference per plan.

## Capturing a persona

1. On medicare.gov/plan-compare, pick the plan year, enter the ZIP, the drugs (same strength, quantity and
   frequency as in the file) and the pharmacy (or mail order). Choose the Extra Help answer.
2. For each plan to check, open "Plan details" → "Drug costs & coverage" and record the **yearly drug cost**
   (not the total with premium).
3. Add the plan (`H1234-001-000`) and that figure under `expected`, with today's date in `capturedOn`.
4. Re-capture after every data release: Plan Finder updates prices more often than the public files.

A difference we can't close with public data (for example mail-order unit prices) gets a `knownGap`
explaining why, and is reported as KNOWN instead of FAIL.

Plan Finder's cost API requires the site's signing key, so capture is manual (see the plan doc).
