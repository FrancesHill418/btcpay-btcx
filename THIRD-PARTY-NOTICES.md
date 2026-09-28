# Third-party notices

This repository does not declare an aggregate license for the BTCX plugin and integration work. No public license grant is made for project-authored files. The repository is being prepared as a private project repository; choose a project license separately before making it public.

The following upstream components or source material are relevant to files and patches in this repository. Their license notices are preserved in `THIRD-PARTY-LICENSES/` where source-derived patches are present.

| Component / material | Pinned source | License and notice |
|---|---|---|
| BTCPay Server plugin template | [`ebc4d8891aa5de03cb54edceda566fe5b3110d46`](https://github.com/btcpayserver/btcpayserver-plugin-template/tree/ebc4d8891aa5de03cb54edceda566fe5b3110d46) | MIT; notice at `THIRD-PARTY-LICENSES/BTCPayServer-Plugin-Template-MIT.txt`. |
| XBoard BTCPay provider targeted by the patch | [`4f48e61a2cbc6db5338872b6bdb45ef954ec1256`](https://github.com/cedar2025/Xboard/tree/4f48e61a2cbc6db5338872b6bdb45ef954ec1256) | MIT; notice at `THIRD-PARTY-LICENSES/XBoard-MIT.txt`. Only the provider patch is included here, not the XBoard source tree. |
| Bitcoin Core REST blockpart implementation adapted for PoCX v30 | [Bitcoin-PoCX source commit `b88b852644f629cd5f25b3424d11b462462c24b3`](https://github.com/PoC-Consortium/bitcoin/tree/b88b852644f629cd5f25b3424d11b462462c24b3), [Bitcoin Core PR #33657](https://github.com/bitcoin/bitcoin/pull/33657) | Bitcoin-PoCX identifies its inherited Bitcoin Core license as MIT; the pinned Bitcoin source `COPYING` is preserved at `THIRD-PARTY-LICENSES/Bitcoin-Core-MIT.txt`. The compatibility patch is under `integrations/electrs-btcx/`. |
| BTCPay Server source submodule | [`2d5a0d8077bb33af080e949031da33d84b80638d`](https://github.com/btcpayserver/btcpayserver/tree/2d5a0d8077bb33af080e949031da33d84b80638d) | MIT. This repository records it as a Git submodule; its upstream `LICENSE` remains in the submodule checkout and is not copied or modified here. |

Other BTCX upstream repositories documented in `docs/baseline.md` are referenced for compatibility and validation; their source code is not vendored in this repository. Phoenix parser behavior was checked against its upstream source, but that implementation is not copied here.
