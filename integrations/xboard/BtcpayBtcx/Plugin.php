<?php

namespace Plugin\BtcpayBtcx;

use App\Contracts\PaymentInterface;
use App\Exceptions\ApiException;
use App\Models\Order;
use App\Services\Plugin\AbstractPlugin;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;

/** XBoard's Greenfield provider. Keep chain/wallet logic in the BTCPay plugin. */
class Plugin extends AbstractPlugin implements PaymentInterface
{
    private const BTCX_PAYMENT_METHOD = 'BTCX-CHAIN';
    private const CURRENCY = 'CNY';

    public function boot(): void
    {
        $this->filter('available_payment_methods', function ($methods) {
            if ($this->getConfig('enabled', true)) {
                $methods['BTCPayBTCX'] = [
                    'name' => $this->getConfig('display_name', 'BTCPay BTCX'),
                    'icon' => $this->getConfig('icon', '₿'),
                    'plugin_code' => $this->getPluginCode(),
                    'type' => 'plugin'
                ];
            }
            return $methods;
        });
    }

    public function form(): array
    {
        return [
            'btcpay_btcx_url' => [
                'label' => 'BTCPay Greenfield URL',
                'type' => 'string',
                'required' => true,
                'description' => 'HTTPS base URL, for example https://btcpay.example/'
            ],
            'btcpay_btcx_store_id' => [
                'label' => 'BTCPay Store ID',
                'type' => 'string',
                'required' => true
            ],
            'btcpay_btcx_api_key_file' => [
                'label' => 'Greenfield API token secret-file path',
                'type' => 'string',
                'required' => true,
                'description' => 'Absolute path to a read-only Docker secret file; the token value is not stored in XBoard configuration.'
            ],
            'btcpay_btcx_webhook_key_file' => [
                'label' => 'Webhook HMAC secret-file path',
                'type' => 'string',
                'required' => true,
                'description' => 'Absolute path to a separate read-only Docker secret file.'
            ],
            'btcpay_btcx_rate' => [
                'label' => 'BTCX/CNY rate (CNY per BTCX)',
                'type' => 'string',
                'required' => true,
                'description' => 'Manual rate. Existing invoices keep their saved rate and BTCX amount.'
            ],
            'btcpay_btcx_allow_mainnet' => [
                'label' => 'Allow BTCX mainnet invoices',
                'type' => 'boolean',
                'required' => false,
                'default' => false,
                'description' => 'Production requires explicit true; BTCPay BTCX mainnet must also be enabled.'
            ],
        ];
    }

