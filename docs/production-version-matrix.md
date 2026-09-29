# Production candidate version matrix

**Candidate date:** 2026-09-29
**Release candidate:** `v0.1.0-rc3` (annotated local tag; target `a0a0cd78c6410fd3bcf64d996366fa480213402d`, release-preparation candidate; tag is local and not pushed).
**Status:** candidate lock only; not a production approval. The RC2 tag predates the standalone XBoard plugin and is not reusable.

Every requested upstream SHA below was queried from the named GitHub repository's commit endpoint on 2026-09-29 (HTTP 200); source checkouts also fetched the Bitcoin-PoCX, electrs-btcx and bindex-btcx commit objects. No SHA was substituted. The repo commit is verified locally. The exact upstream targets are:

The three requested XBoard architecture commits are ancestors of the frozen `957b8a7` candidate: `91a4a1dd0f461f0b47bd9b70df5c97e693fa3694` (standalone plugin), `1962a1dc8e6c2efc50a806b22967ebfaa3efddc3` (compatibility tests), and `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0` (documentation). The request's `9184a1d` short SHA does not exist in this repository; the actual refactor commit starts `91a4a1d`. `v0.1.0-rc2` peels to `2925af8b19174ad24418529407a4a408cfdf5e59` (annotated tag object `f5d6449453713144d093bc8259f3b0c3e0cd78c2`) and predates these changes, so it is not reused.

