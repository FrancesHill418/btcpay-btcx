# Generated BTCPay Compose snapshot

`docker-compose.generated.yml` and `manifest.json` were emitted by the official BTCPay Server Docker Compose generator at commit `9c8fe127850d079405dbb2db0748611548dc1a42` after applying the tracked BTCX overlay and deployment-template patches. Do not edit these generated files directly. Maintain `integrations/production/btcpayserver-docker/` inputs, then regenerate the stack.

The generator snapshot was passed through `normalize-generated-compose.py` to remove trailing spaces from empty YAML map values; this changes formatting only. Run that normalizer after generation for a clean diff.

The committed snapshot is a packaging fixture, not a deployable production stack: its image registry uses the reserved `example.invalid` domain and must be replaced by real candidate images pinned by immutable manifest digests. It has not been started. Production remains unauthorized.
