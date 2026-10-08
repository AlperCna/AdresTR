# 0009. Privacy by design (KVKK)

- Status: accepted · Date: 2026-10-08

Addresses linked to a person are personal data under KVKK. The library is stateless and sends
nothing anywhere. The hosted API never logs request bodies (only length, latency, confidence),
stores nothing, and documents self-hosting. The playground runs the parser in the browser, so
addresses never leave the device. Any LLM fallback is opt-in and disabled by default.
