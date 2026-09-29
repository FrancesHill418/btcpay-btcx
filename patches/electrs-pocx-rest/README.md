# Bitcoin-PoCX REST compatibility set for electrs-btcx

This is the canonical, shared patch set used by the staging and production Bitcoin-PoCX Docker builds. It is tied to Bitcoin-PoCX commit `005bf0098e217b76a2627bfae458dff4f5718dd5`, whose `bitcoin` submodule is Bitcoin Core-derived commit `b88b852644f629cd5f25b3424d11b462462c24b3` (v30.2.1).

## Why it is required

The pinned bindex-btcx commit `eda7c70660baa06affef464c7ea1e131c39304f1`, embedded by electrs-btcx `2f78c63e20215e20944767f0901209c4d740fe5b`, consumes Bitcoin REST rather than reading block files directly:

* It calls `/rest/spenttxouts/<hash>.bin` (introduced in Bitcoin Core PR #32540 and present in the pinned v30.2.1 source).
* At startup it fetches the full serialized genesis block through `/rest/blockpart/<hash>.bin?offset=0&size=<genesis-block-length>` and checks that the response matches the `/rest/block/` bytes. The pinned source does **not** register this route. It is added by Bitcoin Core PR [#33657](https://github.com/bitcoin/bitcoin/pull/33657), after the pinned base. The regtest genesis fixture used during staging was 491 bytes; bindex computes the request size from the actual genesis block response.
* For indexing, bindex also uses blockpart ranges to fetch transaction bytes. PoCX headers are 286 bytes, not Bitcoin's 80 bytes; bindex's PoCX parser computes the header hash with the trailing 65-byte signature zeroed.

The `blockpart` patch backports the partial block REST handler to the exact older C++ APIs in the Bitcoin submodule. The second patch preserves the older `ReadRawBlock(vector&, pos)` overload still called by that pinned v30 `net_processing.cpp` after the REST implementation is added. Neither patch changes PoCX consensus or validation. These remain local compatibility backports, not upstream releases; production use is blocked on independent source/security review or an approved upstream-supported replacement.

## Reproducibility

Both Dockerfiles fetch the exact Bitcoin-PoCX and Bitcoin submodule commits, verify patch checksums, run `git apply --check`, then apply the two patches in filename order before compilation. The build has no dependency on a developer's modified checkout. See `integrations/production/bitcoin-pocx/Dockerfile` and `integrations/staging/bitcoin-pocx/Dockerfile`.

| Patch | SHA-256 | Base |
|---|---|---|
| `bitcoin-pocx-v30-blockpart-compat.patch` | `9dc06e76a641996fc7831b02f9ba27e39405e926b2a544962070495e2cb1ae21` | `b88b852644f629cd5f25b3424d11b462462c24b3` |
| `bitcoin-pocx-v30-net-processing-compat.patch` | `bf6e17148c4dc3a7397f310418915a2ccb027c1e4c4f3eff4bbdc60e784055cb` | same |

The backport pair was previously compiled and exercised on isolated regtest. That is integration evidence, not a production security review. Recheck upstream for an equivalent fix before changing the base; never silently apply this patch set to another Bitcoin-PoCX/Bitcoin Core revision.