    public function pay($order): array
    {
        $config = $this->validatedConfig();
        $tradeNo = $this->identifier($order['trade_no'] ?? null);
        $amountCents = $this->orderAmountCents($order['total_amount'] ?? null);
        $notifyUrl = $this->validAbsoluteUrl($order['notify_url'] ?? null, 'XBoard notify URL');
        $expectedBtcxAtomicUnits = $this->calculateBtcxAtomicUnits($amountCents, $config['rate_scaled']);

        // The URL is stable for this configured XBoard payment method. Create or
        // update one narrowly scoped Greenfield webhook, never one webhook/order.
        $this->ensureWebhook($config, $notifyUrl);

        $existingBinding = DB::table('btcpay_btcx_invoice_bindings')->where('trade_no', $tradeNo)->first();
        if ($existingBinding) {
            if ((int) $existingBinding->expected_amount_cents !== $amountCents ||
                $existingBinding->store_id !== $config['store_id'] ||
                $existingBinding->currency !== self::CURRENCY ||
                $existingBinding->payment_method_id !== self::BTCX_PAYMENT_METHOD ||
                $existingBinding->speed_policy !== 'LowSpeed') {
                throw new ApiException('Existing BTCPay invoice binding does not match the XBoard order.');
            }
            $existingInvoice = $this->getInvoice($config, $existingBinding->invoice_id);
            if (!$this->invoiceMatchesBinding((array) $existingInvoice, (array) $existingBinding, false) ||
                !is_string($existingBinding->checkout_link) || $existingBinding->checkout_link === '') {
                throw new ApiException('Existing BTCPay invoice binding failed validation.');
            }
            return ['type' => 1, 'data' => $existingBinding->checkout_link];
        }

        $request = [
            'amount' => $this->formatCny($amountCents),
            'currency' => self::CURRENCY,
            'metadata' => ['orderId' => $tradeNo],
            'checkout' => [
                'paymentMethods' => [self::BTCX_PAYMENT_METHOD],
                'defaultPaymentMethod' => self::BTCX_PAYMENT_METHOD,
                // XBoard fulfillment waits for BTCPay's conservative confirmation threshold.
                'speedPolicy' => 'LowSpeed',
                'paymentTolerance' => 0,
            ],
        ];
        $created = $this->api('POST', $config, '/api/v1/stores/' . rawurlencode($config['store_id']) . '/invoices', $request);
        $invoiceId = $this->identifier($created['id'] ?? null);
        $checkoutLink = $this->validCheckoutLink($created['checkoutLink'] ?? null, $config['base_url']);

        // Read back the saved invoice before exposing a checkout link. Bind the
        // immutable XBoard order amount and invoice ID before the customer pays.
        $invoice = $this->getInvoice($config, $invoiceId);
        $snapshot = $this->invoiceSnapshot($invoice);
        if ($snapshot === null || $snapshot['cryptoAmountAtomicUnits'] !== $expectedBtcxAtomicUnits ||
            $this->decimalToScaledInteger($snapshot['exchangeRate'], 8) !== $config['rate_scaled'] ||
            $snapshot['network'] === 'main' && !$config['allow_mainnet']) {
            throw new ApiException('BTCPay BTCX rate, amount, or network does not match the configured invoice policy.');
        }
        $binding = [
            'invoice_id' => $invoiceId,
            'store_id' => $config['store_id'],
            'trade_no' => $tradeNo,
            'expected_amount_cents' => $amountCents,
            'btc_cny_rate' => $config['rate'],
            'expected_btcx_atomic_units' => $expectedBtcxAtomicUnits,
            'expected_btcx_amount' => $this->formatBtcxAtomicUnits($expectedBtcxAtomicUnits),
            'rate_timestamp' => $snapshot['rateTimestamp'],
            'network' => $snapshot['network'],
            'currency' => self::CURRENCY,
            'payment_method_id' => self::BTCX_PAYMENT_METHOD,
            'speed_policy' => 'LowSpeed',
            'checkout_link' => $checkoutLink,
            'created_at' => now(),
            'updated_at' => now(),
        ];
        if (!$this->invoiceMatchesBinding($invoice, $binding, false)) {
            throw new ApiException('BTCPay invoice did not match the requested BTCX order.');
        }

        try {
            DB::table('btcpay_btcx_invoice_bindings')->insert($binding);
        } catch (\Throwable $exception) {
            // Duplicate IDs or storage failure must not return an unbound checkout.
            throw new ApiException('Could not safely bind the BTCPay invoice to the XBoard order.');
        }

        return ['type' => 1, 'data' => $checkoutLink];
    }

