# XBoard integration boundary

## Responsibility split

| XBoard | BTCPay Server | BTCX plugin / infrastructure |
|---|---|---|
| Create local order and UUID | Create/manage invoice and checkout page | Generate/assign BTCX receive destination |
| Request invoice with fiat amount/currency and order metadata | Lock rates/expiry and expose invoice status | Observe BTCX scripts/transactions and confirmations |
| Redirect customer to BTCPay checkout | Deliver authenticated invoice notifications | Handle mempool, duplicate observations, reorgs and restart recovery |
| Validate callback and match invoice/order | Own payment state and webhook retry | Reconcile against canonical BTCX node |
| Fulfill order idempotently | Return trusted invoice details/status | Keep RPC/indexer and wallet details out of XBoard |

XBoard must not implement BTCX RPC, UTXO lookup, address scanning or confirmation logic.

## API choice: compatibility must be proven

BTCPay documents two integrations: a legacy BitPay-compatible invoice API with limited feature scope, and the Greenfield REST API for newer features. Greenfield documents invoice creation, checkout link, invoice events, and webhook HMAC (`BTCPay-Sig`); BTCPay also documents that BitPay notifications and Greenfield webhooks are separate mechanisms. The public documentation does not establish that a custom BTCX plugin payment method is selectable, serializable and retrievable through every legacy BitPay endpoint on the intended release.

Therefore:

1. If the XBoard BitPay adapter can select a payment method/currency provided by BTCPay and accept the legacy notification contract, run a compatibility spike on the pinned BTCPay version with BTCX plugin loaded.
2. If legacy invoice creation cannot request BTCX or its callback cannot represent the resulting state, use XBoard's BTCPay/Greenfield adapter or add a narrowly scoped XBoard payment-provider adapter. Do not put chain monitoring in that adapter.
3. Do not claim full BitPay compatibility until create, retrieve/status, checkout URL, currency/payment amount, callback signature, expiry, and confirmation event behavior are verified end to end.

## XBoard request/callback contract

At order creation, XBoard stores `order_uuid`, the BTCPay store identity, invoice ID, expected fiat total/currency, and checkout link. It sends the minimum invoice metadata needed for correlation; do not duplicate unnecessary buyer personal data into BTCPay.

On callback, XBoard should:

- verify TLS and the configured BTCPay webhook authentication/signature before parsing;
- validate invoice ID maps to one existing order UUID;
- validate event delivery identity and store it atomically with the order state transition;
- fetch/verify invoice state when callback fields are insufficient;
- validate amount/currency/payment method and only fulfill on the configured terminal state;
- treat duplicate callbacks as successful no-ops;
- return success after durable acceptance so BTCPay retry behavior remains safe;
- support manual review/compensation if a later reorg invalidates a payment after fulfillment.

## Webhook semantics

Greenfield webhook docs describe signed deliveries and events including `InvoiceProcessing`, `InvoiceSettled`, `InvoiceInvalid`, `InvoiceExpired`, and `InvoicePaymentSettled`; exact event delivery/retry behavior must be checked against the deployed BTCPay version. A processing/paid-unconfirmed event must not automatically grant a subscription unless the merchant explicitly chooses zero-confirmation risk. XBoard should not infer confirmation counts from its own blockchain query.

## Integration test acceptance

Use isolated test infrastructure and test credentials to prove: order UUID correlation, BTCX method visibility, amount/currency mapping, checkout redirect, signed event validation, duplicate replay safety, underpayment and split-payment behavior, late payment handling, confirmation threshold, invoice expiration, reorg status reversal, and one-time entitlement activation. Never use production wallet credentials.

## Sources

- [BTCPay custom integrations and BitPay API](https://docs.btcpayserver.org/CustomIntegration/)
- [Greenfield API and webhook schema](https://docs.btcpayserver.org/API/Greenfield/v1/)
- [BTCPay eCommerce integration guide](https://docs.btcpayserver.org/Development/ecommerce-integration-guide/)
- [BTCPay notifications FAQ](https://docs.btcpayserver.org/FAQ/General/)
