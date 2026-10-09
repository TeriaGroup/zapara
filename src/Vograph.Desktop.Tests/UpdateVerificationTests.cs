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
            Delay = _ => Task.CompletedTask
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
            var sums = Encoding.UTF8.GetBytes($"{Hex(bytes)}  {ZipName}\n");
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