    public function notify($params): array|bool
    {
        if (!$this->isExpectedNotifyRoute()) {
            return false;
        }
        $rawBody = request()->getContent();
        $signature = (string) request()->header('BTCPay-Sig', '');
        $secret = $this->readSecretFile($this->getConfig('btcpay_btcx_webhook_key_file'), 'webhook HMAC secret');
        if ($rawBody === '' || $secret === '' || !preg_match('/^sha256=[a-f0-9]{64}$/D', $signature)) {
            return false;
        }
        $expectedSignature = 'sha256=' . hash_hmac('sha256', $rawBody, $secret);
        if (!hash_equals($expectedSignature, $signature)) {
            return false;
        }

        try {
            $event = json_decode($rawBody, true, 512, JSON_THROW_ON_ERROR);
        } catch (\JsonException $exception) {
            return false;
        }
        if (!is_array($event) || ($event['type'] ?? null) !== 'InvoiceSettled' ||
            ($event['manuallyMarked'] ?? null) !== false || ($event['overPaid'] ?? null) !== false) {
            return false;
        }

        $deliveryId = $this->safeIdentifier($event['deliveryId'] ?? null);
        if ($deliveryId === null) {
            return false;
        }
        $originalDeliveryId = $event['originalDeliveryId'] ?? null;
        $eventIdentity = $deliveryId;
        if (is_string($originalDeliveryId) && $originalDeliveryId !== '') {
            $eventIdentity = $this->safeIdentifier($originalDeliveryId);
            if ($eventIdentity === null) {
                return false;
            }
        }
        $invoiceId = $this->safeIdentifier($event['invoiceId'] ?? null);
        if ($invoiceId === null) {
            return false;
        }
        $priorDelivery = DB::table('btcpay_btcx_webhook_deliveries')->where('delivery_id', $eventIdentity)->first();
        if ($priorDelivery) {
            $priorBinding = DB::table('btcpay_btcx_invoice_bindings')->where('invoice_id', $invoiceId)->first();
            if ($priorDelivery->invoice_id !== $invoiceId || !$priorBinding ||
                $priorBinding->trade_no !== $priorDelivery->trade_no ||
                $priorBinding->store_id !== ($event['storeId'] ?? null)) {
                return false;
            }
            return ['trade_no' => $priorBinding->trade_no, 'callback_no' => $invoiceId];
        }
        $config = $this->validatedConfig();
        if (($event['storeId'] ?? null) !== $config['store_id']) {
            return false;
        }
        $binding = DB::table('btcpay_btcx_invoice_bindings')->where('invoice_id', $invoiceId)->first();
        if (!$binding || $binding->store_id !== $config['store_id'] ||
            $binding->currency !== self::CURRENCY || $binding->payment_method_id !== self::BTCX_PAYMENT_METHOD ||
            $binding->speed_policy !== 'LowSpeed') {
            return false;
        }
        if ($binding->network === 'main' && !$config['allow_mainnet']) {
            return false;
        }

        $invoice = $this->getInvoice($config, $invoiceId);
        if (!$this->invoiceMatchesBinding((array) $invoice, (array) $binding, true)) {
            return false;
        }
        $order = Order::where('trade_no', $binding->trade_no)->first();
        if (!$order || (int) $order->total_amount !== (int) $binding->expected_amount_cents) {
            return false;
        }

        $inserted = DB::table('btcpay_btcx_webhook_deliveries')->insertOrIgnore([
            'delivery_id' => $eventIdentity,
            'invoice_id' => $invoiceId,
            'trade_no' => $binding->trade_no,
            'event_type' => 'InvoiceSettled',
            'received_at' => now(),
        ]);
        if ($inserted !== 1) {
            $stored = DB::table('btcpay_btcx_webhook_deliveries')->where('delivery_id', $eventIdentity)->first();
            if (!$stored || $stored->invoice_id !== $invoiceId || $stored->trade_no !== $binding->trade_no) {
                return false;
            }
        }
        // XBoard's PaymentController calls OrderService::paid only while an
        // order is pending; repeats safely return success without re-paying it.
        // Return the same mapping on duplicate delivery so BTCPay gets a 2xx.

        return ['trade_no' => $binding->trade_no, 'callback_no' => $invoiceId];
    }

    private function validatedConfig(): array
    {
        $baseUrl = $this->validAbsoluteUrl($this->getConfig('btcpay_btcx_url'), 'BTCPay URL');
        $parts = parse_url($baseUrl);
        if (!in_array(strtolower($parts['scheme'] ?? ''), ['https', 'http'], true) ||
            isset($parts['user']) || isset($parts['pass']) || isset($parts['query']) || isset($parts['fragment'])) {
            throw new ApiException('BTCPay URL must be an absolute HTTPS base URL.');
        }
        if (strtolower($parts['scheme']) !== 'https' && !$this->isDevelopmentHost($parts['host'] ?? '')) {
            throw new ApiException('Plain HTTP is allowed only for a loopback/private development host.');
        }
        if (app()->environment('production') && strtolower($parts['scheme']) !== 'https') {
            throw new ApiException('Production URLs must use HTTPS.');
        }
        $storeId = $this->identifier($this->getConfig('btcpay_btcx_store_id'));
        $apiKey = $this->readSecretFile($this->getConfig('btcpay_btcx_api_key_file'), 'Greenfield API token');
        $webhookKey = $this->readSecretFile($this->getConfig('btcpay_btcx_webhook_key_file'), 'webhook HMAC secret');
        $rate = $this->decimalString($this->getConfig('btcpay_btcx_rate'), 8, 'BTCX/CNY rate');
        $rateScaled = $this->decimalToScaledInteger($rate, 8);
        if ($rateScaled <= 0) {
            throw new ApiException('BTCX/CNY rate must be positive.');
        }
        $allowMainnet = $this->configuredMainnetAllowed();
        return [
            'base_url' => rtrim($baseUrl, '/') . '/',
            'store_id' => $storeId,
            'api_key' => $apiKey,
            'webhook_key' => $webhookKey,
            'rate' => $rate,
            'rate_scaled' => $rateScaled,
            'allow_mainnet' => $allowMainnet,
            'payment_uuid' => $this->identifier($this->getConfig('uuid')),
        ];
    }