| Component | Version/ref | Repository | Commit | Role/status |
|---|---|---|---|---|
| `btcpay-btcx` | candidate `v0.1.0-rc3`; plugin package `0.1.0` | `FrancesHill418/btcpay-btcx` | `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0` | Frozen application/payment source candidate; RC tag also includes release hardening and its final documentation commit (see release manifest). |
| XBoard | pinned commit | `cedar2025/Xboard` | `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Integration/API compatibility target. Original provider remains `BTCPay`. |
| BTCPay Server | `v2.4.4` | `btcpayserver/btcpayserver` | `2d5a0d8077bb33af080e949031da33d84b80638d` | Plugin target and unchanged upstream server base. |
| Bitcoin-PoCX | pinned commit; bundled Bitcoin source v30.2.1 | `PoC-Consortium/bitcoin-pocx` | `005bf0098e217b76a2627bfae458dff4f5718dd5` | Node and wallet RPC. Its `bitcoin` submodule is `b88b852644f629cd5f25b3424d11b462462c24b3`. |
| electrs-btcx | `v0.11.1-btcx.1` | `PoC-Consortium/electrs-btcx` | `2f78c63e20215e20944767f0901209c4d740fe5b` | Electrum service; uses local bindex path dependency. |
| bindex-btcx | pinned commit | `PoC-Consortium/bindex-btcx` | `eda7c70660baa06affef464c7ea1e131c39304f1` | Build dependency embedded in electrs, not a container/service. Parses 286-byte PoCX headers and signature-zeroed hashes. |
| Phoenix PoCX | `v2.4.0` | `PoC-Consortium/phoenix-pocx` | `bc4713306c9c2cd3cbf989a3e355e0705b485218` | Payer-side compatibility reference only; no server container. Real-device E2E remains unverified. |

## Container image locks

Public helper references below were pulled from their registries on 2026-09-29 and the returned repository digests recorded. These are the exact digest-qualified refs in the checked-in generated BTCPay snapshot. They were inspected on Linux amd64; confirm the target platform before promotion.

| Image | Tag | Registry digest | Provenance/status |
|---|---|---|---|
| `nginx` | `1.31.6-trixie` | `sha256:908dc23e643a1447dbfb2e189ed268bfde6a51a5bf9a34d3dd3440a24f58ccf7` | Public image, digest obtained by pull/inspect; generator input at pinned BTCPay docker commit. |
| `btcpayserver/docker-gen` | `0.10.7` | `sha256:52ff0c9478f96956700e0adde8ef9372747cf923d978575bb4996bde63585b7c` | Public image, digest obtained by pull/inspect. |
| `btcpayserver/postgres` | `18.6` | `sha256:4f9632f312190f6fcd580bb71f463637c6eab6f1fc548a3c6460c9c934deafca` | Public image, digest obtained by pull/inspect; used in production Compose. |
| `btcpayserver/letsencrypt-nginx-proxy-companion` | `2.2.9-2` | `sha256:d10a378a5ea7acc24c4ab5a354804fb5d7e1de0789efe063ba669dd482b189c1` | Public image, digest obtained by pull/inspect. |
| Bitcoin-PoCX custom image | `0.1.0-rc3` candidate | **not published; no registry digest** | Local build may be produced; cannot be deployed by digest until an approved registry push. |
| electrs-btcx custom image | `0.1.0-rc3` candidate | **not published; no registry digest** | Local build may be produced; cannot be deployed by digest until an approved registry push. |
| BTCPay wrapper image | `2.4.4-btcx-rc3` candidate | **not published; no registry digest** | Extends the exact BTCPay v2.4.4 base; no BTCPay core changes. |
| BTCPay BTCX `.btcpay` | plugin version `0.1.0` | `sha256:46c357e9c42813f923dcd496dffcdf9ca81fe7b3974e1055d54b930374c38d8b` | Locally produced with `scripts/package-plugin.sh` after locked restore; artifact is gitignored and not published. |

## Release toolchain locks

| Tool | Pinned value | Use |
|---|---|---|
| .NET SDK | `10.0.401` | Plugin restore/build/package and tests; local package artifact is checksum recorded in the release manifest. |
| Rust | `1.85.1` | CI and local tests for the pinned electrs/bindex source. Both Cargo graphs use their committed lockfiles. |
| PHP | `8.3` | XBoard standalone plugin tests, PHPStan and PHP syntax checks. |
| Composer | `2.10.3` | XBoard test dependency install; exact version selected from the [official release download listing](https://getcomposer.org/download/) on 2026-09-29. |
| Buildx | `v0.37.1` | CI/release image build frontend. |
| BuildKit | `moby/buildkit:buildx-stable-1@sha256:28a898719c18a33f4e8000685287fa36fd0dd9560c6440227d3a732d79bb41d8` | Digest-pinned builder, inspected as BuildKit `v0.32.2`. |
| SBOM generator | `docker/scout-sbom-indexer@sha256:4b67f29eb0d1244ab0f62de867ac5dafd7262fcd7ebbdefa6ec8aacd6b15252d` | OCI SPDX SBOM generation; pinned in CI and publish script. |

Base image index digests, Ubuntu/Debian snapshots and patch checksums are recorded in [`image-lock.md`](image-lock.md). The final release build must record the actual pushed registry digests, selected platform, Dockerfile checksum, SBOM, provenance, and signing/verification evidence. A local image ID or local OCI archive digest is not a substitute for the registry manifest digest.

## Bitcoin-PoCX ↔ electrs REST compatibility

At Bitcoin-PoCX `005bf0098e217b76a2627bfae458dff4f5718dd5` / Bitcoin source `b88b852644f629cd5f25b3424d11b462462c24b3`, `/rest/spenttxouts/` exists; bindex requires it. `/rest/blockpart/` is absent. The pinned bindex source fetches and byte-compares the complete genesis block using `/rest/blockpart/<hash>.bin?offset=0&size=<serialized-genesis-size>`, then uses blockpart ranges for indexed transaction reads. Bitcoin Core PR #33657 added the endpoint after this pinned source base.

The canonical local backport pair is in [`patches/electrs-pocx-rest/`](../patches/electrs-pocx-rest/). Both production and staging Docker builds verify each patch SHA, check it applies to the exact submodule, and apply it before compiling. This preserves PoCX consensus behavior, but it remains a compatibility backport rather than an independently approved production dependency. Do not change base SHA or skip patches without a replacement compatibility review.

## Mainnet safety

Production Compose sets `BTCX_ALLOW_MAINNET=false`; no release workflow changes it. The RC tag and version lock authorize no mainnet RPC, wallet, invoice, payment, or real funds.

## Release security finding

The upstream BTCPay 2.4.4 projects pin SourceLink 8.0.0, which originally resolved `Microsoft.Build.Tasks.Git 8.0.0` and triggered GitHub Advisory `GHSA-23fw-v26w-5fgq` / CVE-2026-62900. The release build now applies a build-only package override to `Microsoft.Build.Tasks.Git 10.0.303` from this repository's [`Directory.Build.targets`](../Directory.Build.targets) and locks that version/content hash in the external BTCPay package-lock snapshots. Locked restore/build completed without the NU1902 warning, and the `.btcpay` runtime package does not include the build task. No BTCPay core source is changed and the finding is not suppressed. Re-evaluate the override when changing the BTCPay target. The [GitHub advisory](https://github.com/advisories/GHSA-23fw-v26w-5fgq) lists 10.0.303 as a patched release.
