# TASK 03.0 — BTCX invoice snapshot timestamp compatibility

The BTCX snapshot parser now accepts `rateTimestamp` as an ISO-8601 date/string or as an integer timestamp in Unix seconds, milliseconds, microseconds, Unix-epoch ticks, or .NET ticks. Integer units are recognized by their magnitude and range-checked. New prompt snapshots write one canonical UTC ISO-8601 round-trip string (`O` format). This keeps old snapshots readable while preventing a Greenfield payment-method retrieval from failing on the previously observed integer token.

The converter is scoped to the BTCX snapshot property; it does not change BTCPay serialization or core behavior. Tests retain and verify all other snapshot fields while reading each supported legacy numeric representation and writing the canonical string. A live PostgreSQL/Greenfield `includePaymentMethods` runtime smoke remains scheduled for the isolated development acceptance phase.
