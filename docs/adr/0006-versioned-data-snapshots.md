# 0006. Versioned data snapshots with stable IDs

- Status: accepted · Date: 2026-10-08

## Context
Administrative data changes: Law 6360 turned ~16k köy into mahalle (2014); NVİ shows ~1%
street churn per half year; new il/ilçe are being discussed.

## Decision
Gazetteer snapshots are versioned `YYYY.MM` and embed source hashes. Entities keep stable IDs
(NVİ `kimlikNo` where available). Renamed or merged units remain as aliases pointing to the
current entity. Users can load a newer snapshot file without upgrading the code.
