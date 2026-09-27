# BTCX/CNY production quote source audit

**Audit date:** 2026-09-27. **Asset identity:** Bitcoin-PoCX native BTCX, not any other asset sharing the ticker. This is a source and availability audit; no rate integration was implemented.

## Decision

There is currently **no verified production quote source for native Bitcoin-PoCX BTCX/CNY**. This audit did not find a verified BTCX/CNY, BTCX/USDT, or BTCX/USD market with a public API and observable executable liquidity. Consequently BTCX payment must remain disabled for real CNY orders until a real, approved source is available. Do not substitute a same-symbol token or invent a reference price.

The Bitcoin-PoCX ecosystem has a peer-to-peer atomic swap application, Satchel. Its public description is a Nostr offer board for negotiated UTXO-coin swaps, not an exchange order book/API or a BTCX/CNY oracle. Offers are not proof of a filled trade or a firm CNY quote. [PoC-Consortium/satchel](https://github.com/PoC-Consortium/satchel)

The Bitcoin-PoCX Observatory now displays a **$0.20–$0.25/BTCX valuation estimate**. Its public docs expose chain/UTXO monitoring JSON, but no documented valuation or price API; the economy page says this is an estimate, has no quote timestamp/provenance, and its graph is not historical market price. It is useful as a project valuation reference only, not as evidence of an executable BTCX/USD or BTCX/CNY market. See [the Observatory-specific audit](btcx-observatory-rate.md).

## Provider candidates

| Provider / API | Pair | Availability for native PoCX BTCX | Update, liquidity, history | Auth, limits, commercial terms | Assessment |
|---|---|---|---|---|---|
| Bitcoin-PoCX official ecosystem | BTCX/CNY, BTCX/USDT, BTCX/USD | No official quote endpoint or published reference-price feed was identified. Satchel's P2P offers are bilateral offers, not a normalized market quote. The Observatory's $0.20–$0.25/BTCX band is explicitly an estimate, with no public provenance or quote API. | No consolidated trades, depth, or historical OHLC quote API found. An offer board does not establish executable depth. Observatory history values historical supply at today's band; it is not a price history. | No quote API contract or service limits to evaluate. | Observatory can be an explicitly labeled secondary valuation reference only, not a production executable rate source. |
| Centralized exchanges | BTCX/CNY, BTCX/USDT, BTCX/USD | No currently operating venue/pair for the native PoCX asset could be verified from the exchange/market API checks in this audit. A ticker-only search is ambiguous and is not pair verification. | With no verified pair, volume, order-book depth, refresh interval, and history are unavailable. | No pair-specific API terms or limits can be evaluated until a venue is identified. | No exchange is recommended or claimed to list native BTCX. Recheck pair status and depth immediately before any future integration. |
| CoinGecko | BTCX/CNY, BTCX/USD | API is real; the `BTCX` asset it returns is **BitcoinX on Solana**, CoinGecko ID `bitcoinx-3`, token address `6iLrk475gh1yUv1tQKSC5nUugAHgxeTUf2GTo1Xvsz5m`. It is not Bitcoin-PoCX. `/api/v3/coins/bitcoin-pocx` returned 404 during this audit. | CoinGecko showed markets and fiat conversion/history for that different Solana token; those figures and liquidity do not apply to PoCX. | Current docs require a key for Demo/Pro API. Limits and quotas are plan-specific and changeable. Commercial use is subject to the selected plan and API terms; obtain the appropriate plan/rights before embedding it in a paid service. [Auth](https://docs.coingecko.com/docs/setting-up-your-api-key), [simple price](https://docs.coingecko.com/reference/simple-price), [pricing](https://www.coingecko.com/en/api/pricing), [API terms](https://www.coingecko.com/en/api_terms). | Reject for native BTCX. Never select by symbol alone; bind to chain/asset identity. |
| GeckoTerminal | Solana BTCX token pools, not native PoCX | REST DEX API is real and its token endpoint returned the same Solana contract above. It has no network representing Bitcoin-PoCX in the checked response. | Pool reserve, recent volume and OHLC endpoints exist for supported DEX pools, but the data belongs to the unrelated Solana token. Thin pools can be manipulated. | Public API is free, beta, asks attribution, and can change; current official page advertises 10 calls/minute for public API and paid CoinGecko Onchain API has higher limits. No API key on the public API. [API status and terms](https://apiguide.geckoterminal.com/), [API](https://www.geckoterminal.com/dex-api). | Reject for native BTCX. A JSON response with `symbol=BTCX` is not enough to establish the correct chain asset. |
| Aggregators / symbol search | BTCX/CNY, BTCX/USD | CoinGecko currently resolves BTCX to the unrelated Solana token; no native PoCX mapping was found. No trustworthy aggregator quote was verified. | Any volume/frequency/history inherited from the wrong asset is irrelevant. | Provider-specific; not reached for the native asset. | Aggregation cannot repair the missing source-market identity. |

### Direct API checks

- CoinGecko search for `BTCX` returned `bitcoinx-3`, named BitcoinX and not Bitcoin-PoCX. The direct `coins/bitcoin-pocx` lookup returned HTTP 404.
- GeckoTerminal `GET /api/v2/networks/solana/tokens/6iLrk475gh1yUv1tQKSC5nUugAHgxeTUf2GTo1Xvsz5m` returned `name=BitcoinX`, `symbol=BTCX`, CoinGecko ID `bitcoinx-3`, and Solana token market fields. That verifies a live API and a different asset only; it does not verify PoCX liquidity.
- Bitcoin-PoCX chain sources/explorer establish a native BTCX asset and chain, but expose chain data rather than a fiat exchange rate. See [Bitcoin-PoCX repository](https://github.com/PoC-Consortium/bitcoin-pocx) and [PoCX explorer](https://explorer.bitcoin-pocx.org/).

## Cross quotes

Neither `BTCX/USDT + USDT/CNY` nor `BTCX/USD + USD/CNY` is currently a usable quote: the first leg for the native PoCX coin is unverified. A real and liquid `USDT/CNY` or `USD/CNY` feed cannot manufacture the missing BTCX value. Even if a native BTCX crypto pair is later listed, the two legs must be timestamp-aligned, explicitly identify whether USDT is treated as USD, include executable bid/ask and fees, and reject stale or dislocated stablecoin conversion. Never assume USDT=USD or an indicative USD/CNY rate equals a merchant's executable conversion.

## Required failure, freshness, and sanity policy

No source means **fail closed**: disable the BTCX payment prompt or reject invoice creation. Do not reuse an old quote, silently switch assets, or create a nominal BTCX amount.

When a verified source is selected, implement these limits as configuration backed by observed source cadence and monitored service objectives; the numbers must be agreed with the venue before production:

- Keep both source `observedAt` and local `receivedAt`, venue, pair, bid, ask, last trade, order-book depth, and source response/reference ID.
- Treat a quote older than **30 seconds** at invoice creation as stale initially; tighten or widen only from documented source update cadence and a risk review. Reject future timestamps beyond bounded clock skew.
- Require positive finite decimal prices, exact pair orientation, non-crossed bid/ask, minimum depth for the intended order size, and a maximum spread. Compare to an independent same-asset reference and a rolling robust median; reject a configurable deviation (initial guardrail: >10%) for manual review. No threshold makes an unverified or shallow venue safe.
- On timeout, HTTP error, rate limit, schema mismatch, missing pair, stale timestamp, inadequate depth, or outlier: do not issue a BTCX invoice; retry with bounded exponential backoff only before the order quote deadline and surface service unavailability.
- Lock the accepted quote and rounded BTCX base-unit amount onto the invoice before returning `checkoutLink`. Later market movements never alter that invoice's due amount. After expiry, require a new invoice and quote; late/partial payments follow a separately documented policy.

## Recommended architecture

Use a **BTCX plugin-owned rate provider** (`IRateProvider`/BTCPay rate-rule integration) only after a real native BTCX market source is selected and licensed. BTCPay's built-in providers do not create a PoCX BTCX price. Prefer direct BTCX/CNY if an actual deep pair becomes available; otherwise a verified BTCX/USDT or BTCX/USD market plus a separately approved CNY conversion may be used with both leg timestamps and spreads recorded. The present result is a blocker, not an invitation to use the Solana listing.

The provider boundary should return `{base, quote, bid, ask, source, pair, observedAt, receivedAt, reference, depth}` and fail closed on stale, missing, malformed, dislocated or illiquid data. BTCPay should persist the selected rate on invoice creation so the BTCX amount is immutable for that invoice's life.
