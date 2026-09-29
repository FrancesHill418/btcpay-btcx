# Release manifest: v0.1.0-rc3

**Status:** release candidate for build and review only; **NOT READY / NO-GO for production**.
**Tag:** local annotated `v0.1.0-rc3`, targeting final HEAD `b7dbd0aabd6be84ba61a91d57b468213969bf21f`. It has not been pushed.
**Functional source freeze:** `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0`. The tag also includes the release-hardening, CI and operations-documentation commits preceding the tag target.
**Existing `v0.1.0-rc2`:** predates the standalone XBoard plugin and is not reused.

## Source and component locks

| Component | Version/ref | Immutable commit | Notes |
|---|---|---|---|
| btcpay-btcx | `v0.1.0-rc3` candidate; plugin package `0.1.0` | `b7dbd0aabd6be84ba61a91d57b468213969bf21f` tag target; application source freeze `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0` | Local tag only; includes standalone BtcpayBtcx architecture. |
| XBoard | pinned target | `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Standalone plugin installs at `XBoard/plugins/BtcpayBtcx/`, payment method `BTCPayBTCX`; original `BTCPay` remains separate. |
| BTCPay Server | `v2.4.4` | `2d5a0d8077bb33af080e949031da33d84b80638d` | Core unchanged; production wrapper image not yet published. |
| Bitcoin-PoCX | pinned | `005bf0098e217b76a2627bfae458dff4f5718dd5` | Bundled Bitcoin source `b88b852644f629cd5f25b3424d11b462462c24b3`. |
| electrs-btcx | `v0.11.1-btcx.1` | `2f78c63e20215e20944767f0901209c4d740fe5b` | Cargo locked build. |
| bindex-btcx | pinned | `eda7c70660baa06affef464c7ea1e131c39304f1` | Built into electrs; not a separate service. |
| Phoenix PoCX | `v2.4.0` | `bc4713306c9c2cd3cbf989a3e355e0705b485218` | User wallet compatibility reference only; real-device validation remains open. |

The architecture refactor is commit `91a4a1dd0f461f0b47bd9b70df5c97e693fa3694`; compatibility tests are `1962a1dc8e6c2efc50a806b22967ebfaa3efddc3`; XBoard documentation is `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0`. The requested `9184a1d` refactor SHA was not present; the actual commit is `91a4a1d...`.

## Runtime image references

Custom production images are build candidates only and have **not** been built as release artifacts or pushed. There are no real registry manifest digests to record. The image tags below are proposed labels, not available deployment references:

| Image | Proposed tag | Registry digest | SBOM / provenance |
|---|---|---|---|
| `btcx/bitcoin-pocx` | `0.1.0-rc3` | Not published | No release image artifact yet. |
| `btcx/electrs-btcx` | `0.1.0-rc3` | Not published | No release image artifact yet. |
| `btcx/btcpayserver` | `2.4.4-btcx-rc3` | Not published | No release image artifact yet. |

Digest-pinned public helper images already available to the checked-in BTCPay generated snapshot are recorded in [production-version-matrix.md](production-version-matrix.md). They do not substitute for the custom service image publication.

The BTCX plugin package was produced locally by `scripts/package-plugin.sh` after locked restore:

| Artifact | SHA-256 | Publication state |
|---|---|---|
| `BTCPayServer.Plugins.BTCX.btcpay` | `46c357e9c42813f923dcd496dffcdf9ca81fe7b3974e1055d54b930374c38d8b` | Local, gitignored build output; not uploaded. |
| `BTCPayServer.Plugins.BTCX.btcpay.json` | `615cf3c3d112a0a6360412c358c4d21bf61e8552a252137dc9d44365e9f13c64` | Local, gitignored build output; not uploaded. |

These plugin hashes describe the local packaging result and are not a signed release artifact. Rebuild and verify them in the tagged CI run before promotion.

## REST compatibility patches

The production and staging Bitcoin-PoCX Dockerfiles apply these reviewed-as-source but not independently production-approved backports from [`patches/electrs-pocx-rest/`](../patches/electrs-pocx-rest/README.md), against Bitcoin-PoCX `005bf0098e217b76a2627bfae458dff4f5718dd5` and its Bitcoin source submodule `b88b852644f629cd5f25b3424d11b462462c24b3`:

| Patch | SHA-256 | Purpose |
|---|---|---|
| `bitcoin-pocx-v30-blockpart-compat.patch` | `9dc06e76a641996fc7831b02f9ba27e39405e926b2a544962070495e2cb1ae21` | Adds the REST blockpart range behavior required by pinned bindex/electrs. |
| `bitcoin-pocx-v30-net-processing-compat.patch` | `bf6e17148c4dc3a7397f310418915a2ccb027c1e4c4f3eff4bbdc60e784055cb` | Supplies the corresponding v30 REST/network-processing compatibility. |

The Docker builds verify both patch hashes, exact upstream base commits and patch applicability. They do not alter BTCX consensus/payment business logic. Upstream disposition or an independent security review remains an open release gate.

## Test and build evidence

Completed locally against the pinned sources and the repository state through `a0a0cd7`:

| Check | Result |
|---|---|
| XBoard BtcpayBtcx PHPUnit | PASS: 17 tests, 81 assertions. |
| PHPStan and PHP syntax | PASS. |
| .NET build | PASS: 0 errors; 106 existing analyzer warnings. |
| .NET tests | PASS: 114 total, 113 passed, 1 isolated runtime smoke test skipped. |
| bindex-btcx Rust tests | PASS: 7 tests. |
| electrs-btcx Rust tests | PASS: 7 tests. |
| Production Dockerfile Buildx checks | PASS for Bitcoin-PoCX, electrs-btcx and BTCPay wrapper. |
| Production/staging Compose structural checks | PASS; no `up` was run. Production preflight used only synthetic disposable invalid-example values to test validation behavior. |
| OCI attestation verifier | PASS against a tiny synthetic sample archive only; this is not a release image. |
| `git diff --check` | PASS before manifest creation; rerun for final repository state. |

The staging/regtest E2E record in [`staging-deployment.md`](staging-deployment.md) is PASS for CNY 12.34 at 0.20 CNY/BTCX (61.7 BTCX), six confirmations, `InvoiceSettled`, paid XBoard order and duplicate webhook no-op. It is historical staging evidence; it was not rerun against RC3 final images. No mainnet endpoint, real production secret, production host or real funds were used.

The GitHub Actions release workflow has not run for this local-only tag. It is configured to build OCI archives and attach SPDX SBOM plus SLSA v1 provenance after tests pass. **No production image digest, release SBOM, provenance attestation, signature or image vulnerability report has been generated.** Those are outstanding artifacts, not assumed successes.

## Safety and release blockers

- Production Compose keeps `BTCX_ALLOW_MAINNET=false`, exposes no host ports for RPC, electrs or PostgreSQL, and has no real domain, IP, registry or secret values. It is an incomplete template pending approved environment-specific values and a real external ingress network.
- No production host/resources, DNS/TLS/ingress, secret manager, approved registry or registry credentials were supplied.
- No disposable-wallet production backup/restore rehearsal has been performed; wallet custody and recovery remain a hard blocker.
- Custom images need a tagged CI build, SBOM/provenance review, immutable registry digests and a production-like isolated E2E using those final images.
- The REST patches need independent production review/upstream disposition; Phoenix real-device compatibility and operational approvals remain open.
- RC3 is not a stable release and does not authorize production deployment or mainnet payment activation.

**Release decision: NOT READY / NO-GO.**
