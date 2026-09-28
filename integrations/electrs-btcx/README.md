# Development compatibility patch for electrs-btcx

At pinned PoCX node commit `005bf0098e217b76a2627bfae458dff4f5718dd5`, the bundled Bitcoin source is v30.2.1 commit `b88b852644f629cd5f25b3424d11b462462c24b3`. It exposes `/rest/spenttxouts/` but does not register `/rest/blockpart/`. Pinned bindex-btcx `eda7c70660baa06affef464c7ea1e131c39304f1` probes `/rest/blockpart/<hash>.bin?offset=0&size=491` during startup and rejects HTTP 404. Pinned electrs-btcx is `2f78c63e20215e20944767f0901209c4d740fe5b`.

`bitcoin-pocx-v30-blockpart-compat.patch` backports Bitcoin Core PR [#33657](https://github.com/bitcoin/bitcoin/pull/33657), which adds partial block reads, and adapts its result type to the v30.2.1 source API (which predates `util::Expected`). The patch only changes block-file reads and the REST interface; it does not change PoCX consensus or validation rules. It was applied to an isolated development source tree and built as `bitcoind`. On loopback regtest, chaininfo, blockhashbyheight, full block, blockpart, and spenttxouts all returned HTTP 200. A separate isolated electrs instance indexed a real wallet payment; Electrum reported it first at mempool height 0 and then at confirmed height 537 with its UTXO and raw transaction available.

The patch is a development compatibility bridge, not an upstream release or production deployment artifact. Re-evaluate it against the eventual supported Bitcoin-PoCX release before use. The official electrs PowerShell testkit could not run because `pwsh` is absent in this environment; equivalent endpoint, Electrum protocol, mempool, transaction, and confirmation checks were run with curl/Python.

To apply against the exact bundled Bitcoin source revision:

```sh
git -C bitcoin apply --check /path/to/bitcoin-pocx-v30-blockpart-compat.patch
git -C bitcoin apply /path/to/bitcoin-pocx-v30-blockpart-compat.patch
```
