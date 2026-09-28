# BTCX staging release checklist

Use this checklist for an isolated staging release from the `development-complete` baseline. Attach evidence to the operator's private change record; never attach secrets, wallet files, database dumps, or volume contents.

## Source and build

- [ ] Verify source tag `development-complete` points to `57995b3ecc154069c94d06c967cbac2c54ab3324` before release-doc commit, and record the exact release commit used for the build.
- [ ] Verify clean source checkout and initialized BTCPay submodule at v2.4.4 commit `2d5a0d8077bb33af080e949031da33d84b80638d`.
- [ ] Verify `.NET SDK 10.0.401` and record build output.
- [ ] Build/publish the plugin in Release configuration; record artifact checksum and source commit.
- [ ] Verify plugin manifest and DLL are present; review plugin metadata before any public distribution (current description still says “skeleton”).
- [ ] Verify XBoard patch applies only to pinned commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`; record that patch is provider-only.
- [ ] Record pinned Bitcoin-PoCX, bindex-btcx, and electrs-btcx revisions. For staging, record the development `/rest/blockpart` compatibility patch and its checksum.

## Isolation and secrets

- [ ] Confirm this is a dedicated development host and BTCX `regtest`; no production DNS, credentials, wallet, database, or data volumes are attached.
- [ ] Confirm RPC, REST, node P2P, and Electrum ports have no public host bindings and are reachable only over the private Docker backend network.
- [ ] Confirm RPC allow-list is limited to the backend subnet and credentials are injected through the secret manager.
- [ ] Confirm Greenfield token and webhook HMAC secret are distinct, least-privilege, staging-only, and not in source, Compose text, logs, or shell history.
- [ ] Confirm staging volumes are separately named and are not included in a repository archive or release artifact.
- [ ] Scan release artifacts/repository for private keys, wallet seeds, API keys, webhook secrets, `.env`, databases, Docker volumes, and BTCX chain data; investigate findings before distribution.

## Runtime acceptance

- [ ] Verify BTCPay v2.4.4 loads the plugin from the persistent plugin directory and shows `/server/btcx`.
- [ ] Verify node network is `regtest`, wallet `btcx-receive` loads, and RPC auth succeeds over the private network.
- [ ] Verify electrs is synchronized to the regtest node and address history, UTXO, transaction and confirmation queries succeed.
- [ ] Verify `BTCX_CNY` selects `manualbtcx(BTCX_CNY)`; record the test manual rate and UTC timestamp in protected evidence.
- [ ] Create a disposable CNY XBoard order; verify order ID, Greenfield invoice ID, BTCX amount/address/URI, rate snapshot and invoice binding.
- [ ] Send a regtest payment; verify listener detection, transaction ID, required confirmations, `InvoiceSettled`, HMAC validation, HTTP 200 and XBoard paid state.
- [ ] Verify duplicate webhook, underpayment, overpayment, expiry, and binding cases using the provider automated tests; mark live cases separately (they have not all been run live).
- [ ] Record Phoenix status accurately: protocol fixture compatible; real-device QR/send/receive E2E pending.
- [ ] Verify backup/restore procedure in a disposable regtest target and record recovered wallet labels/history.

## Release decision

- [ ] Confirm production remains blocked: mainnet wallet allocation is rejected by this build; no approved production node/indexer compatibility, key-custody design, market-rate policy, recovery certification, or Phoenix device E2E.
- [ ] Do not deploy production, connect BTCX mainnet, or send mainnet funds under this staging checklist.
- [ ] Record operator, date, source/image/artifact checksums, test evidence, exceptions, rollback owner, and staging teardown plan.
