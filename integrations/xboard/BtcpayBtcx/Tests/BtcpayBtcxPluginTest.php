<?php

namespace Plugin\BtcpayBtcx\Tests;

use App\Models\Order;
use Illuminate\Container\Container;
use Illuminate\Foundation\Application;
use Illuminate\Database\Capsule\Manager as Capsule;
use Illuminate\Http\Client\Factory as HttpFactory;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Facade;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Schema;
use PHPUnit\Framework\TestCase;
use Plugin\BtcpayBtcx\Plugin;

require_once __DIR__ . '/../Plugin.php';

final class BtcpayBtcxPluginTest extends TestCase
{
    private const ORIGINAL_BTCPAY_PLUGIN_SHA256 = 'b29140a3ea137179d61d4346b9ef15c3628cbec3379cfe49019e6c6c042de028';
    private const ORIGINAL_BTCPAY_CONFIG_SHA256 = 'e29bf5c79e997e690fa5edc8317a09215b654585234b9a2959a0c0f0b36ac0fb';
    private Plugin $plugin;
    private const SECRET = 'development-webhook-secret';
    private const RAW_SETTLED = '{"deliveryId":"delivery-1","webhookId":"hook-1","type":"InvoiceSettled","storeId":"store-1","invoiceId":"invoice-1","manuallyMarked":false,"overPaid":false}';
    private string $secretDirectory;
    private string $apiKeyPath;
    private string $webhookSecretPath;

    protected function setUp(): void
    {
        parent::setUp();
        $this->secretDirectory = sys_get_temp_dir() . '/btcpay-test-secrets-' . bin2hex(random_bytes(8));
        mkdir($this->secretDirectory, 0700);
        $this->apiKeyPath = $this->secretDirectory . '/greenfield-token';
        $this->webhookSecretPath = $this->secretDirectory . '/webhook-hmac';
        file_put_contents($this->apiKeyPath, "development-api-token\n", LOCK_EX);
        file_put_contents($this->webhookSecretPath, self::SECRET . "\n", LOCK_EX);
        chmod($this->apiKeyPath, 0600);
        chmod($this->webhookSecretPath, 0600);
        $app = new Application(dirname(__DIR__, 3));
        $app->detectEnvironment(fn () => 'testing');
        Container::setInstance($app);
        Facade::setFacadeApplication($app);

        $capsule = new Capsule($app);
        $capsule->addConnection(['driver' => 'sqlite', 'database' => ':memory:', 'prefix' => '']);
        $capsule->setAsGlobal();
        $capsule->bootEloquent();
        $app->instance('db', $capsule->getDatabaseManager());
        $app->instance('db.schema', $capsule->schema());
        $app->instance('http', new HttpFactory());
        $app->instance('request', Request::create('/'));

        Schema::create('v2_order', function ($table) {
            $table->increments('id');
            $table->string('trade_no')->unique();
            $table->unsignedBigInteger('total_amount');
            $table->unsignedInteger('status')->default(0);
            $table->integer('created_at')->nullable();
            $table->integer('updated_at')->nullable();
        });
        $migration = require __DIR__ . '/../database/migrations/2026_09_29_000000_create_btcpay_btcx_tables.php';
        $migration->up();

        $this->plugin = new Plugin('btcpay_btcx');
        $this->plugin->setConfig([
            'enabled' => true,
            'uuid' => 'provider-uuid',
            'btcpay_btcx_url' => 'http://127.0.0.1:8080/',
            'btcpay_btcx_store_id' => 'store-1',
            'btcpay_btcx_api_key_file' => $this->apiKeyPath,
            'btcpay_btcx_webhook_key_file' => $this->webhookSecretPath,
            'btcpay_btcx_rate' => '0.20',
            'btcpay_btcx_allow_mainnet' => false,
        ]);
    }

    protected function tearDown(): void
    {
        @unlink($this->apiKeyPath);
        @unlink($this->webhookSecretPath);
        @rmdir($this->secretDirectory);
        Facade::clearResolvedInstances();
        Facade::setFacadeApplication(null);
        Container::setInstance(null);
        parent::tearDown();
    }

