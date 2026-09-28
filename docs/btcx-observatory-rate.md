# BTCX Observatory valuation source audit

**Audit date:** 2026-09-27. **Scope:** rate architecture only. No BTCX plugin or other code was implemented.

## Findings

1. The Observatory economy page currently displays **$0.2000–$0.2500 / BTCX**. It explicitly calls this a “valuation band” and says the figures estimate what supply would be worth at that band. It does not describe it as an executable market price.
2. The economy page's chart help says historical market capitalization is the supply that existed at each point valued at **today's** band; it explicitly says this is not a historical price chart. Thus its history is a chain-supply time series revalued at the current estimate, not archived BTCX trades or archived valuation-band observations.
3. The valuation band's provenance is **not published** on the page or in the public Observatory documentation. The page does not name an exchange, market pair, OTC venue, model, input data, administrator, or calculation method. It is therefore not possible to verify that it is market-derived. The site provides no evidence that it is; whether the band is an administrator-set configuration, stored value, or internal model output cannot be determined from the public material.
4. The official [HTTP API documentation](https://observatory.bitcoin-pocx.org/docs/api) describes a read-only JSON API for chain and UTXO monitoring: chain tip/blocks, supply, richlist, and distribution. It does **not** document a valuation, market-price, or economy endpoint. Direct probes of `/api/v1/economy`, `/api/v1/valuation`, and `/api/v1/price` returned 404. The documented `/api/v1/total_supply` endpoint returned a plain-text supply value, not a price. The `app.js` loaded by the economy page only requests `/api/v1/chain/tip`; the valuation band itself is rendered in the HTML and no public JSON response for it was found.
5. No public Observatory source repository was found in the PoC-Consortium public GitHub repository listing or via searches for the site/repository. With no source tree, the band-generation logic cannot be audited as code. The public docs describe only the chain/UTXO API and do not mention a hidden valuation API.
6. The page has no valuation-band observation timestamp or update cadence. The HTTP response time/date is not a quote observation timestamp. Although the page lets the user select a period for its graph, that graph is not historical pricing and there is no disclosed historical valuation-band series.

The page is presently human-readable, but the band is embedded in server-rendered HTML. Parsing that text would be fragile, has no machine-readable schema, timestamp, provenance or contractual stability, and is not an acceptable production integration. Treating the chain API as available does not make the estimate API available.

## Three different kinds of value

| Kind | What it means | Can determine a checkout amount? |
|---|---|---|
| **Executable market price** | A current bid/ask or recent trade for the correct native BTCX asset on an identified venue, with enough depth to cover the intended payment size and a known pair. | Yes, subject to venue, spread, depth, timestamp, and manipulation checks. No such source/pair was verified in the preceding rate audit. |
| **OTC price** | A bilateral firm quote for a specific size/counterparty and validity window. | Only for that quote's size, counterparty, conditions, and TTL. An OTC quote is not a general spot-market rate. |
| **Project valuation estimate** | A model, administrator, or project reference range. It may support discussion or bounds but need not represent a buyer or seller willing to transact. | Not as an automatic payment quote. Observatory describes its band as an estimate and supplies no execution evidence or quote timestamp. |

**$0.20–$0.25/BTCX does not prove that BTCX can be bought or sold at $0.20–$0.25.** Multiplying circulating supply by that range gives estimated capitalization; it does not create order-book liquidity, counterparties, or a firm price.

## Rate hierarchy

### 1. Primary: verified real market quote

Use a documented exchange/market feed for the **native Bitcoin-PoCX BTCX** asset. Verify chain/asset identity, pair, venue API, order-book depth, timestamp, update frequency, status, and commercial-use terms. Prefer direct `BTCX/CNY` if a sufficiently liquid executable market is later verified.

Otherwise use `BTCX/USD` or `BTCX/USDT` only if the native BTCX leg is actually listed and liquid, then compose:

```text
BTCX/USD × USD/CNY = BTCX/CNY
```

or

```text
BTCX/USDT × USDT/CNY = BTCX/CNY
```

The two observations must be timestamp-aligned. Do not assume USDT equals USD, and do not use a displayed indicative currency converter as an executable CNY conversion. Record both legs, source, direction, spread/fee treatment, and timestamps.

At this audit, the primary source is **unavailable**: no native BTCX market pair with verified live liquidity was identified. A missing market leg means no market-derived checkout rate.

### 2. Secondary: Observatory valuation band (reference only)

The current band may be recorded as a **project valuation reference** and used for operator context or as an independent sanity bound after its owner confirms the methodology. It is not a BTCX/USD feed: it lacks a machine-readable endpoint, provenance, observation time, update policy, and historical band record. Do not silently turn the midpoint into the price. If the business chooses to accept an estimate-based quote, that must be a deliberate, visibly disclosed admin action that captures the chosen value and approval; label it “reference estimate,” not “market rate.”

### 3. Fallback: administrator-configured fixed reference

An administrator may set an explicit BTCX/USD reference when no acceptable market quote is available, subject to dual review and audit logging. This is a controlled manual override, not a market fallback. Store the value, currency/pair, operator, approver, reason, set time, hard expiration, and affected invoice/order. Require a maximum 24-hour validity for the override as an initial policy; after expiry, fail closed until renewed. A BTCPay invoice must lock the selected quote and final BTCX due amount at invoice creation; later updates must never change that invoice.

Convert the selected USD amount to CNY using a separately approved and fresh USD/CNY source. Persist the USD/CNY quote as a second leg. If no valid BTCX source or no valid FX leg exists, do not show BTCX as payable.

## Safety policy

- **Quote record:** `{asset_identity, base, quote, bid, ask, source, venue, observedAt, receivedAt, depth, reference_id, conversion_legs, operator/approver}`. Keep market, Observatory estimate, and manual override source types distinct.
- **Market TTL:** start with a maximum quote age of 30 seconds at invoice creation, then calibrate against a verified venue's documented cadence. Reject future times beyond bounded clock skew. Both cross-rate legs must be fresh and within 5 seconds of each other initially.
- **Observatory TTL:** no source observation time exists, so it has no machine-verifiable TTL and must not be auto-consumed. If a human approves a value transcribed from the page, capture the observation time and apply the manual override's hard expiration; re-approval is required for renewal.
- **Manual override TTL:** hard maximum 24 hours initially; when expired, stop new BTCX invoice creation. Do not extend an existing invoice's locked amount.
- **Abnormal price:** require positive finite decimal, correct pair orientation and native-asset identity; reject crossed/wide spreads, inadequate order-book depth, and deviations beyond an explicitly configured threshold against independent same-asset references. Use an initial >10% deviation alert/review guardrail, not as proof of correctness. A valuation band can alert on a discrepancy but cannot certify the market quote.
- **Minimum/maximum:** configure BTCX/USD operational bounds from a reviewed range, plus maximum quote spread and minimum executable depth for the typical order size. The Observatory band alone is not a safe min/max because its source and freshness are unknown. A quote outside approved bounds disables the method and creates an audit event; no silent clamp.
- **Provider failure/unavailable market:** bounded retries before quote deadline; no stale-cache use and no fallback to a different asset. If there is no fresh market and no unexpired, approved manual override, fail closed and leave BTCX unavailable.
- **Audit log:** record quote source and payload hash/response ID, timestamps, exact arithmetic/rounding, USD/CNY leg, sanity-check outcome, override approval, invoice ID, final BTCX base units, and expiry. Never mutate the locked invoice due amount after creation.

## Recommendation

This TASK 01.6.2 recommendation was superseded for the first release by TASK 01.6.3: the selected source is an administrator-managed manual BTCX/CNY rate, not a market quote. The Observatory range remains a valuation reference only and is not used by the plugin. A verified native market pair would be required only if a future release changes to live-market pricing. BTCX checkout is currently blocked for the separate reason that no receiving address or payment monitor exists; see [PROJECT-STATE.md](PROJECT-STATE.md).

Sources: [Observatory economy page](https://observatory.bitcoin-pocx.org/economy), [Observatory docs](https://observatory.bitcoin-pocx.org/docs), [documented HTTP API](https://observatory.bitcoin-pocx.org/docs/api), [PoC-Consortium public repositories](https://github.com/orgs/PoC-Consortium/repositories).