    private function ensureWebhook(array $config, string $notifyUrl): void
    {
        $row = [
            'payment_uuid' => $config['payment_uuid'],
            'store_id' => $config['store_id'],
            'notify_url' => $notifyUrl,
            'secret_fingerprint' => hash('sha256', $config['webhook_key']),
            'webhook_id' => '',
            'created_at' => now(),
            'updated_at' => now(),
        ];
        DB::table('btcpay_btcx_webhook_registrations')->insertOrIgnore($row);

        DB::transaction(function () use ($config, $notifyUrl) {
            $registration = DB::table('btcpay_btcx_webhook_registrations')
                ->where('payment_uuid', $config['payment_uuid'])->lockForUpdate()->first();
            if ($registration && $registration->store_id === $config['store_id'] &&
                $registration->notify_url === $notifyUrl &&
                hash_equals($registration->secret_fingerprint, hash('sha256', $config['webhook_key'])) &&
                $registration->webhook_id !== '') {
                $registered = $this->api('GET', $config, '/api/v1/stores/' . rawurlencode($config['store_id']) .
                    '/webhooks/' . rawurlencode($registration->webhook_id));
                if (($registered['enabled'] ?? false) === true && ($registered['url'] ?? null) === $notifyUrl &&
                    ($registered['authorizedEvents']['everything'] ?? true) === false &&
                    ($registered['authorizedEvents']['specificEvents'] ?? []) === ['InvoiceSettled']) {
                    return;
                }
            }

            $path = '/api/v1/stores/' . rawurlencode($config['store_id']) . '/webhooks';
            $existing = $this->api('GET', $config, $path);
            $webhookId = null;
            foreach ($existing as $webhook) {
                if (is_array($webhook) && ($webhook['url'] ?? null) === $notifyUrl) {
                    $webhookId = $this->identifier($webhook['id'] ?? null);
                    break;
                }
            }
            $payload = [
                'url' => $notifyUrl,
                'secret' => $config['webhook_key'],
                'enabled' => true,
                'automaticRedelivery' => true,
                'authorizedEvents' => ['everything' => false, 'specificEvents' => ['InvoiceSettled']],
            ];
            $response = $webhookId === null
                ? $this->api('POST', $config, $path, $payload)
                : $this->api('PUT', $config, $path . '/' . rawurlencode($webhookId), $payload);
            $webhookId = $this->identifier($response['id'] ?? $webhookId);

            DB::table('btcpay_btcx_webhook_registrations')->where('payment_uuid', $config['payment_uuid'])->update([
                'store_id' => $config['store_id'],
                'notify_url' => $notifyUrl,
                'secret_fingerprint' => hash('sha256', $config['webhook_key']),
                'webhook_id' => $webhookId,
                'updated_at' => now(),
            ]);
        });
    }

    /** Read a mounted secret without ever logging its value or path. */
    private function readSecretFile(mixed $path, string $label): string
    {
        if (!is_string($path) || $path === '' || $path[0] !== '/') {
            throw new ApiException('Configured ' . $label . ' secret file is unavailable.');
        }
        $resolvedPath = realpath($path);
        $secretRoot = realpath('/run/secrets');
        $mountedSecret = $resolvedPath !== false && $secretRoot !== false &&
            str_starts_with($resolvedPath, $secretRoot . DIRECTORY_SEPARATOR);
        $testSecret = $resolvedPath !== false && app()->environment('testing') &&
            str_starts_with($resolvedPath, realpath(sys_get_temp_dir()) . DIRECTORY_SEPARATOR);
        if ((!$mountedSecret && !$testSecret) || !is_file($resolvedPath) || !is_readable($resolvedPath)) {
            throw new ApiException('Configured ' . $label . ' secret file is unavailable.');
        }
        $value = @file_get_contents($resolvedPath);
        if (!is_string($value)) {
            throw new ApiException('Configured ' . $label . ' secret file is unavailable.');
        }
        $value = rtrim($value, "\r\n");
        if ($value === '') {
            throw new ApiException('Configured ' . $label . ' secret file is empty.');
        }
        return $value;
    }

