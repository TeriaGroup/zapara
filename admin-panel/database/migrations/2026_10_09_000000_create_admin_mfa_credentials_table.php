<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

/**
 * TOTP secrets and recovery codes for panel administrators.
 * Lives in the panel's own schema next to cache/jobs; the accounts schema owned by Zapara.Server is not touched.
 */
return new class extends Migration
{
    public function up(): void
    {
        Schema::create('admin_mfa_credentials', function (Blueprint $table) {
            // accounts.users.user_id of the administrator.
            $table->uuid('user_id')->primary();
            // Encrypted with APP_KEY (Laravel "encrypted" casts).
            $table->text('app_authentication_secret')->nullable();
            $table->text('app_authentication_recovery_codes')->nullable();
            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('admin_mfa_credentials');
    }
};
