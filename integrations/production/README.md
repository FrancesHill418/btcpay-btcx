# Production build inputs

These Dockerfiles and generator fragments are production packaging candidates, not approved production artifacts. Build base images are digest-pinned; apt inputs use pinned dated Ubuntu/Debian snapshots; node, Bitcoin Core submodule, bindex and electrs source revisions are pinned. The two verified compatibility backports are applied to the node source during the build. A thin derived BTCPay image reads PostgreSQL credentials from a Docker secret file and adds curl for HTTP healthcheck; the underlying BTCPay v2.4.4 image/core is unchanged. The `.env.example` uses `example.invalid` registry references intentionally; they are not deployable and must be replaced by published, digest-pinned candidate images.

## Bitcoin-PoCX node

* Bitcoin-PoCX commit: `005bf0098e217b76a2627bfae458dff4f5718dd5`.
* Bundled Bitcoin Core commit: `b88b852644f629cd5f25b3424d11b462462c24b3` (v30.2.1 source).
* Base: Ubuntu `24.04`, OCI index `sha256:008173c23f95b170204355c12626cb5a965d779a7e1283b09e9cffbb1bf33ca3`.
* OS dependencies resolve from Ubuntu snapshot `20260928T000000Z`.
* Required settings: `server=1`, `rest=1`, `txindex=1`; `/rest/chaininfo.json` and the bindex-required `/rest/blockpart/<hash>.bin?offset=0&size=491` are available on the private RPC/REST listener.
* Source patches under [`../../patches/electrs-pocx-rest`](../../patches/electrs-pocx-rest): blockpart backport SHA-256 `9dc06e76a641996fc7831b02f9ba27e39405e926b2a544962070495e2cb1ae21`; v30 net-processing compatibility SHA-256 `bf6e17148c4dc3a7397f310418915a2ccb027c1e4c4f3eff4bbdc60e784055cb`. Both are applied in the Docker build and checked against the pinned source. They alter REST/block-file API compatibility, not PoCX consensus rules. They are not independently production-reviewed or upstream-merged.
* Runtime supports mainnet (default) and regtest for isolated staging. `/data` is persistent; `/run/btcx-rpc` holds the node cookie used by BTCPay's BTCX wallet RPC. electrs receives a separate `rpcauth` identity via `/run/btcx-electrs-rpc/.cookie`, restricted to the RPC methods it needs; `rpcwhitelistdefault=0` leaves the cookie-authenticated BTCPay wallet identity unrestricted. Do not publish RPC/REST or P2P host ports.

## Electrs and bindex

* electrs-btcx commit: `2f78c63e20215e20944767f0901209c4d740fe5b` (`v0.11.1-btcx.1`).
* bindex-btcx commit: `eda7c70660baa06affef464c7ea1e131c39304f1`; electrs Cargo.toml consumes it by local path `../bindex-btcx/bindex-lib`. It is not a separate service/container.
* Base: Debian Trixie/Trixie Slim, pinned OCI index digests `sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c` and `sha256:a99cfc517144bc59b1978475ec53b46ecabec7e43635402ee5b77cc54cd1b20a`; OS dependencies resolve from Debian snapshot `20260928T000000Z`; Rust crate resolution uses committed `Cargo.lock` with `--locked`.
* The selected electrs commit requires a PoCX-aware bindex and Bitcoin REST `/rest/blockpart/` endpoint. It currently calls REST on localhost; the fragment therefore shares the node's network namespace. Mainnet Electrum listens internally on 50001; regtest uses 60401.

## BTCPay runtime wrapper

The [`btcpay/Dockerfile`](btcpay/Dockerfile) extends the exact BTCPay Server v2.4.4 image digest recorded in `docs/image-lock.md`. It does not replace BTCPay code. Its entrypoint reads the Postgres password from a mounted secret file, constructs the database connection string in process memory, and execs the upstream `/app/docker-entrypoint.sh`. The BTCX Compose fragment attaches PostgreSQL and BTCPay only to an internal database network; the node/electrs use a separate internal network.

## Build commands

For a local, non-publishing RC build, use the exact pinned release toolchain and
BuildKit builder. To publish, first configure the approved registry and login
using its credential helper, then use the guarded publisher script:

```sh
BTCX_RELEASE_TAG=v0.1.0-rc3 BTCX_IMAGE_PREFIX='<approved-registry>/<repository>' \
  ./scripts/publish-production-images.sh
```

This script pushes to the explicit registry and therefore is **not run as part
of repository validation**. It refuses dirty trees and non-RC tags, pins the
BuildKit and SBOM generator images, and prints registry manifest digests only
after a successful push. No registry credentials or destination have been
provided. For non-publishing verification, the CI workflow builds OCI archives
with embedded SBOM/provenance attestations.

Generate the BTCPay stack using the official generator and the BTCX fragment. See [BTCPay Docker overlay](btcpayserver-docker/README.md) and [deployment guide](../../docs/production-deployment.md). A clean non-mainnet staging acceptance against the final images remains required.
