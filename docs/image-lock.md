# Image and source lock record

Captured 2026-09-29 from `docker image inspect`, `docker compose config --images`, and remote OCI manifest inspection. This is a **staging lock snapshot**, not authorization to promote these artifacts. Public base-image values below are multi-platform OCI index digests; custom application values are locally built image IDs exposed by this local Docker daemon as repo digests and are not registry-published artifacts. The custom image IDs do not embed a verifiable source/build-provenance attestation.

## Compose deployment images

| Image | Tag | Digest / image ID | Source commit | Scope / status |
|---|---|---|---|---|
| `postgres` | `16.15-alpine3.24` | `sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea` | docker-library/postgres `9d15534160ade17f2b6c455a39ee967c49b1937d` | Official OCI index; pinned in staging Compose |
| `bitcoin-pocx` | `development-regtest` | `sha256:e84901f03d3d70e89f798235255523201c1be5a9e5756259e54984d7de0e9d99` | Dockerfile build args pin Bitcoin-PoCX `005bf0098e217b76a2627bfae458dff4f5718dd5`, Bitcoin Core `b88b852644f629cd5f25b3424d11b462462c24b3`; not attested in image metadata | Local staged build image; contains the two listed REST compatibility patches |
| `electrs-btcx` | `development-regtest` | `sha256:31959be4c1ccc92aa65487b7800fe0786a4990b1ab33189743a2bad82e3969f2` | Dockerfile args pin bindex-btcx `eda7c70660baa06affef464c7ea1e131c39304f1`, electrs-btcx `2f78c63e20215e20944767f0901209c4d740fe5b`; not attested in image metadata | Local staged build image; not registry-published |
| `btcpay-btcx` | `staging` | `sha256:34381e062bcaf1f733230f7d46dca28b8948acc6a0ce91dfdc4ee5d4e297ec0e` | BTCPay base commit `2d5a0d8077bb33af080e949031da33d84b80638d`; BTCX plugin source commit at image build is unknown/unattested | Local staged build image; not registry-published |

BTCX core/wallet source `PoC-Consortium/btcx` at `v0.1.1`, commit `6907bacb132324e460cbe55d3765b4350bb56e61`, is a pinned reference only; it is **not a runtime/build dependency** of this BTCPay plugin. Wallet RPC is provided by Bitcoin-PoCX.

## Build base images

| Dockerfile stage | Image reference | OCI index digest | Upstream source revision / version |
|---|---|---|---|
| Bitcoin-PoCX builder and runtime | `ubuntu:24.04` | `sha256:008173c23f95b170204355c12626cb5a965d779a7e1283b09e9cffbb1bf33ca3` | Ubuntu OCI revision `4fde35ab880af6a7c67aa9802ea978137542dc35` (linux/amd64 manifest `496754492fb28b4d3049432f2ca787449331e23fb14f0dd3fffea86bf5a93eb4`) |
| electrs builder | `debian:trixie` | `sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c` | debuerreotype source `8f962b15d7884a90e17876a9303cbac909d119aa` |
| electrs runtime | `debian:trixie-slim` | `sha256:a99cfc517144bc59b1978475ec53b46ecabec7e43635402ee5b77cc54cd1b20a` | debuerreotype source `8f962b15d7884a90e17876a9303cbac909d119aa` |
| BTCPay plugin builder | `mcr.microsoft.com/dotnet/sdk:10.0.401` | `sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29` | .NET SDK `10.0.401` |
| BTCPay runtime base | `btcpayserver/btcpayserver:2.4.4` | `sha256:c264aa08cd32a469bd30d41978b73dc8bb2de1503ce67bdb0ab8fd5d934fb614` | BTCPay Server `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` |

These references are pinned as `tag@sha256:digest` in the staging Dockerfiles. `docker image inspect` confirmed the locally cached manifest/image identifiers; registry manifest inspection confirmed the public base OCI index digests. No production image has been built, published or approved.

## Patch lock

| Patch | SHA-256 | Exact base | Purpose |
|---|---|---|---|
| `integrations/electrs-btcx/bitcoin-pocx-v30-blockpart-compat.patch` | `9dc06e76a641996fc7831b02f9ba27e39405e926b2a544962070495e2cb1ae21` | Bitcoin Core source `b88b852644f629cd5f25b3424d11b462462c24b3` | Backport blockpart REST endpoint from Bitcoin Core PR #33657 to the older v30 source API |
| `integrations/electrs-btcx/bitcoin-pocx-v30-net-processing-compat.patch` | `bf6e17148c4dc3a7397f310418915a2ccb027c1e4c4f3eff4bbdc60e784055cb` | Same | Preserve the old `ReadRawBlock(vector&, pos)` overload used by v30 `net_processing.cpp` |
| `integrations/xboard/0001-btcpay-btcx-provider.patch` | `1dd6130b819321851c2bc26664ae0eac380ea0926badb17523bd52d7cbbfd70b` | XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Provider and `/run/secrets`-restricted credential file diff; `git apply --unidiff-zero --check` passed on an independent checkout |

Apply in filename order in a clean source checkout at that exact commit; check hashes first; use `git apply --check` before each patch; then build from the Dockerfile. Never substitute a VPS-edited binary. Both patches affect block-file read/REST compatibility only; staging exercised the patched route. Their correctness and production maintenance are **not independently security-reviewed**.

## Lock gaps and staging image caveat

1. `global.json` now disallows SDK roll-forward. Dockerfiles still run `apt-get update` against live distribution repositories and install unversioned packages; NuGet transitive packages are not locked in a committed plugin lock file. Base images are immutable, but OS/package dependency resolution and reproducible builds remain release blockers.
2. Custom images are local-only. Publish reviewed artifacts to an approved private registry and record registry manifest digest, platform digest, SBOM, provenance and signature before any promotion.
3. At lock capture, `docker image inspect bitcoin-pocx:development-regtest` returned image ID `e849...`, while the already-running staging container image ID was `e63db88573ae3a674440af008db61d3608f35c994fab921558d3005076de2895`. The container was left running. Its original image source/commit cannot be inferred from the current local tag; this drift must be reconciled without replacing the active staging container.
4. Dependency versions/source commits do not constitute a production security review. Mainnet code-path review, wallet custody, XBoard secret mount behavior, Phoenix device E2E and production DR gates remain open.
