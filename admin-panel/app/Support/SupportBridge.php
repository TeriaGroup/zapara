<?php

namespace App\Support;

use Symfony\Component\Process\Process;

final class SupportBridge
{
    public static function reply(string $threadId, string $body): void
    {
        if (! preg_match('/\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z/', $threadId)) {
            throw new \RuntimeException('Ответ не записан.');
        }
        $file = tempnam(sys_get_temp_dir(), 'zapara-reply-');
        if ($file === false) {
            throw new \RuntimeException('Ответ не записан.');
        }
        file_put_contents($file, $body);
        $root = dirname(__DIR__, 3);
        $project = $root.DIRECTORY_SEPARATOR.'src'.DIRECTORY_SEPARATOR.'Zapara.AdminCli'.DIRECTORY_SEPARATOR.'Zapara.AdminCli.csproj';
        $env = getenv();
        if (! is_array($env)) {
            $env = [];
        }
        $env['Accounts__Enabled'] = 'true';
        $env['Accounts__Schema'] = (string) config('zapara.schemas.accounts');
        $env['Operator__Schema'] = (string) config('zapara.schemas.settings');
        $env['ConnectionStrings__Accounts'] = self::dsn();
        $process = new Process(
            ['dotnet', 'run', '--project', $project, '-c', 'Debug', '--no-launch-profile', '--', 'support', 'reply', '--thread', $threadId, '--body-file', $file],
            $root,
            $env,
            null,
            120,
        );
        $process->run();
        @unlink($file);
        if ($process->getExitCode() !== 0) {
            throw new \RuntimeException('Ответ не записан.');
        }
    }

    private static function dsn(): string
    {
        $raw = Zapara::dsn();
        if (is_string($raw) && trim($raw) !== '') {
            return $raw;
        }
        $name = (string) config('database.default');
        $connection = config('database.connections.'.$name);
        if (! is_array($connection) || ($connection['driver'] ?? '') !== 'pgsql') {
            throw new \RuntimeException('Ответ не записан.');
        }

        return 'Host='.$connection['host'].';Port='.(string) $connection['port'].';Database='.$connection['database'].';Username='.$connection['username'].';Password='.$connection['password'];
    }
}
