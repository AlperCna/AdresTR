# 0001. Rules + gazetteer + fuzzy matching first; ML later and optional

- Status: accepted · Date: 2026-10-08

## Context
No public labeled Turkish address dataset exists. The best published Turkish address NER
(deprem-ml, BERTurk-128k) reaches macro-F1 0.84 but only ~0.70 on building and door numbers,
which are highly regular and easy for rules. Users need offline, deterministic, explainable output.

## Decision
The MVP parser is rule-based: normalization → tokenization → pattern tagging → keyword lexicon →
gazetteer candidates (exact + scoped fuzzy) → hierarchical resolution → calibrated confidence.
ML (ONNX token classifier) and LLM fallback are optional add-on packages in Faz 9, used only
for low-confidence inputs, and their proposals are always verified against the gazetteer.

## Consequences
Small, dependency-free core; every result explainable via a corrections log. Accuracy on very
noisy inputs is bounded by rules until Faz 9.