    private function getInvoice(array $config, string $invoiceId): array
    {
        return $this->api('GET', $config,
            '/api/v1/stores/' . rawurlencode($config['store_id']) . '/invoices/' . rawurlencode($invoiceId) . '?includePaymentMethods=true');
    }

    private function api(string $method, array $config, string $path, ?array $body = null): array
    {
        $request = Http::timeout(12)->connectTimeout(5)->acceptJson()
            ->withHeaders(['Authorization' => 'token ' . $config['api_key']]);
        $url = $config['base_url'] . ltrim($path, '/');
        $response = match (strtoupper($method)) {
            'GET' => $request->get($url),
            'POST' => $request->post($url, $body ?? []),
            'PUT' => $request->put($url, $body ?? []),
            default => throw new ApiException('Unsupported BTCPay API operation.'),
        };
        if (!$response->successful()) {
            throw new ApiException('BTCPay Greenfield request failed. Check the development endpoint and API token permissions.');
        }
        $decoded = $response->json();
        if (!is_array($decoded)) {
            throw new ApiException('BTCPay Greenfield returned an invalid response.');
        }
        return $decoded;
    }

    private function invoiceMatchesBinding(array $invoice, array $binding, bool $requireSettled): bool
    {
        if (($invoice['id'] ?? null) !== $binding['invoice_id'] ||
            ($invoice['storeId'] ?? null) !== $binding['store_id'] ||
            ($invoice['currency'] ?? null) !== self::CURRENCY ||
            $this->amountCents($invoice['amount'] ?? null) !== (int) $binding['expected_amount_cents'] ||
            ($invoice['metadata']['orderId'] ?? null) !== $binding['trade_no']) {
            return false;
        }
        $methods = $invoice['checkout']['paymentMethods'] ?? null;
        if (!is_array($methods) || $methods !== [self::BTCX_PAYMENT_METHOD] ||
            ($invoice['checkout']['speedPolicy'] ?? null) !== 'LowSpeed' ||
            (float) ($invoice['checkout']['paymentTolerance'] ?? -1) !== 0.0) {
            return false;
        }
        $snapshot = $this->invoiceSnapshot($invoice);
        if ($snapshot === null || $snapshot['fiatCurrency'] !== self::CURRENCY ||
            $this->amountCents($snapshot['fiatAmount']) !== (int) $binding['expected_amount_cents'] ||
            $snapshot['cryptoAmountAtomicUnits'] !== (int) $binding['expected_btcx_atomic_units'] ||
            $this->decimalToScaledInteger($snapshot['cryptoAmount'], 8) !== (int) $binding['expected_btcx_atomic_units'] ||
            $this->decimalToScaledInteger($snapshot['exchangeRate'], 8) !==
                $this->decimalToScaledInteger((string) $binding['btc_cny_rate'], 8) ||
            $snapshot['network'] !== $binding['network']) {
            return false;
        }
        if ($snapshot['network'] === 'main' && !$this->configuredMainnetAllowed()) {
            return false;
        }
        if (!$requireSettled) {
            return true;
        }
        if (($invoice['status'] ?? null) !== 'Settled' ||
            !in_array($invoice['additionalStatus'] ?? null, [null, '', 'None'], true) ||
            $this->amountCents($invoice['paidAmount'] ?? null) !== (int) $binding['expected_amount_cents']) {
            return false;
        }
        foreach (($invoice['paymentMethods'] ?? []) as $paymentMethod) {
            if (($paymentMethod['paymentMethodId'] ?? null) !== self::BTCX_PAYMENT_METHOD ||
                ($paymentMethod['activated'] ?? false) !== true) {
                continue;
            }
            foreach (($paymentMethod['payments'] ?? []) as $payment) {
                if (($payment['status'] ?? null) === 'Settled') {
                    return true;
                }
            }
        }
        return false;
    }

