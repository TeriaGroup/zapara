using System.Security.Cryptography;
using System.Text;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Preferences;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>The downloaded archive is checked against the release's SHA256SUMS (and its signature, once the
/// release key is set) before it can become «Ready», and again right before it is handed to the installer.</summary>
public class UpdateVerificationTests
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);
    private const string ZipName = "ZAPARA_windows-v2.2.0_win-x64.zip";
    private static AutoUpdateService.UpdateInfo Newer => new("windows-v2.2.0", "https://example.test/releases/tag/windows-v2.2.0",
        "https://example.test/download/" + ZipName, "2026-09-05T10:00:00Z", ZipName,
        "https://example.test/download/SHA256SUMS", "https://example.test/download/SHA256SUMS.sig");

    private static (UpdateCheckViewModel Vm, FakeUpdateSource Source, List<string> Installed) Make(TestDb db)
    {
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        var installed = new List<string>();
        var vm = new UpdateCheckViewModel(db.Services, () => Sun6, Path.Combine(db.Dir, "updates"))
        {
            Installer = installed.Add,
            Delay = _ => Task.CompletedTask,
            StagingRoot = Path.Combine(db.Dir, "staging")
        };
        return (vm, source, installed);
    }

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public void Checksums_file_is_parsed_in_sha256sum_format()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var map = UpdateVerifier.ParseChecksums($"{a}  {ZipName}\r\n{b} *ZAPARA_android-debug.apk\n# comment\nnot a line\n");
        Assert.Equal(a, map[ZipName]);
        Assert.Equal(new string('b', 64), map["ZAPARA_android-debug.apk"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Conflicting_entries_make_the_checksums_unusable()
    {
        var map = UpdateVerifier.ParseChecksums($"{new string('a', 64)}  {ZipName}\n{new string('c', 64)}  {ZipName}\n");
        Assert.Empty(map);
    }

    [Fact]
    public void Verify_compares_the_file_with_its_listed_hash()
    {
        var dir = Directory.CreateTempSubdirectory("upd-verify-");
        try
        {
            var path = Path.Combine(dir.FullName, ZipName);
            var bytes = FakeUpdateSource.ReleaseZip();
            File.WriteAllBytes(path, bytes);
            var good = Encoding.UTF8.GetBytes($"{Hex(bytes)}  {ZipName}\n");
            var result = UpdateVerifier.Verify(path, ZipName, good, null, "");
            Assert.True(result.Ok);
            Assert.Equal(Hex(bytes), result.Sha256);

            var other = Encoding.UTF8.GetBytes($"{Hex(new byte[] { 1 })}  {ZipName}\n");
            Assert.Equal(UpdateVerifier.Outcome.Mismatch, UpdateVerifier.Verify(path, ZipName, other, null, "").Outcome);
            Assert.Equal(UpdateVerifier.Outcome.NotListed, UpdateVerifier.Verify(path, "other.zip", good, null, "").Outcome);
            Assert.Equal(UpdateVerifier.Outcome.NoChecksums, UpdateVerifier.Verify(path, ZipName, null, null, "").Outcome);
            Assert.Equal(UpdateVerifier.Outcome.NoChecksums, UpdateVerifier.Verify(path, ZipName, Array.Empty<byte>(), null, "").Outcome);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Signature_of_the_checksums_is_required_once_a_key_is_set()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportSubjectPublicKeyInfoPem();
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var dir = Directory.CreateTempSubdirectory("upd-sign-");
        try
        {
            var path = Path.Combine(dir.FullName, ZipName);
            var bytes = FakeUpdateSource.ReleaseZip();
            File.WriteAllBytes(path, bytes);
            var sums = Encoding.UTF8.GetBytes($"version: 2.2.0\n{Hex(bytes)}  {ZipName}\n");
            var der = key.SignData(sums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var raw = key.SignData(sums, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            Assert.True(UpdateVerifier.Verify(path, ZipName, sums, der, pem).Ok);
            Assert.True(UpdateVerifier.Verify(path, ZipName, sums, raw, pem).Ok);
            Assert.Equal(UpdateVerifier.Outcome.NoSignature, UpdateVerifier.Verify(path, ZipName, sums, null, pem).Outcome);
            var foreign = otherKey.SignData(sums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            Assert.Equal(UpdateVerifier.Outcome.BadSignature, UpdateVerifier.Verify(path, ZipName, sums, foreign, pem).Outcome);
            var edited = Encoding.UTF8.GetBytes($"{Hex(new byte[] { 1 })}  {ZipName}\n");
            Assert.Equal(UpdateVerifier.Outcome.BadSignature, UpdateVerifier.Verify(path, ZipName, edited, der, pem).Outcome);
            Assert.False(UpdateVerifier.VerifySignature(sums, der, "not a key"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Asset_name_comes_from_the_download_url()
    {
        Assert.Equal(ZipName, UpdateVerifier.AssetNameFromUrl("https://github.com/TeriaGroup/zapara-releases/releases/download/windows-v2.2.0/" + ZipName));
        Assert.Equal("a b.zip", UpdateVerifier.AssetNameFromUrl("https://example.test/x/a%20b.zip?download=1"));
    }

    [Fact]
    public async Task Archive_not_matching_the_release_checksum_is_deleted_and_never_installed()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Checksums = $"{Hex(new byte[] { 42 })}  {ZipName}\n";
        Assert.True(await vm.CheckAsync());

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal(db.Services.Loc.T("updBadChecksum"), vm.StatusText);
        Assert.Empty(Directory.GetFiles(vm.UpdatesDir, "*.zip"));
    }

    [Fact]
    public async Task Release_without_checksums_is_not_downloaded()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        source.Latest = Newer with { ChecksumsUrl = null };
        Assert.True(await vm.CheckAsync());

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(source.Downloads);
        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal(db.Services.Loc.T("updNoChecksum"), vm.StatusText);
    }

    [Fact]
    public async Task Cached_archive_that_no_longer_matches_is_downloaded_again()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        Directory.CreateDirectory(vm.UpdatesDir);
        var cached = FakeUpdateSource.ReleaseZip(nested: true); // a valid zip, but not the listed one
        File.WriteAllBytes(Path.Combine(vm.UpdatesDir, ZipName), cached);
        source.Checksums = FakeUpdateSource.ChecksumsFor(Newer, FakeUpdateSource.ReleaseZip());
        Assert.True(await vm.CheckAsync());

        Assert.True(await vm.DownloadAsync());

        Assert.Single(source.Downloads);
        Assert.Equal(UpdateState.Ready, vm.State);
        Assert.Equal(FakeUpdateSource.ReleaseZip(), File.ReadAllBytes(Path.Combine(vm.UpdatesDir, ZipName)));
        Assert.Empty(installed);
    }

    [Fact]
    public async Task Archive_changed_after_verification_is_not_handed_to_the_installer()
    {
        using var db = TestDb.Create();
        var (vm, _, installed) = Make(db);
        Assert.True(await vm.CheckAsync());
        Assert.True(await vm.DownloadAsync());
        var path = Path.Combine(vm.UpdatesDir, ZipName);
        File.WriteAllBytes(path, FakeUpdateSource.ReleaseZip(nested: true));

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task With_a_release_key_only_a_signed_checksum_file_is_accepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        vm.ReleasePublicKeyPem = key.ExportSubjectPublicKeyInfoPem();
        var sums = FakeUpdateSource.ChecksumsFor(Newer, FakeUpdateSource.ReleaseZip());
        source.Checksums = sums;
        source.Latest = Newer with { SignatureUrl = null };

        Assert.True(await vm.CheckAsync());
        Assert.False(await vm.DownloadAsync()); // no SHA256SUMS.sig in the release
        Assert.Equal(db.Services.Loc.T("updBadSignature"), vm.StatusText);

        source.Latest = Newer;
        source.Signature = key.SignData(Encoding.UTF8.GetBytes(sums), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.True(await vm.CheckAsync());
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Single(installed);
    }
}

/// <summary>Review follow-ups: the manifest is bound to its release, superseded work never installs, and a
/// manifest that cannot be fetched right now keeps the archive.</summary>
public class UpdateVerificationBindingTests
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);
    private const string ZipName = "ZAPARA_windows-v2.2.0_win-x64.zip";
    private static AutoUpdateService.UpdateInfo Newer => new("windows-v2.2.0", "https://example.test/releases/tag/windows-v2.2.0",
        "https://example.test/download/" + ZipName, "2026-09-05T10:00:00Z", ZipName,
        "https://example.test/download/SHA256SUMS", "https://example.test/download/SHA256SUMS.sig");

    private static (UpdateCheckViewModel Vm, FakeUpdateSource Source, List<string> Installed) Make(TestDb db)
    {
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        var installed = new List<string>();
        var vm = new UpdateCheckViewModel(db.Services, () => Sun6, Path.Combine(db.Dir, "updates"))
        {
            Installer = installed.Add,
            Delay = _ => Task.CompletedTask,
            StagingRoot = Path.Combine(db.Dir, "staging")
        };
        return (vm, source, installed);
    }

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static UpdateVerifier.Result VerifyWith(string manifest, string? key = null, byte[]? sig = null,
        string? releaseTag = "windows-v2.2.0", string? currentTag = "windows-v2.1.42")
    {
        var dir = Directory.CreateTempSubdirectory("upd-bind-");
        try
        {
            var path = Path.Combine(dir.FullName, ZipName);
            File.WriteAllBytes(path, FakeUpdateSource.ReleaseZip());
            return UpdateVerifier.Verify(path, ZipName, Encoding.UTF8.GetBytes(manifest), sig, key ?? "", releaseTag, currentTag);
        }
        finally { dir.Delete(true); }
    }

    private static string Line => $"{Hex(FakeUpdateSource.ReleaseZip())}  {ZipName}\n";

    [Fact]
    public void Version_header_is_parsed_and_files_without_it_still_parse()
    {
        var withHeader = UpdateVerifier.ParseManifest("version: 2.2.0\n" + Line);
        Assert.Equal("2.2.0", withHeader.Version);
        Assert.Single(withHeader.Checksums);
        var plain = UpdateVerifier.ParseManifest(Line);
        Assert.Null(plain.Version);
        Assert.Single(plain.Checksums);
        Assert.True(UpdateVerifier.ParseManifest("version: 2.2.0\nversion: 2.1.0\n" + Line).Conflicting);
        Assert.Equal(AutoUpdateService.ParseVersion("2.2.0"), AutoUpdateService.ParseVersion("windows-v2.2.0"));
        Assert.Equal(AutoUpdateService.ParseVersion("v2.2.0.0"), AutoUpdateService.ParseVersion("2.2"));
    }

    [Fact]
    public void Manifest_version_must_match_the_release_and_not_be_older_than_the_app()
    {
        Assert.True(VerifyWith("version: 2.2.0\n" + Line).Ok);
        Assert.True(VerifyWith(Line).Ok); // unsigned, legacy format: checksum only
        Assert.Equal(UpdateVerifier.Outcome.WrongVersion, VerifyWith("version: 2.1.50\n" + Line).Outcome);
        Assert.Equal(UpdateVerifier.Outcome.Downgrade, VerifyWith("version: 2.1.0\n" + Line, releaseTag: "v2.1.0").Outcome);
        Assert.Equal(UpdateVerifier.Outcome.NoVersion, VerifyWith("version: banana\n" + Line).Outcome);
    }

    [Fact]
    public void Signed_manifest_must_name_its_version()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportSubjectPublicKeyInfoPem();
        byte[] Sign(string m) => key.SignData(Encoding.UTF8.GetBytes(m), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal(UpdateVerifier.Outcome.NoVersion, VerifyWith(Line, pem, Sign(Line)).Outcome);
        var bound = "version: 2.2.0\n" + Line;
        Assert.True(VerifyWith(bound, pem, Sign(bound)).Ok);
    }

    [Fact]
    public async Task Signed_manifest_of_an_older_release_is_rejected_under_a_newer_tag()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        vm.ReleasePublicKeyPem = key.ExportSubjectPublicKeyInfoPem();
        var old = "version: 2.1.30\n" + FakeUpdateSource.ChecksumsFor(Newer with { Tag = "x" }, FakeUpdateSource.ReleaseZip());
        source.Checksums = old;
        source.Signature = key.SignData(Encoding.UTF8.GetBytes(old), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.True(await vm.CheckAsync());

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal(db.Services.Loc.T("updBadVersion"), vm.StatusText);
        Assert.Empty(Directory.GetFiles(vm.UpdatesDir, "*.zip"));
    }

    [Fact]
    public async Task Work_superseded_while_hashing_never_reaches_the_installer()
    {
        using var db = TestDb.Create();
        var (vm, _, installed) = Make(db);
        Assert.True(await vm.CheckAsync());
        Assert.True(await vm.DownloadAsync());
        vm.HashArchive = path =>
        {
            db.Services.Work.Suspend(); // e.g. a profile switch while the archive is being hashed
            return UpdateVerifier.Sha256File(path);
        };

        await vm.InstallAsync();

        Assert.Empty(installed);
        Assert.False(File.Exists(Path.Combine(vm.UpdatesDir, ZipName + ".attempted")));
    }

    [Fact]
    public async Task Transient_manifest_failure_keeps_the_cached_archive_and_a_retry_installs_it()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        Directory.CreateDirectory(vm.UpdatesDir);
        var path = Path.Combine(vm.UpdatesDir, ZipName);
        File.WriteAllBytes(path, FakeUpdateSource.ReleaseZip());
        source.ChecksumsFailure = new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable);
        Assert.True(await vm.CheckAsync());

        Assert.False(await vm.DownloadAsync());
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal(db.Services.Loc.T("updVerifyRetry"), vm.StatusText);
        Assert.True(File.Exists(path));

        source.ChecksumsFailure = new TaskCanceledException("timeout"); // HttpClient timeout, not our cancellation
        Assert.True(await vm.CheckAsync());
        Assert.False(await vm.DownloadAsync());
        Assert.True(File.Exists(path));

        source.ChecksumsFailure = null;
        Assert.True(await vm.CheckAsync());
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Empty(source.Downloads);
        Assert.Single(installed);
    }

    [Fact]
    public async Task Transient_failure_after_a_fresh_download_keeps_it_for_the_next_attempt()
    {
        using var db = TestDb.Create();
        var (vm, source, _) = Make(db);
        source.ChecksumsFailure = new HttpRequestException("network down");
        Assert.True(await vm.CheckAsync());

        Assert.False(await vm.DownloadAsync());
        Assert.Equal(db.Services.Loc.T("updVerifyRetry"), vm.StatusText);
        Assert.True(File.Exists(Path.Combine(vm.UpdatesDir, ZipName)));

        source.ChecksumsFailure = null;
        Assert.True(await vm.CheckAsync());
        Assert.True(await vm.DownloadAsync());
        Assert.Single(source.Downloads);
    }

    [Fact]
    public async Task Missing_manifest_404_deletes_the_cached_archive()
    {
        using var db = TestDb.Create();
        var (vm, source, installed) = Make(db);
        Directory.CreateDirectory(vm.UpdatesDir);
        var path = Path.Combine(vm.UpdatesDir, ZipName);
        File.WriteAllBytes(path, FakeUpdateSource.ReleaseZip());
        source.ChecksumsFailure = new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound);
        Assert.True(await vm.CheckAsync());

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Empty(source.Downloads);
        Assert.Equal(db.Services.Loc.T("updNoChecksum"), vm.StatusText);
        Assert.False(File.Exists(path));
    }
}

public class UpdateStagingTests
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);
    private const string ZipName = "ZAPARA_windows-v2.2.0_win-x64.zip";
    private static AutoUpdateService.UpdateInfo Newer => new("windows-v2.2.0", "https://example.test/releases/tag/windows-v2.2.0",
        "https://example.test/download/" + ZipName, "2026-09-05T10:00:00Z", ZipName,
        "https://example.test/download/SHA256SUMS", "https://example.test/download/SHA256SUMS.sig");

    [Fact]
    public async Task Installer_gets_a_verified_copy_in_a_private_folder_not_the_download()
    {
        using var db = TestDb.Create();
        var source = new FakeUpdateSource { Latest = Newer };
        db.Services.UpdateSource = source;
        var staging = Path.Combine(db.Dir, "staging");
        string? handed = null;
        byte[]? handedBytes = null;
        var vm = new UpdateCheckViewModel(db.Services, () => Sun6, Path.Combine(db.Dir, "updates"))
        {
            Installer = p => { handed = p; handedBytes = File.ReadAllBytes(p); },
            Delay = _ => Task.CompletedTask,
            StagingRoot = staging
        };
        Assert.True(await vm.CheckAsync());
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.NotNull(handed);
        var dir = Path.GetDirectoryName(handed)!;
        Assert.Equal(Path.GetFullPath(staging), Path.GetFullPath(Path.GetDirectoryName(dir)!));
        Assert.NotEqual(Path.Combine(vm.UpdatesDir, ZipName), handed);
        Assert.Equal(FakeUpdateSource.ReleaseZip(), handedBytes);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
        Assert.True(File.Exists(Path.Combine(vm.UpdatesDir, ZipName + ".attempted"))); // R45 marker stays with the download

        // Each install stages into its own folder; old staged folders are removed at the next start.
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddDays(-1));
        await vm.CleanupAsync();
        Assert.False(Directory.Exists(dir));
    }
}