    public function test_greenfield_invoice_is_cny_btcx_only_and_bound_before_checkout_is_returned(): void
    {
        Http::fakeSequence()
            ->push([], 200) // list existing store webhooks
            ->push(['id' => 'hook-1'], 200) // register only InvoiceSettled
            ->push(['id' => 'invoice-1', 'checkoutLink' => 'http://127.0.0.1:8080/i/invoice-1'], 200)
            ->push($this->invoice(status: 'New'), 200);

        $result = $this->plugin->pay($this->orderPayload());

        self::assertSame(['type' => 1, 'data' => 'http://127.0.0.1:8080/i/invoice-1'], $result);
        self::assertSame(1234, (int) \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('expected_amount_cents'));
        self::assertSame('order-1', \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('trade_no'));
        self::assertSame('0.20', \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('btc_cny_rate'));
        self::assertSame(6170000000, (int) \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('expected_btcx_atomic_units'));
        self::assertSame('61.7', \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('expected_btcx_amount'));
        Http::assertSent(fn ($request) => $request->method() === 'POST' &&
            str_ends_with($request->url(), '/api/v1/stores/store-1/invoices') &&
            $request['amount'] === '12.34' && $request['currency'] === 'CNY' &&
            $request['metadata']['orderId'] === 'order-1' &&
            $request['checkout']['paymentMethods'] === ['BTCX-CHAIN'] &&
            $request['checkout']['defaultPaymentMethod'] === 'BTCX-CHAIN' &&
            $request['checkout']['speedPolicy'] === 'LowSpeed' &&
            $request['checkout']['paymentTolerance'] === 0);
        Http::assertSent(fn ($request) => $request->method() === 'POST' &&
            str_ends_with($request->url(), '/api/v1/stores/store-1/webhooks') &&
            $request['authorizedEvents']['specificEvents'] === ['InvoiceSettled'] &&
            $request['authorizedEvents']['everything'] === false);
    }

    public function test_secret_file_path_outside_mounted_secret_root_is_rejected_without_echoing_path(): void
    {
        $this->plugin->setConfig([
            'enabled' => true,
            'uuid' => 'provider-uuid',
            'btcpay_btcx_url' => 'http://127.0.0.1:8080/',
            'btcpay_btcx_store_id' => 'store-1',
            'btcpay_btcx_api_key_file' => '/etc/passwd',
            'btcpay_btcx_webhook_key_file' => $this->webhookSecretPath,
        ]);

        try {
            $this->plugin->pay($this->orderPayload());
            self::fail('An arbitrary readable application file must not be accepted as a secret.');
        } catch (\App\Exceptions\ApiException $exception) {
            self::assertSame('Configured Greenfield API token secret file is unavailable.', $exception->getMessage());
            self::assertStringNotContainsString('/etc/passwd', $exception->getMessage());
        }
    }

    public function test_payment_method_is_independent_and_original_btcpay_source_and_contract_are_unchanged(): void
    {
        $basePath = dirname(__DIR__, 3);
        $originalPluginPath = $basePath . '/plugins-core/Btcpay/Plugin.php';
        $originalConfigPath = $basePath . '/plugins-core/Btcpay/config.json';
        if (!is_file($originalPluginPath)) {
            self::markTestSkipped('Run this compatibility test from an XBoard checkout.');
        }
        self::assertSame(self::ORIGINAL_BTCPAY_PLUGIN_SHA256, hash_file('sha256', $originalPluginPath));
        self::assertSame(self::ORIGINAL_BTCPAY_CONFIG_SHA256, hash_file('sha256', $originalConfigPath));
        $legacyConfig = json_decode(file_get_contents($originalConfigPath), true, 512, JSON_THROW_ON_ERROR);
        foreach (['btcpay_url', 'btcpay_storeId', 'btcpay_api_key', 'btcpay_webhook_key'] as $legacyKey) {
            self::assertStringContainsString($legacyKey, file_get_contents($originalPluginPath));
        }
        self::assertSame('btcpay', $legacyConfig['code']);
        require_once $originalPluginPath;

        $original = new \Plugin\Btcpay\Plugin('btcpay');
        $original->boot();
        $this->plugin->boot();
        $methods = \App\Services\Plugin\HookManager::filter('available_payment_methods', []);

        self::assertArrayHasKey('BTCPay', $methods);
        self::assertArrayHasKey('BTCPayBTCX', $methods);
        self::assertSame('btcpay', $methods['BTCPay']['plugin_code']);
        self::assertSame('btcpay_btcx', $methods['BTCPayBTCX']['plugin_code']);
        self::assertSame(['BTCPay', 'BTCPayBTCX'], array_keys($methods));
    }