    private function invoiceSnapshot(array $invoice): ?array
    {
        foreach (($invoice['paymentMethods'] ?? []) as $paymentMethod) {
            if (($paymentMethod['paymentMethodId'] ?? null) !== self::BTCX_PAYMENT_METHOD ||
                ($paymentMethod['activated'] ?? false) !== true) {
                continue;
            }
            $data = $paymentMethod['additionalData'] ?? null;
            if (!is_array($data) || !is_string($data['rateTimestamp'] ?? null) ||
                !is_string($data['network'] ?? null) ||
                !in_array($data['network'], ['main', 'test', 'regtest'], true)) {
                return null;
            }
            $rate = $this->decimalString($data['exchangeRate'] ?? null, 8, 'BTCX invoice rate');
            $amount = $this->decimalString($data['cryptoAmount'] ?? null, 8, 'BTCX invoice amount');
            $atomic = $data['cryptoAmountAtomicUnits'] ?? null;
            if (!is_int($atomic) && !(is_string($atomic) && ctype_digit($atomic))) {
                return null;
            }
            return [
                'fiatAmount' => $data['fiatAmount'] ?? null,
                'fiatCurrency' => $data['fiatCurrency'] ?? null,
                'cryptoAmount' => $amount,
                'cryptoAmountAtomicUnits' => (int) $atomic,
                'exchangeRate' => $rate,
                'rateTimestamp' => $data['rateTimestamp'],
                'network' => $data['network'],
            ];
        }
        return null;
    }

    private function calculateBtcxAtomicUnits(int $amountCents, int $rateScaled): int
    {
        // (CNY cents / 100) / (rateScaled / 1e8) * 1e8, rounded to atoms.
        $numerator = (string) $amountCents . str_repeat('0', 14);
        $quotient = '';
        $remainder = 0;
        foreach (str_split($numerator) as $digit) {
            $current = $remainder * 10 + (int) $digit;
            $quotient .= (string) intdiv($current, $rateScaled);
            $remainder = $current % $rateScaled;
        }
        $quotient = ltrim($quotient, '0') ?: '0';
        if ($remainder * 2 >= $rateScaled) {
            $quotient = $this->incrementDecimalInteger($quotient);
        }
        $max = (string) PHP_INT_MAX;
        if (strlen($quotient) > strlen($max) ||
            (strlen($quotient) === strlen($max) && strcmp($quotient, $max) > 0)) {
            throw new ApiException('BTCX invoice amount exceeds supported atomic-unit bounds.');
        }
        $units = (int) $quotient;
        if ($units <= 0) {
            throw new ApiException('BTCX invoice amount is below one atomic unit.');
        }
        return $units;
    }

    private function incrementDecimalInteger(string $value): string
    {
        for ($index = strlen($value) - 1; $index >= 0; $index--) {
            if ($value[$index] !== '9') {
                $value[$index] = (string) ((int) $value[$index] + 1);
                return $value;
            }
            $value[$index] = '0';
        }
        return '1' . $value;
    }

    private function formatBtcxAtomicUnits(int $units): string
    {
        $fraction = rtrim(str_pad((string) ($units % 100_000_000), 8, '0', STR_PAD_LEFT), '0');
        return intdiv($units, 100_000_000) . ($fraction === '' ? '' : '.' . $fraction);
    }

    private function decimalString(mixed $value, int $scale, string $label): string
    {
        if (!is_string($value) && !is_int($value) && !is_float($value)) {
            throw new ApiException($label . ' is invalid.');
        }
        $text = is_float($value) ? rtrim(rtrim(number_format($value, $scale, '.', ''), '0'), '.') : (string) $value;
        if (!preg_match('/^(0|[1-9][0-9]*)(?:\.([0-9]{1,' . $scale . '}))?$/D', $text)) {
            throw new ApiException($label . ' is invalid.');
        }
        return $text;
    }

    private function decimalToScaledInteger(string $value, int $scale): int
    {
        [$whole, $fraction] = array_pad(explode('.', $value, 2), 2, '');
        if (strlen($whole) > 9) {
            throw new ApiException('BTCX/CNY rate is outside supported bounds.');
        }
        return ((int) $whole * (10 ** $scale)) + (int) str_pad($fraction, $scale, '0');
    }

    private function configuredMainnetAllowed(): bool
    {
        return filter_var($this->getConfig('btcpay_btcx_allow_mainnet', false), FILTER_VALIDATE_BOOLEAN);
    }