public class UpdateAssetNameTests
{
    private static (string, string) A(string name) => (name, "https://example.test/" + name);

    [Fact]
    public void Versioned_windows_archive_is_preferred_then_the_legacy_name()
    {
        Assert.Equal("ZAPARA_win-x64_2.1.43.zip", AutoUpdateService.VersionedWindowsAssetName("v2.1.43"));
        var both = new[] { A("ZAPARA_win-x64.zip"), A("ZAPARA_win-x64_2.1.42.zip"), A("ZAPARA_win-x64_2.1.43.zip"), A("ZAPARA_android-debug.apk") };
        Assert.Equal("ZAPARA_win-x64_2.1.43.zip", AutoUpdateService.PickAsset(both, "windows-v2.1.43", wantZip: true)!.Value.Name);
        // A versioned file of another version is not «this release's» archive: fall back to the legacy name.
        var legacy = new[] { A("ZAPARA_win-x64_2.1.42.zip"), A("ZAPARA_win-x64.zip") };
        Assert.Equal("ZAPARA_win-x64.zip", AutoUpdateService.PickAsset(legacy, "v2.1.43", wantZip: true)!.Value.Name);
        var old = new[] { A("other.zip"), A("ZAPARA_old.zip"), A("last.zip") };
        Assert.Equal("ZAPARA_old.zip", AutoUpdateService.PickAsset(old, "v1.2", wantZip: true)!.Value.Name);
        Assert.Equal("ZAPARA_android-debug.apk", AutoUpdateService.PickAsset(both, "android-v1.2.21", wantZip: false)!.Value.Name);
        Assert.Null(AutoUpdateService.PickAsset(new[] { A("SHA256SUMS") }, "v2.1.43", wantZip: true));
    }