    public function test_btcpay_btcx_configuration_uses_its_own_namespace_and_defaults_mainnet_off(): void
    {
        $pluginConfig = json_decode(file_get_contents(dirname(__DIR__) . '/config.json'), true, 512, JSON_THROW_ON_ERROR);
        self::assertSame('btcpay_btcx', $pluginConfig['code']);
        self::assertFalse($pluginConfig['config']['btcpay_btcx_allow_mainnet']['default']);
        foreach (['btcpay_btcx_url', 'btcpay_btcx_store_id', 'btcpay_btcx_api_key_file',
                     'btcpay_btcx_webhook_key_file', 'btcpay_btcx_rate', 'btcpay_btcx_allow_mainnet'] as $key) {
            self::assertArrayHasKey($key, $pluginConfig['config']);
        }
        self::assertArrayNotHasKey('btcpay_url', $pluginConfig['config']);
        self::assertArrayNotHasKey('btcpay_api_key', $pluginConfig['config']);
        self::assertArrayNotHasKey('btcpay_webhook_key', $pluginConfig['config']);
    }

    public function test_disabling_btcpay_btcx_leaves_original_btcpay_method_available(): void
    {
        $basePath = dirname(__DIR__, 3);
        $originalPluginPath = $basePath . '/plugins-core/Btcpay/Plugin.php';
        if (!is_file($originalPluginPath)) {
            self::markTestSkipped('Run this compatibility test from an XBoard checkout.');
        }
        require_once $originalPluginPath;
        $original = new \Plugin\Btcpay\Plugin('btcpay');
        $original->boot();
        $this->plugin->setConfig(['enabled' => false]);
        $this->plugin->boot();

        $methods = \App\Services\Plugin\HookManager::filter('available_payment_methods', []);

        self::assertSame(['BTCPay'], array_keys($methods));
        self::assertSame('btcpay', $methods['BTCPay']['plugin_code']);
    }

