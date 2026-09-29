<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('btcpay_btcx_invoice_bindings', function (Blueprint $table) {
            $table->string('invoice_id', 128)->primary();
            $table->string('store_id', 128);
            $table->string('trade_no', 128)->unique();
            $table->unsignedBigInteger('expected_amount_cents');
            $table->string('btc_cny_rate', 32);
            $table->unsignedBigInteger('expected_btcx_atomic_units');
            $table->string('expected_btcx_amount', 32);
            $table->string('rate_timestamp', 64);
            $table->string('network', 16);
            $table->string('currency', 8);
            $table->string('payment_method_id', 64);
            $table->string('speed_policy', 32);
            $table->text('checkout_link');
            $table->timestamps();
        });

        Schema::create('btcpay_btcx_webhook_registrations', function (Blueprint $table) {
            $table->string('payment_uuid', 128)->primary();
            $table->string('store_id', 128);
            $table->string('notify_url', 2048);
            $table->string('secret_fingerprint', 64);
            $table->string('webhook_id', 128)->default('');
            $table->timestamps();
        });

        Schema::create('btcpay_btcx_webhook_deliveries', function (Blueprint $table) {
            $table->string('delivery_id', 128)->primary();
            $table->string('invoice_id', 128)->index();
            $table->string('trade_no', 128)->index();
            $table->string('event_type', 64);
            $table->timestamp('received_at');
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('btcpay_btcx_webhook_deliveries');
        Schema::dropIfExists('btcpay_btcx_webhook_registrations');
        Schema::dropIfExists('btcpay_btcx_invoice_bindings');
    }
};