    [Fact]
    public async Task Latest_release_reports_the_versioned_archive_and_manifest_assets()
    {
        const string json = """
        [{"tag_name":"v2.1.43","html_url":"https://example.test/r","published_at":"2026-10-01T00:00:00Z","assets":[
          {"name":"ZAPARA_win-x64.zip","browser_download_url":"https://example.test/d/ZAPARA_win-x64.zip"},
          {"name":"ZAPARA_win-x64_2.1.43.zip","browser_download_url":"https://example.test/d/ZAPARA_win-x64_2.1.43.zip"},
          {"name":"SHA256SUMS","browser_download_url":"https://example.test/d/SHA256SUMS"},
          {"name":"SHA256SUMS.sig","browser_download_url":"https://example.test/d/SHA256SUMS.sig"}]}]
        """;
        using var service = new AutoUpdateService(new HttpClient(new StubHandler(json)));
        var info = await service.GetLatestAsync("windows");
        Assert.NotNull(info);
        Assert.Equal("ZAPARA_win-x64_2.1.43.zip", info!.ZipName);
        Assert.Equal("https://example.test/d/ZAPARA_win-x64_2.1.43.zip", info.ZipUrl);
        Assert.Equal("https://example.test/d/SHA256SUMS", info.ChecksumsUrl);
        Assert.Equal("https://example.test/d/SHA256SUMS.sig", info.SignatureUrl);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

public class UpdateSignatureRequiredTests
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);
    private const string ZipName = "ZAPARA_windows-v2.2.0_win-x64.zip";
    private static AutoUpdateService.UpdateInfo Newer => new("windows-v2.2.0", "https://example.test/releases/tag/windows-v2.2.0",
        "https://example.test/download/" + ZipName, "2026-09-05T10:00:00Z", ZipName,
        "https://example.test/download/SHA256SUMS", "https://example.test/download/SHA256SUMS.sig");

