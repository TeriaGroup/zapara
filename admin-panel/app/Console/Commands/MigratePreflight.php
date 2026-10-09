<?php

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Database\Migrations\Migrator;
use Illuminate\Support\Facades\DB;

/**
 * Checks, before `migrate --force` runs on container start, that the panel's pending migrations can be applied
 * in the shared zapara database: the panel schema exists, the role may create tables in it, and no table the
 * migrations would create already exists there without having been created by them.
 *
 * Exit codes: 0 ready (or nothing pending), 1 database not reachable (worth retrying), 2 configuration problem
 * (retrying will not help; every problem is printed as one "admin:" line with the fix).
 */
final class MigratePreflight extends Command
{
    public const NOT_REACHABLE = 1;

    public const CONFIG_PROBLEM = 2;

    protected $signature = 'admin:migrate-preflight';

    protected $description = 'Check that the panel migrations can run against the configured database';

    /**
     * Tables each panel migration creates. null: the migration fails if the table exists. A column list: the
     * migration skips an existing table, which is accepted only if it has these columns.
     *
     * @var array<string, array<string, list<string>|null>>
     */
    public const TABLES = [
        '0001_01_01_000000_create_users_table' => ['users' => null, 'password_reset_tokens' => null, 'sessions' => null],
        '0001_01_01_000001_create_cache_table' => ['cache' => null, 'cache_locks' => null],
        '0001_01_01_000002_create_jobs_table' => ['jobs' => null, 'job_batches' => null, 'failed_jobs' => null],
        '2026_10_09_000000_create_admin_mfa_credentials_table' => [
            'admin_mfa_credentials' => ['user_id', 'app_authentication_secret', 'app_authentication_recovery_codes'],
        ],
    ];

    public function handle(Migrator $migrator): int
    {
        $connection = DB::connection();
        try {
            $connection->getPdo();
        } catch (\Throwable $e) {
            $this->error('admin: database not reachable ('.$e->getMessage().')');

            return self::NOT_REACHABLE;
        }
        if ($connection->getDriverName() !== 'pgsql') {
            return self::SUCCESS;
        }

        $info = $connection->selectOne('select current_user as role, current_database() as db, current_schema() as schema');
        $role = (string) $info->role;
        $searchPath = (string) $connection->getConfig('search_path');
        if ($info->schema === null) {
            return $this->report([
                "schema \"{$searchPath}\" (DB_SCHEMA) does not exist in database \"{$info->db}\". "
                ."Create it: CREATE SCHEMA {$searchPath} AUTHORIZATION {$role};",
            ]);
        }
        $schema = (string) $info->schema;

        $migrationsTable = (string) config('database.migrations.table', 'migrations');
        $columns = fn (string $table): array => array_map(
            fn ($row) => (string) $row->column_name,
            $connection->select(
                'select column_name from information_schema.columns where table_schema = ? and table_name = ?',
                [$schema, $table],
            ),
        );

        $ran = [];
        $repositoryColumns = $columns($migrationsTable);
        if ($repositoryColumns !== []) {
            if (array_diff(['migration', 'batch'], $repositoryColumns) !== []) {
                return $this->report([
                    "table {$schema}.{$migrationsTable} exists but is not a Laravel migrations table. "
                    .'Use a separate schema for the panel (DB_SCHEMA) or rename that table.',
                ]);
            }
            $ran = $connection->table($migrationsTable)->pluck('migration')->map(fn ($m) => (string) $m)->all();
        }

        $files = array_keys($migrator->getMigrationFiles([database_path('migrations')]));
        $pending = array_values(array_diff($files, $ran));
        if ($pending === []) {
            return self::SUCCESS;
        }

        $problems = [];
        $canCreate = (bool) $connection->selectOne(
            "select has_schema_privilege(current_user, ?, 'USAGE') and has_schema_privilege(current_user, ?, 'CREATE') as ok",
            [$schema, $schema],
        )->ok;
        if (! $canCreate) {
            $problems[] = "role \"{$role}\" cannot create tables in schema \"{$schema}\" of database \"{$info->db}\" "
                .'('.count($pending).' panel migrations pending). '
                ."Grant it: GRANT USAGE, CREATE ON SCHEMA {$schema} TO {$role}; "
                .'or apply the migrations with a privileged role and set ZAPARA_ADMIN_MIGRATE_ON_START=false.';
        }

        foreach ($pending as $migration) {
            foreach (self::TABLES[$migration] ?? [] as $table => $required) {
                $existing = $columns($table);
                if ($existing === []) {
                    continue;
                }
                if ($required !== null && array_diff($required, $existing) === []) {
                    continue;
                }
                $problems[] = "table {$schema}.{$table} already exists and was not created by the panel migration "
                    ."{$migration}. Point the panel at its own schema (DB_SCHEMA, see admin-panel/README.md) "
                    .'instead of dropping or changing a table another application may use.';
            }
        }

        return $problems === [] ? self::SUCCESS : $this->report($problems);
    }

    /** @param list<string> $problems */
    private function report(array $problems): int
    {
        foreach ($problems as $problem) {
            $this->error('admin: '.$problem);
        }

        return self::CONFIG_PROBLEM;
    }
}
