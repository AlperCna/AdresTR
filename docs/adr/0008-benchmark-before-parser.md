# 0008. Build the benchmark before the parser

- Status: accepted · Date: 2026-10-08

Every parser change must be measurable, and README numbers must be real. Faz 3 builds the
evaluation sets (synthetic, real public-institution addresses, hand-written challenge cases), the
metrics runner and baselines (regex, libpostal, optional LLM) before Faz 4. The test split is never
used for tuning. CI fails a PR that drops dev-set F1 by more than 0.5 points.
