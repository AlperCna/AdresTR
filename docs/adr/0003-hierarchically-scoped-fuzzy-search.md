# 0003. Hierarchically scoped fuzzy search

- Status: accepted · Date: 2026-10-08

## Context
A naive trigram + OSA search over 1M names measured p50 20 ms / p99 32 ms on .NET 10 — too slow
for a < 1 ms/address target.

## Decision
Exact multi-token matches come from a token trie over folded names. Fuzzy matching (banded,
allocation-free OSA; SymSpell at token level later) runs globally only for il/ilçe and is scoped
to candidate ilçe for mahalle (and to mahalle for streets). Edit-distance budgets grow with
name length.

## Consequences
Fast and predictable; a mahalle typo cannot be fixed when no il/ilçe/postal-code evidence exists —
such cases return low confidence with alternatives.