    [Theory]
    [InlineData("missing-asset")]
    [InlineData("404")]
    [InlineData("garbage")]
    [InlineData("other-key")]
    public async Task With_a_release_key_an_unsigned_or_badly_signed_release_is_refused(string kind)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var db = TestDb.Create();
        var source = new FakeUpdateSource { Latest = kind == "missing-asset" ? Newer with { SignatureUrl = null } : Newer };
        db.Services.UpdateSource = source;
        var installed = new List<string>();
        var vm = new UpdateCheckViewModel(db.Services, () => Sun6, Path.Combine(db.Dir, "updates"))
        {
            Installer = installed.Add,
            Delay = _ => Task.CompletedTask,
            StagingRoot = Path.Combine(db.Dir, "staging"),
            ReleasePublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
        };
        var sums = FakeUpdateSource.ChecksumsFor(Newer, FakeUpdateSource.ReleaseZip());
        source.Checksums = sums;
        source.Signature = kind switch
        {
            "garbage" => new byte[] { 1, 2, 3 },
            "other-key" => other.SignData(Encoding.UTF8.GetBytes(sums), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
            _ => null // "404": the fake answers 404 for the .sig
        };
        Assert.True(await vm.CheckAsync());

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installed);
        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal(db.Services.Loc.T("updBadSignature"), vm.StatusText);
        Assert.Empty(Directory.GetFiles(vm.UpdatesDir, "*.zip"));
    }
}
