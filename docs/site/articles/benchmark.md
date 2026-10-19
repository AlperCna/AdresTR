# Benchmark

The first open Turkish address parsing benchmark. Test splits are never used for tuning; CI fails if dev accuracy drops.

| Test set | AdresTR | libpostal | regex baseline |
|---|---:|---:|---:|
| Synthetic, noisy (2,000), exact match | **98.0 %** | 10.0 % | 14.0 % |
| Real public-institution addresses (1,200), exact match | **95.8 %** | 26.2 % | 8.2 % |
| Hand-written hard cases (167), exact match | **86.8 %** | 24.6 % | 21.0 % |

Exact match = every annotated component is correct. Gold labels separate **parsing** (what the text says) from **resolution**
(what the text determines). Metrics include field-level F1, id accuracy, MRR, calibration error and bootstrap confidence
intervals. Full tables: [eval/results](https://github.com/AlperCna/AdresTR/blob/main/eval/results/README.md); methodology and
format: [eval/README.md](https://github.com/AlperCna/AdresTR/blob/main/eval/README.md).
