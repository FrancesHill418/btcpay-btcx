# XBoard integration

Production and staging should use the standalone [`BtcpayBtcx` plugin](BtcpayBtcx/README.md), installed at `XBoard/plugins/BtcpayBtcx/`. It registers `BTCPayBTCX` and coexists with the unchanged original `BTCPay` payment provider.

The former patch-based integration modified `plugins-core/Btcpay/Plugin.php`, reused original BTCPay configuration/table names, and registered its BTCX flow under `BTCPay`. It is deprecated and must not be applied to a production XBoard checkout. No patching of XBoard core or the original payment plugin is required for the standalone plugin.

The original patch artifact was removed after migrating its Greenfield, invoice-binding, webhook-registration/delivery, secret-file, and regression-test logic into `BtcpayBtcx`. The standalone plugin has passed its isolated PHPUnit suite and a 2026-09-29 staging regtest payment/webhook E2E with duplicate callback replay. See the plugin README for install, configuration, migration, secrets, quote snapshots, mainnet gate, and test instructions.
