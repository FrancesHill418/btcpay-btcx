# BTCPay Server Docker generator BTCX overlay

This package uses the official BTCPay Server Docker Compose generator model. It does not edit `Generated/docker-compose.generated.yml` as a source file. The maintained sources are the `btcx.yml` fragment and the `btcx` crypto definition in `crypto-definitions.json`.

The upstream baseline is pinned in `upstream-lock.json`. No private fork repository currently exists. For a maintained production deployment, publish this overlay in an operator-controlled fork or submit it to the official deployment repository and pin the accepted commit. `apply-overlay.sh` adds the BTCX fragment/definition and two small deployment-template patches: PostgreSQL password-file auth instead of the upstream trust setting, and a required explicit BTCPay image reference. Neither patch changes BTCPay Server core. Do not deploy from a mutable upstream branch plus an undocumented local edit.

To test against the pinned upstream checkout, see [production-deployment.md](../../../docs/production-deployment.md). The custom node and electrs images must first be built, published to a controlled registry, and referenced by immutable `tag@sha256:digest` values. This checkout does not claim those images have been published or that their digest is known.
