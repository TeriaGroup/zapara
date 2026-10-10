<?php

namespace Tests\Feature;

use App\Console\Commands\MigratePreflight;
use App\Support\Zapara;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\DB;
use Tests\TestCase;

/** Needs the local test PostgreSQL (ZAPARA_TEST_POSTGRES); each test works in a throwaway schema. */
class MigratePreflightTest extends TestCase
{
    private string $schema;

    private ?string $role = null;

    /** @var array<string, mixed> */
    private array $owner;

    protected function setUp(): void
    {
        parent::setUp();
        if (Zapara::dsn() === null) {
            $this->markTestSkipped('ZAPARA_TEST_POSTGRES is not set.');
        }
        $this->owner = config('database.connections.pgsql');
        $this->schema = 'preflight_'.bin2hex(random_bytes(4));
        DB::statement("create schema {$this->schema}");
        $this->usePanelSchema($this->schema);
    }

    protected function tearDown(): void
    {
        config(['database.connections.pgsql' => $this->owner]);
        DB::purge('pgsql');
        DB::statement("drop schema if exists {$this->schema} cascade");
        if ($this->role !== null) {
            DB::statement("drop owned by {$this->role}");
            DB::statement("drop role if exists {$this->role}");
        }
        parent::tearDown();
    }

    public function test_fresh_schema_passes_and_migrations_then_apply(): void
    {
        $this->assertSame(0, Artisan::call('admin:migrate-preflight'));
        $this->assertSame(0, Artisan::call('migrate', ['--force' => true]));
        $this->assertNotNull(DB::selectOne('select to_regclass(?) as t', [$this->schema.'.admin_mfa_credentials'])->t);
        // Nothing pending: passes even for a role that can no longer create tables.
        $this->connectAsRole(create: false, readMigrations: true);
        $this->assertSame(0, Artisan::call('admin:migrate-preflight'));
    }

    public function test_conflicting_users_table_fails_with_an_actionable_line(): void
    {
        DB::statement("create table {$this->schema}.users (id bigint primary key, email text)");

        $this->assertSame(MigratePreflight::CONFIG_PROBLEM, Artisan::call('admin:migrate-preflight'));
        $output = Artisan::output();
        $this->assertStringContainsString("admin: table {$this->schema}.users already exists", $output);
        $this->assertStringContainsString('DB_SCHEMA', $output);
        // The table is left as it was.
        $this->assertSame(['email', 'id'], collect(DB::select(
            'select column_name from information_schema.columns where table_schema = ? and table_name = ? order by 1',
            [$this->schema, 'users'],
        ))->pluck('column_name')->all());
    }

    public function test_compatible_admin_mfa_table_is_accepted_but_a_foreign_one_is_not(): void
    {
        (require database_path('migrations/2026_10_09_000000_create_admin_mfa_credentials_table.php'))->up();
        $this->assertSame(0, Artisan::call('admin:migrate-preflight'));

        DB::statement("drop table {$this->schema}.admin_mfa_credentials");
        DB::statement("create table {$this->schema}.admin_mfa_credentials (id int)");
        $this->assertSame(MigratePreflight::CONFIG_PROBLEM, Artisan::call('admin:migrate-preflight'));
        $this->assertStringContainsString('admin_mfa_credentials already exists', Artisan::output());
    }

    public function test_role_without_create_privilege_fails_with_the_grant_to_run(): void
    {
        $role = $this->connectAsRole(create: false, readMigrations: false);

        $this->assertSame(MigratePreflight::CONFIG_PROBLEM, Artisan::call('admin:migrate-preflight'));
        $output = Artisan::output();
        $this->assertStringContainsString("role \"{$role}\" cannot create tables in schema \"{$this->schema}\"", $output);
        $this->assertStringContainsString("GRANT USAGE, CREATE ON SCHEMA {$this->schema} TO {$role};", $output);

        // With the documented grant the same role passes and can apply the migrations.
        config(['database.connections.pgsql' => $this->owner]);
        DB::purge('pgsql');
        DB::statement("grant usage, create on schema {$this->schema} to {$role}");
        $this->connectAsRole(create: true, readMigrations: false, existing: $role);
        $this->assertSame(0, Artisan::call('admin:migrate-preflight'));
        $this->assertSame(0, Artisan::call('migrate', ['--force' => true]));
    }

    public function test_missing_schema_fails(): void
    {
        $this->usePanelSchema('preflight_missing_'.bin2hex(random_bytes(3)));

        $this->assertSame(MigratePreflight::CONFIG_PROBLEM, Artisan::call('admin:migrate-preflight'));
        $this->assertStringContainsString('does not exist', Artisan::output());
    }

    public function test_unreachable_database_is_reported_as_retryable(): void
    {
        config(['database.connections.pgsql.port' => '1']);
        DB::purge('pgsql');

        $this->assertSame(MigratePreflight::NOT_REACHABLE, Artisan::call('admin:migrate-preflight'));
        $this->assertStringContainsString('admin: database not reachable', Artisan::output());
    }

    private function usePanelSchema(string $schema): void
    {
        config(['database.connections.pgsql.search_path' => $schema]);
        DB::purge('pgsql');
    }

    private function connectAsRole(bool $create, bool $readMigrations, ?string $existing = null): string
    {
        $password = bin2hex(random_bytes(12));
        config(['database.connections.pgsql' => $this->owner]);
        DB::purge('pgsql');
        if ($existing === null) {
            $this->role = 'preflight_role_'.bin2hex(random_bytes(4));
            DB::statement("create role {$this->role} login password '{$password}'");
            DB::statement("grant usage on schema {$this->schema} to {$this->role}");
            if ($readMigrations) {
                DB::statement("grant select on {$this->schema}.migrations to {$this->role}");
            }
        } else {
            DB::statement("alter role {$existing} password '{$password}'");
        }
        config([
            'database.connections.pgsql.username' => $this->role,
            'database.connections.pgsql.password' => $password,
        ]);
        $this->usePanelSchema($this->schema);

        return (string) $this->role;
    }
}
