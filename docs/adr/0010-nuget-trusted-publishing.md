# 0010. Publish to NuGet with Trusted Publishing

- Status: accepted · Date: 2026-10-08

Packages are published from GitHub Actions via NuGet Trusted Publishing (OIDC, short-lived key)
instead of a stored long-lived API key. Versions come from git tags (`vX.Y.Z`) via MinVer.
