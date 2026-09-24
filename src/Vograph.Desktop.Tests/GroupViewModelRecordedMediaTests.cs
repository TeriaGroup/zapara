using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class GroupViewModelRecordedMediaTests
{
    private static readonly Guid Conversation = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Message = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly byte[] Media = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    [AvaloniaTheory]
    [InlineData("voice", "Голосовое")]
    [InlineData("circle", "Кружок")]
    public async Task Group_recording_sends_kind_and_duration_without_changing_the_text_draft(string kind, string label)
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var uploads = 0;
        handler.Send = (request, _) => Task.FromResult(Route(request));
        HttpResponseMessage Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/communities", StringComparison.Ordinal))
                return Payload(new[] { Membership });
            if (request.Method == HttpMethod.Get && path.EndsWith("/home", StringComparison.Ordinal))
                return Payload(Home());
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return Payload(new ChatPageResponse([], false));
            if (request.Method == HttpMethod.Post && path.EndsWith("/read", StringComparison.Ordinal))
                return Payload(Home().GroupChat);
            if (request.Method == HttpMethod.Post && path.EndsWith("/media", StringComparison.Ordinal))
            {
                Assert.Equal(kind, request.Headers.GetValues("X-Zapara-Kind").Single());
                Assert.Equal("1500", request.Headers.GetValues("X-Zapara-Duration-Ms").Single());
                uploads++;
                return Payload(new ChatMessageResponse(Message, Conversation, UserId, "Аня", label,
                    AccountClientTestSupport.Now, kind), System.Net.HttpStatusCode.Created);
            }
            return Problem(404, "not_found");
        }
        var recorder = new FakeRecorder(kind);
        var vm = new GroupViewModel(services, recorder);
        await vm.ActivateAsync();
        Assert.True(vm.HasHome);
        vm.Draft = "Черновик";

        await vm.StartRecordingCommand.ExecuteAsync(kind);
        Assert.True(vm.IsRecording);
        await vm.FinishRecordingCommand.ExecuteAsync(null);

        Assert.Equal(1, uploads);
        Assert.False(vm.IsRecording);
        Assert.Equal("Черновик", vm.Draft);
        Assert.Equal(label, Assert.Single(vm.Messages).Display);
        await vm.StartRecordingCommand.ExecuteAsync(kind);
        await vm.CancelRecordingCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
        Assert.Equal(1, uploads);
        await vm.StartRecordingCommand.ExecuteAsync(kind);
        vm.Detach();
        await Waits.Until(() => !vm.IsRecording && recorder.Cancellations >= 3,
            "recording cancelled when group view detaches");
        Assert.Equal(1, uploads);
    }

    [AvaloniaFact]
    public async Task Group_photo_loads_an_authenticated_inline_thumbnail()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var downloaded = false;
        handler.Send = (request, _) => Task.FromResult(Route(request));
        HttpResponseMessage Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/communities", StringComparison.Ordinal))
                return Payload(new[] { Membership });
            if (request.Method == HttpMethod.Get && path.EndsWith("/home", StringComparison.Ordinal))
                return Payload(Home());
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return Payload(new ChatPageResponse([new ChatMessageResponse(Message, Conversation, UserId, "Аня",
                    "photo.webp", AccountClientTestSupport.Now, "image")], false));
            if (request.Method == HttpMethod.Post && path.EndsWith("/read", StringComparison.Ordinal))
                return Payload(Home().GroupChat);
            if (request.Method == HttpMethod.Get && path.EndsWith("/media", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer " + Access, request.Headers.Authorization?.ToString());
                downloaded = true;
                return new(System.Net.HttpStatusCode.OK)
                { Content = new ByteArrayContent(png) { Headers = { ContentType = new("application/octet-stream") } } };
            }
            return Problem(404, "not_found");
        }

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        await Waits.Until(() => downloaded && vm.Messages.Count == 1 && vm.Messages[0].Preview is not null,
            "group photo preview loaded");
        Assert.InRange(vm.Messages[0].Preview!.PixelSize.Width, 1, 320);
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Finalizing_group_recording_blocks_a_second_capture_until_cleanup_finishes()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            var path when request.Method == HttpMethod.Get && path.EndsWith("/communities", StringComparison.Ordinal)
                => Payload(new[] { Membership }),
            var path when request.Method == HttpMethod.Get && path.EndsWith("/home", StringComparison.Ordinal)
                => Payload(Home()),
            var path when request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal)
                => Payload(new ChatPageResponse([], false)),
            var path when request.Method == HttpMethod.Post && path.EndsWith("/read", StringComparison.Ordinal)
                => Payload(Home().GroupChat),
            var path when request.Method == HttpMethod.Post && path.EndsWith("/media", StringComparison.Ordinal)
                => Payload(new ChatMessageResponse(Message, Conversation, UserId, "Аня", "voice.m4a",
                    AccountClientTestSupport.Now, "voice"), System.Net.HttpStatusCode.Created),
            _ => Problem(404, "not_found")
        });
        var recorder = new BlockingRecorder();
        var vm = new GroupViewModel(services, recorder);
        await vm.ActivateAsync();
        Assert.True(vm.HasHome);

        await vm.StartRecordingCommand.ExecuteAsync("voice");
        var cancelBeforeFinish = recorder.Cancellations;
        var finishing = vm.FinishRecordingCommand.ExecuteAsync(null);
        await recorder.FinishEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsFinalizingRecording);
        Assert.False(vm.StartRecordingCommand.CanExecute("circle"));
        await vm.CancelRecordingCommand.ExecuteAsync(null);
        await vm.StartRecordingCommand.ExecuteAsync("circle");
        Assert.Equal(1, recorder.Starts);
        Assert.Equal(cancelBeforeFinish, recorder.Cancellations);

        recorder.ReleaseFinish();
        await finishing;
        Assert.False(vm.IsFinalizingRecording);
        Assert.True(vm.StartRecordingCommand.CanExecute("circle"));
        Assert.Equal(1, recorder.Starts);
        Assert.Equal(cancelBeforeFinish + 1, recorder.Cancellations);
    }

    private static GroupHomeResponse Home() => new(CommunityId, "О3313", "О3313",
        new ConversationResponse(Conversation, "group", CommunityId, "О3313", null, null, null, 0),
        [new ClassmateResponse(UserId, "student", "Аня", "member", true)], []);

    private sealed class FakeRecorder(string kind) : IChatMediaRecorder
    {
        public long CurrentLength => Media.Length;
        public int Cancellations { get; private set; }
        public Task StartAsync(string value, CancellationToken ct) => Task.CompletedTask;
        public Task<ChatCapturedMedia> FinishAsync(CancellationToken ct)
            => Task.FromResult(new ChatCapturedMedia(kind, kind == "voice" ? "voice.m4a" : "circle.mp4",
                Media.ToArray(), 1500));
        public Task CancelAsync() { Cancellations++; return Task.CompletedTask; }
    }

    private sealed class BlockingRecorder : IChatMediaRecorder
    {
        private readonly TaskCompletionSource<ChatCapturedMedia> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public int Cancellations { get; private set; }
        public long CurrentLength => 12;
        public Task StartAsync(string kind, CancellationToken ct) { Starts++; return Task.CompletedTask; }
        public async Task<ChatCapturedMedia> FinishAsync(CancellationToken ct)
        {
            FinishEntered.TrySetResult();
            return await release.Task.WaitAsync(ct);
        }
        public void ReleaseFinish() => release.TrySetResult(new ChatCapturedMedia("voice", "voice.m4a", Media.ToArray(), 1500));
        public Task CancelAsync() { Cancellations++; return Task.CompletedTask; }
    }
}
