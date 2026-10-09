<?php

namespace Tests\Concerns;

use App\Auth\IdentityPassword;
use App\Models\AccountUser;
use App\Support\Zapara;
use Filament\Facades\Filament;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Symfony\Component\Process\Process;

trait PanelFixtures
{
    protected const PANEL_PASSWORD = 'Panel-password-1';

    /** @var list<string> */
    private array $fixtureUsers = [];

    /** @var list<string> */
    private array $fixtureCommunities = [];

    public static function setUpBeforeClass(): void
    {
        parent::setUpBeforeClass();
        $dsn = Zapara::dsn();
        if ($dsn === null) {
            return;
        }
        $root = dirname(__DIR__, 3);
        $project = $root.'/src/Zapara.Server.Tests/Zapara.Server.Tests.csproj';
        $process = new Process(
            ['dotnet', 'test', $project, '--filter', 'Prepare_panel_schemas', '--nologo', '--verbosity', 'minimal'],
            $root,
            ['ZAPARA_TEST_POSTGRES' => $dsn],
            null,
            300,
        );
        $process->run();
        if ($process->getExitCode() !== 0) {
            throw new \RuntimeException((string) preg_replace('/Password=[^;\s]*/', 'Password=[redacted]', $process->getOutput().$process->getErrorOutput()));
        }
    }

    protected function setUp(): void
    {
        parent::setUp();
        if (Zapara::dsn() === null) {
            $this->fail('ZAPARA_TEST_POSTGRES is required.');
        }
        Filament::setCurrentPanel(Filament::getPanel('admin'));
        config(['app.env' => 'local']);
    }

    protected function tearDown(): void
    {
        $communities = Zapara::communities();
        foreach ($this->fixtureCommunities as $id) {
            $audited = DB::table($communities.'.community_audit')->where('community_id', $id)->exists();
            DB::table($communities.'.join_requests')->where('community_id', $id)->delete();
            if (! $audited) {
                DB::table($communities.'.memberships')->where('community_id', $id)->delete();
                DB::table($communities.'.communities')->where('community_id', $id)->delete();
            }
        }
        foreach ($this->fixtureUsers as $id) {
            $this->deleteFixtureUser($id);
        }
        parent::tearDown();
    }

    protected function makeUser(bool $admin, ?string $displayName = null): AccountUser
    {
        $username = $this->token($admin ? 'a' : 'm');
        $id = (string) Str::uuid();
        $now = now();
        DB::table(Zapara::accounts().'.users')->insert([
            'user_id' => $id,
            'username' => $username,
            'normalized_username' => strtolower($username),
            'display_name' => $displayName ?? ($admin ? 'Оператор' : 'Участник'),
            'created_at' => $now,
            'status' => 'active',
        ]);
        DB::table(Zapara::accounts().'.password_credentials')->insert([
            'user_id' => $id,
            'password_hash' => IdentityPassword::hash(self::PANEL_PASSWORD),
            'changed_at' => $now,
        ]);
        if ($admin) {
            DB::table(Zapara::admin().'.platform_admins')->insert([
                'user_id' => $id,
                'granted_at' => $now,
                'revoked_at' => null,
            ]);
        }
        $this->fixtureUsers[] = $id;

        return AccountUser::query()->findOrFail($id);
    }

    protected function makeCommunity(string $name): string
    {
        $id = (string) Str::uuid();
        DB::table(Zapara::communities().'.communities')->insert([
            'community_id' => $id,
            'name' => $name,
            'description' => '',
            'revision' => 1,
            'created_at' => now(),
            'updated_at' => now(),
        ]);
        $this->fixtureCommunities[] = $id;

        return $id;
    }

    protected function makeJoinRequest(string $communityId, AccountUser $user, ?\DateTimeInterface $at = null): string
    {
        $id = (string) Str::uuid();
        DB::table(Zapara::communities().'.join_requests')->insert([
            'request_id' => $id,
            'community_id' => $communityId,
            'user_id' => $user->user_id,
            'status' => 'pending',
            'created_at' => $at ?? now(),
        ]);

        return $id;
    }

    protected function token(string $prefix): string
    {
        return $prefix.bin2hex(random_bytes(4));
    }

    private function deleteFixtureUser(string $id): void
    {
        $audited = DB::table(Zapara::communities().'.community_audit')->where('actor_id', $id)->exists()
            || DB::table(Zapara::admin().'.admin_audit')->where('actor_id', $id)->exists();
        if ($audited) {
            return;
        }
        $accounts = Zapara::accounts();
        DB::table(Zapara::communities().'.join_requests')->where('user_id', $id)->delete();
        DB::table(Zapara::communities().'.memberships')->where('user_id', $id)->delete();
        DB::table($accounts.'.password_credentials')->where('user_id', $id)->delete();
        DB::table(Zapara::admin().'.admin_sessions')->where('user_id', $id)->delete();
        DB::table(Zapara::admin().'.platform_admins')->where('user_id', $id)->delete();
        DB::table($accounts.'.users')->where('user_id', $id)->delete();
    }
}