    public function test_plugin_migration_only_creates_and_drops_its_own_tables(): void
    {
        Schema::create('btcpay_invoice_bindings', fn ($table) => $table->string('invoice_id'));
        Schema::create('btcpay_webhook_registrations', fn ($table) => $table->string('payment_uuid'));
        Schema::create('btcpay_webhook_deliveries', fn ($table) => $table->string('delivery_id'));
        \Illuminate\Support\Facades\DB::table('btcpay_invoice_bindings')->insert(['invoice_id' => 'legacy-invoice']);
        \Illuminate\Support\Facades\DB::table('btcpay_webhook_registrations')->insert(['payment_uuid' => 'legacy-payment']);
        \Illuminate\Support\Facades\DB::table('btcpay_webhook_deliveries')->insert(['delivery_id' => 'legacy-delivery']);

        $migration = require __DIR__ . '/../database/migrations/2026_09_29_000000_create_btcpay_btcx_tables.php';
        $migration->up();
        $migration->up();

        self::assertTrue(Schema::hasTable('btcpay_btcx_invoice_bindings'));
        self::assertTrue(Schema::hasTable('btcpay_btcx_webhook_registrations'));
        self::assertTrue(Schema::hasTable('btcpay_btcx_webhook_deliveries'));
        self::assertContains(
            ['trade_no'],
            array_values(array_map(
                static fn (array $index): array => $index['columns'],
                array_filter(Schema::getIndexes('btcpay_btcx_invoice_bindings'), static fn (array $index): bool => $index['unique'])
            ))
        );

        $migration->down();

        self::assertTrue(Schema::hasTable('btcpay_invoice_bindings'));
        self::assertTrue(Schema::hasTable('btcpay_webhook_registrations'));
        self::assertTrue(Schema::hasTable('btcpay_webhook_deliveries'));
        self::assertSame('legacy-invoice', \Illuminate\Support\Facades\DB::table('btcpay_invoice_bindings')->value('invoice_id'));
        self::assertSame('legacy-payment', \Illuminate\Support\Facades\DB::table('btcpay_webhook_registrations')->value('payment_uuid'));
        self::assertSame('legacy-delivery', \Illuminate\Support\Facades\DB::table('btcpay_webhook_deliveries')->value('delivery_id'));
        self::assertFalse(Schema::hasTable('btcpay_btcx_invoice_bindings'));
        self::assertFalse(Schema::hasTable('btcpay_btcx_webhook_registrations'));
        self::assertFalse(Schema::hasTable('btcpay_btcx_webhook_deliveries'));
    }