    private function isExpectedNotifyRoute(): bool
    {
        $path = request()->path();
        $uuid = preg_quote((string) $this->getConfig('uuid'), '#');
        return preg_match('#(?:^|/)payment/notify/BTCPayBTCX/' . $uuid . '$#D', $path) === 1;
    }

    private function orderAmountCents(mixed $amount): int
    {
        if (!is_int($amount) || $amount <= 0 || $amount > 2_000_000_000) {
            throw new ApiException('XBoard order amount must be a positive integer number of cents.');
        }
        return $amount;
    }

    private function amountCents(mixed $amount): ?int
    {
        if (!is_int($amount) && !is_float($amount) && !is_string($amount)) {
            return null;
        }
        $text = is_float($amount) ? number_format($amount, 2, '.', '') : (string) $amount;
        if (!preg_match('/^(0|[1-9][0-9]*)(?:\.([0-9]{1,2}))?$/D', $text, $matches)) {
            return null;
        }
        $fraction = str_pad($matches[2] ?? '', 2, '0');
        $whole = (int) $matches[1];
        if ($whole > intdiv(PHP_INT_MAX - (int) $fraction, 100)) {
            return null;
        }
        return $whole * 100 + (int) $fraction;
    }

    private function formatCny(int $amountCents): string
    {
        return intdiv($amountCents, 100) . '.' . str_pad((string) ($amountCents % 100), 2, '0', STR_PAD_LEFT);
    }

    private function identifier(mixed $value): string
    {
        if (!is_string($value) || !preg_match('/^[A-Za-z0-9_-]{1,128}$/D', $value)) {
            throw new ApiException('BTCPay or XBoard returned an invalid invoice/order identifier.');
        }
        return $value;
    }

    private function safeIdentifier(mixed $value): ?string
    {
        if (!is_string($value) || !preg_match('/^[A-Za-z0-9_-]{1,128}$/D', $value)) {
            return null;
        }
        return $value;
    }

    private function validAbsoluteUrl(mixed $value, string $label): string
    {
        if (!is_string($value) || strlen($value) > 2048 || !filter_var($value, FILTER_VALIDATE_URL)) {
            throw new ApiException($label . ' must be an absolute URL.');
        }
        $parts = parse_url($value);
        if (!in_array(strtolower($parts['scheme'] ?? ''), ['https', 'http'], true) ||
            empty($parts['host']) || isset($parts['user']) || isset($parts['pass']) ||
            isset($parts['fragment'])) {
            throw new ApiException($label . ' must use HTTPS without embedded credentials or a fragment.');
        }
        if (strtolower($parts['scheme']) !== 'https' && !$this->isDevelopmentHost($parts['host'])) {
            throw new ApiException('Plain HTTP is allowed only for a loopback/private development host.');
        }
        return $value;
    }

    private function validCheckoutLink(mixed $value, string $baseUrl): string
    {
        $checkout = $this->validAbsoluteUrl($value, 'BTCPay checkout link');
        if (app()->environment('production') && !str_starts_with(strtolower($checkout), 'https://')) {
            throw new ApiException('Production checkout links must use HTTPS.');
        }
        $link = parse_url($checkout);
        $base = parse_url($baseUrl);
        if (!in_array(strtolower($link['scheme'] ?? ''), ['https', 'http'], true) ||
            isset($link['user']) || isset($link['pass']) ||
            strtolower($link['host'] ?? '') !== strtolower($base['host'] ?? '') ||
            (int) ($link['port'] ?? 0) !== (int) ($base['port'] ?? 0)) {
            throw new ApiException('BTCPay returned a checkout link outside the configured development server.');
        }
        return $checkout;
    }

    private function isDevelopmentHost(string $host): bool
    {
        if (strtolower($host) === 'localhost') {
            return true;
        }
        $ip = filter_var($host, FILTER_VALIDATE_IP) ? $host : gethostbyname($host);
        if (!filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)) {
            return false;
        }
        $long = ip2long($ip);
        return ($long & 0xff000000) === 0x0a000000 ||
            ($long & 0xfff00000) === 0xac100000 ||
            ($long & 0xffff0000) === 0xc0a80000 ||
            ($long & 0xff000000) === 0x7f000000;
    }
}