    public function test_configured_rate_is_snapshotted_and_existing_invoice_is_not_repriced(): void
    {
        $this->createBinding();
        \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->update([
            'checkout_link' => 'http://127.0.0.1:8080/i/invoice-1',
        ]);
        \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_registrations')->insert([
            'payment_uuid' => 'provider-uuid', 'store_id' => 'store-1',
            'notify_url' => $this->orderPayload()['notify_url'],
            'secret_fingerprint' => hash('sha256', self::SECRET), 'webhook_id' => 'hook-1',
            'created_at' => now(), 'updated_at' => now(),
        ]);
        $config = [
            'enabled' => true, 'uuid' => 'provider-uuid',
            'btcpay_btcx_url' => 'http://127.0.0.1:8080/', 'btcpay_btcx_store_id' => 'store-1',
            'btcpay_btcx_api_key_file' => $this->apiKeyPath,
            'btcpay_btcx_webhook_key_file' => $this->webhookSecretPath,
            'btcpay_btcx_rate' => '0.25', 'btcpay_btcx_allow_mainnet' => false,
        ];
        $this->plugin->setConfig($config);
        Http::fakeSequence()
            ->push(['id' => 'hook-1', 'enabled' => true, 'url' => $this->orderPayload()['notify_url'],
                'authorizedEvents' => ['everything' => false, 'specificEvents' => ['InvoiceSettled']]], 200)
            ->push($this->invoice(status: 'New'), 200);

        $result = $this->plugin->pay($this->orderPayload());

        self::assertSame('http://127.0.0.1:8080/i/invoice-1', $result['data']);
        self::assertSame('0.2', \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('btc_cny_rate'));
        self::assertSame(6170000000, (int) \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('expected_btcx_atomic_units'));
    }

    public function test_duplicate_order_binding_is_rejected_and_wrong_notify_scope_is_rejected(): void
    {
        $this->createBinding();
        try {
            $this->createBinding('invoice-2', 'order-1');
            self::fail('One XBoard order cannot bind multiple BTCX invoices.');
        } catch (\Illuminate\Database\QueryException) {
            self::assertSame(1, \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->count());
        }
        try {
            $this->createBinding('invoice-1', 'order-2');
            self::fail('One invoice cannot bind multiple XBoard orders.');
        } catch (\Illuminate\Database\QueryException) {
            self::assertSame(1, \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->count());
        }
        $this->setWebhookRequest(self::RAW_SETTLED, $this->signature(self::RAW_SETTLED), '/api/v1/guest/payment/notify/BTCPay/provider-uuid');
        self::assertFalse($this->plugin->notify([]));
    }

    public function test_mainnet_invoice_is_disabled_by_default_and_requires_explicit_opt_in(): void
    {
        Http::fakeSequence()
            ->push([], 200)->push(['id' => 'hook-1'], 200)
            ->push(['id' => 'invoice-1', 'checkoutLink' => 'http://127.0.0.1:8080/i/invoice-1'], 200)
            ->push($this->invoice(status: 'New', network: 'main'), 200);

        try {
            $this->plugin->pay($this->orderPayload());
            self::fail('Mainnet is disabled by default.');
        } catch (\App\Exceptions\ApiException $exception) {
            self::assertSame('BTCPay BTCX rate, amount, or network does not match the configured invoice policy.', $exception->getMessage());
        }
    }

    public function test_explicit_mainnet_opt_in_preserves_quote_and_records_network(): void
    {
        $this->plugin->setConfig([
            'enabled' => true, 'uuid' => 'provider-uuid',
            'btcpay_btcx_url' => 'http://127.0.0.1:8080/', 'btcpay_btcx_store_id' => 'store-1',
            'btcpay_btcx_api_key_file' => $this->apiKeyPath,
            'btcpay_btcx_webhook_key_file' => $this->webhookSecretPath,
            'btcpay_btcx_rate' => '0.20', 'btcpay_btcx_allow_mainnet' => 'true',
        ]);
        Http::fakeSequence()
            ->push([], 200)->push(['id' => 'hook-1'], 200)
            ->push(['id' => 'invoice-1', 'checkoutLink' => 'http://127.0.0.1:8080/i/invoice-1'], 200)
            ->push($this->invoice(status: 'New', network: 'main'), 200);

        $this->plugin->pay($this->orderPayload());

        self::assertSame('main', \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->value('network'));
    }

    public function test_production_requires_https_for_notify_and_checkout_links(): void
    {
        app()->detectEnvironment(fn () => 'production');
        $this->plugin->setConfig([
            'enabled' => true, 'uuid' => 'provider-uuid',
            'btcpay_btcx_url' => 'https://btcpay.example/', 'btcpay_btcx_store_id' => 'store-1',
            'btcpay_btcx_api_key_file' => $this->apiKeyPath,
            'btcpay_btcx_webhook_key_file' => $this->webhookSecretPath,
            'btcpay_btcx_rate' => '0.20', 'btcpay_btcx_allow_mainnet' => false,
        ]);
        $order = $this->orderPayload();
        $order['notify_url'] = 'https://xboard.example/api/v1/guest/payment/notify/BTCPayBTCX/provider-uuid';
        Http::fakeSequence()
            ->push([], 200)->push(['id' => 'hook-1'], 200)
            ->push(['id' => 'invoice-1', 'checkoutLink' => 'http://btcpay.example/i/invoice-1'], 200);

        $this->expectException(\App\Exceptions\ApiException::class);
        $this->plugin->pay($order);
    }

    public function test_invalid_and_insecure_checkout_links_are_rejected(): void
    {
        Http::fakeSequence()
            ->push([], 200)->push(['id' => 'hook-1'], 200)
            ->push(['id' => 'invoice-1', 'checkoutLink' => 'https://attacker.example/i/invoice-1'], 200);
        $this->expectException(\App\Exceptions\ApiException::class);
        $this->plugin->pay($this->orderPayload());
    }

    public function test_repeated_order_request_reuses_its_bound_invoice(): void
    {
        $this->createBinding();
        \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->update(['checkout_link' => 'http://127.0.0.1:8080/i/invoice-1']);
        \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_registrations')->insert([
            'payment_uuid' => 'provider-uuid', 'store_id' => 'store-1',
            'notify_url' => $this->orderPayload()['notify_url'],
            'secret_fingerprint' => hash('sha256', self::SECRET), 'webhook_id' => 'hook-1',
            'created_at' => now(), 'updated_at' => now(),
        ]);
        Http::fakeSequence()
            ->push(['id' => 'hook-1', 'enabled' => true, 'url' => $this->orderPayload()['notify_url'],
                'authorizedEvents' => ['everything' => false, 'specificEvents' => ['InvoiceSettled']]], 200)
            ->push($this->invoice(status: 'New'), 200);

        $result = $this->plugin->pay($this->orderPayload());

        self::assertSame('http://127.0.0.1:8080/i/invoice-1', $result['data']);
        Http::assertSentCount(2);
    }

    public function test_webhook_requires_exact_raw_body_hmac_and_retrieved_invoice_match(): void
    {
        $this->createBinding();
        Order::create(['trade_no' => 'order-1', 'total_amount' => 1234, 'status' => 0]);
        Http::fakeSequence()->push($this->invoice(status: 'Settled', paidAmount: 12.34, settledPayment: true), 200);
        $this->setWebhookRequest(self::RAW_SETTLED, $this->signature(self::RAW_SETTLED));

        $verified = $this->plugin->notify([]);

        self::assertSame(['trade_no' => 'order-1', 'callback_no' => 'invoice-1'], $verified);
        self::assertSame(1, \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_deliveries')->count());
    }

    public function test_duplicate_delivery_is_a_noop_and_skips_invoice_validation(): void
    {
        $this->createBinding();
        Order::create(['trade_no' => 'order-1', 'total_amount' => 1234, 'status' => 0]);
        Http::fakeSequence()->push($this->invoice(status: 'Settled', paidAmount: 12.34, settledPayment: true), 200);
        $this->setWebhookRequest(self::RAW_SETTLED, $this->signature(self::RAW_SETTLED));

        self::assertSame(['trade_no' => 'order-1', 'callback_no' => 'invoice-1'], $this->plugin->notify([]));
        self::assertSame(['trade_no' => 'order-1', 'callback_no' => 'invoice-1'], $this->plugin->notify([]));
        self::assertSame(1, \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_deliveries')->count());
        Http::assertSentCount(1);
    }

    public function test_invalid_event_signature_manual_mark_overpay_and_nonsettled_invoice_never_map_to_order(): void
    {
        $this->createBinding();
        Order::create(['trade_no' => 'order-1', 'total_amount' => 1234, 'status' => 0]);
        $this->setWebhookRequest(self::RAW_SETTLED, 'sha256=' . str_repeat('0', 64));
        self::assertFalse($this->plugin->notify([]));

        foreach ([
            ['type' => 'InvoiceProcessing'],
            ['manuallyMarked' => true],
            ['overPaid' => true],
            ['storeId' => 'store-other'],
        ] as $change) {
            $payload = array_merge(json_decode(self::RAW_SETTLED, true, 512, JSON_THROW_ON_ERROR), $change);
            $raw = json_encode($payload, JSON_THROW_ON_ERROR);
            $this->setWebhookRequest($raw, $this->signature($raw));
            self::assertFalse($this->plugin->notify([]));
        }

        Http::fakeSequence()->push($this->invoice(status: 'Processing', paidAmount: 12.34, settledPayment: true), 200);
        $this->setWebhookRequest(self::RAW_SETTLED, $this->signature(self::RAW_SETTLED));
        self::assertFalse($this->plugin->notify([]));
        self::assertSame(0, \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_deliveries')->count());
    }

    public function test_underpayment_overpayment_late_payment_wrong_currency_order_and_method_are_rejected(): void
    {
        $this->createBinding();
        Order::create(['trade_no' => 'order-1', 'total_amount' => 1234, 'status' => 0]);
        $cases = [
            $this->invoice(status: 'Settled', paidAmount: 12.33, settledPayment: true),
            $this->invoice(status: 'Settled', paidAmount: 12.35, settledPayment: true),
            $this->invoice(status: 'Expired', paidAmount: 12.34, settledPayment: true),
            $this->invoice(status: 'Settled', paidAmount: 12.34, additionalStatus: 'PaidLate', settledPayment: true),
            $this->invoice(status: 'Settled', paidAmount: 12.34, currency: 'USD', settledPayment: true),
            $this->invoice(status: 'Settled', paidAmount: 12.34, orderId: 'order-other', settledPayment: true),
            $this->invoice(status: 'Settled', paidAmount: 12.34, paymentMethodId: 'BTC-OnChain', settledPayment: true),
        ];
        $responses = $cases;
        Http::fake(function () use (&$responses) {
            return Http::response(array_shift($responses), 200);
        });
        foreach ($cases as $invoice) {
            $this->setWebhookRequest(self::RAW_SETTLED, $this->signature(self::RAW_SETTLED));
            self::assertFalse($this->plugin->notify([]));
        }
        self::assertSame(0, \Illuminate\Support\Facades\DB::table('btcpay_btcx_webhook_deliveries')->count());
    }

    private function createBinding(string $invoiceId = 'invoice-1', string $tradeNo = 'order-1'): void
    {
        \Illuminate\Support\Facades\DB::table('btcpay_btcx_invoice_bindings')->insert([
            'invoice_id' => $invoiceId, 'store_id' => 'store-1', 'trade_no' => $tradeNo,
            'expected_amount_cents' => 1234, 'currency' => 'CNY', 'payment_method_id' => 'BTCX-CHAIN',
            'btc_cny_rate' => '0.2', 'expected_btcx_atomic_units' => 6170000000,
            'expected_btcx_amount' => '61.7', 'rate_timestamp' => '2026-09-29T00:00:00Z', 'network' => 'regtest',
            'speed_policy' => 'LowSpeed',
            'checkout_link' => 'http://127.0.0.1:8080/i/invoice-1', 'created_at' => now(), 'updated_at' => now(),
        ]);
    }

    private function orderPayload(): array
    {
        return [
            'trade_no' => 'order-1', 'total_amount' => 1234,
            'notify_url' => 'http://127.0.0.1:8081/api/v1/guest/payment/notify/BTCPayBTCX/provider-uuid',
        ];
    }

    private function invoice(string $status, float $paidAmount = 0, string $currency = 'CNY', string $orderId = 'order-1', string $additionalStatus = 'None', bool $settledPayment = false, string $paymentMethodId = 'BTCX-CHAIN', string $network = 'regtest'): array
    {
        return [
            'id' => 'invoice-1', 'storeId' => 'store-1', 'amount' => 12.34, 'paidAmount' => $paidAmount,
            'currency' => $currency, 'status' => $status, 'additionalStatus' => $additionalStatus,
            'metadata' => ['orderId' => $orderId],
            'checkout' => ['paymentMethods' => ['BTCX-CHAIN'], 'speedPolicy' => 'LowSpeed', 'paymentTolerance' => 0],
            'paymentMethods' => [[
                'activated' => true, 'paymentMethodId' => $paymentMethodId,
                'payments' => $settledPayment ? [['status' => 'Settled']] : [],
                'additionalData' => [
                    'fiatAmount' => 12.34, 'fiatCurrency' => $currency,
                    'cryptoAmount' => '61.7', 'cryptoCurrency' => 'BTCX',
                    'exchangeRate' => '0.2', 'rateSource' => 'manual',
                    'rateTimestamp' => '2026-09-29T00:00:00Z',
                    'cryptoAmountAtomicUnits' => 6170000000, 'network' => $network,
                ],
            ]],
        ];
    }

    private function setWebhookRequest(string $body, string $signature, string $path = '/api/v1/guest/payment/notify/BTCPayBTCX/provider-uuid'): void
    {
        $request = Request::create($path, 'POST', [], [], [], [
            'CONTENT_TYPE' => 'application/json', 'HTTP_BTCPAY_SIG' => $signature,
        ], $body);
        app()->instance('request', $request);
    }

    private function signature(string $body): string
    {
        return 'sha256=' . hash_hmac('sha256', $body, self::SECRET);
    }
}
