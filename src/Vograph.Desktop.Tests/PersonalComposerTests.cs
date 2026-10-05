using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Social;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class PersonalComposerTests
{
    private static readonly Guid Conversation = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static SocialMessageResponse Message(Guid? id = null, string body = "Исходный текст") => new(id ?? Guid.NewGuid(), StaffId, "Я", "text", body,
        null, null, null, null, Now, null, null, null, false, false, null, []);

    private sealed class Fixture : IDisposable
    {
        private readonly ProfileTestDirectory directory = new();
        public readonly AppServices Services;
        public readonly AccountClientHandler Handler = new();
        private readonly HttpClient http;
        private readonly SocialHttpClient social;
        public readonly ChatInboxViewModel Inbox;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Send;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? ReadMessages;
        public Fixture()
        {
            Services = AppServices.Create(directory.Root, () => false);
            Services.AllowNetwork = false;
            http = new(Handler);
            social = new(http, Root);
            Services.UseSocial(social, _ => Task.FromResult<string?>(Access));
            Handler.Send = (request, _) => request.Method == HttpMethod.Post && Send is not null ? Send(request)
                : request.RequestUri!.AbsolutePath.EndsWith("/messages") && ReadMessages is not null ? ReadMessages(request)
                : Task.FromResult(AccountClientTestSupport.Json(request.RequestUri!.AbsolutePath.EndsWith("/home")
                    ? (object)new SocialHomeResponse("CODE", [new(PromotedId, "one", "Первый", Conversation, null, Now, 0),
                        new(Guid.NewGuid(), "two", "Второй", Other, null, Now, 0)], [], [])
                    : new SocialPageResponse([Message()], false)));
            Inbox = new(Services, new ShellViewModel(Services));
        }
        public async Task Open(Guid id)
        {
            Inbox.Chats.Single(row => row.ConversationId == id).OpenCommand.Execute(null);
            await Waits.Until(() => Inbox.Messages.Count == 1, "conversation loaded");
        }
        public void Dispose() { social.Dispose(); http.Dispose(); Services.Dispose(); directory.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Initial_history_started_before_a_send_cannot_remove_the_acknowledged_message()
    {
        using var f = new Fixture();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadMessages = _ => release.Task;
        var sent = Message();
        f.Send = _ => Task.FromResult(AccountClientTestSupport.Json(sent, System.Net.HttpStatusCode.Created));
        await f.Inbox.ActivateAsync();
        f.Inbox.Chats.Single(row => row.ConversationId == Conversation).OpenCommand.Execute(null);
        await Waits.Until(() => f.Inbox.LoadingMessages, "initial history pending");
        f.Inbox.Draft = "Новое сообщение";
        await f.Inbox.SendCommand.ExecuteAsync(null);
        release.SetResult(AccountClientTestSupport.Json(new SocialPageResponse([], false)));
        await Waits.Until(() => !f.Inbox.LoadingMessages, "history settled");
        Assert.Contains(f.Inbox.Messages, row => row.Id == sent.MessageId);
    }

    [AvaloniaFact]
    public async Task Reload_fetched_before_an_edit_cannot_restore_the_old_body()
    {
        using var f = new Fixture();
        var old = Message();
        f.ReadMessages = _ => Task.FromResult(AccountClientTestSupport.Json(new SocialPageResponse([old], false)));
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadMessages = _ => release.Task;
        var reload = f.Inbox.ReloadMessagesCommand.ExecuteAsync(null);
        await Waits.Until(() => f.Inbox.LoadingMessages, "reload pending");
        f.Inbox.Messages[0].EditCommand.Execute(null);
        f.Inbox.Draft = "Исправленный текст";
        f.Send = _ => Task.FromResult(AccountClientTestSupport.Json(Message(old.MessageId, "Исправленный текст")));
        await f.Inbox.SendCommand.ExecuteAsync(null);
        release.SetResult(AccountClientTestSupport.Json(new SocialPageResponse([old], false)));
        await reload;
        Assert.Equal("Исправленный текст", Assert.Single(f.Inbox.Messages).Display);
    }

    [AvaloniaFact]
    public async Task Pending_text_send_disables_attachment_and_recording_actions()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = _ => release.Task;
        f.Inbox.Draft = "Сообщение";
        var pending = f.Inbox.SendCommand.ExecuteAsync(null);
        var attachmentAllowed = f.Inbox.AttachCommand.CanExecute("file");
        var recordingAllowed = f.Inbox.StartRecordingCommand.CanExecute("voice");
        release.SetResult(AccountClientTestSupport.Json(Message(), System.Net.HttpStatusCode.Created));
        await pending;
        Assert.False(attachmentAllowed);
        Assert.False(recordingAllowed);
        Assert.Equal("", f.Inbox.Draft);
        Assert.True(f.Inbox.AttachCommand.CanExecute("file"));
    }

    [AvaloniaFact]
    public async Task Switching_conversations_preserves_their_text()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Первый черновик";
        await f.Open(Other); f.Inbox.Draft = "Второй черновик";
        await f.Open(Conversation);
        Assert.Equal("Первый черновик", f.Inbox.Draft);
    }

    [AvaloniaFact]
    public async Task Successful_send_clears_the_exact_raw_draft_with_outer_spaces()
    {
        using var f = new Fixture();
        f.Send = _ => Task.FromResult(AccountClientTestSupport.Json(Message(), System.Net.HttpStatusCode.Created));
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "  Привет\nДруг  ";
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.Equal("", f.Inbox.Draft);
    }

    [AvaloniaFact]
    public async Task Reply_preserves_text_and_edit_cancel_restores_ordinary_draft()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Мой текст";
        f.Inbox.Messages[0].ReplyCommand.Execute(null);
        Assert.Equal("Мой текст", f.Inbox.Draft);
        await f.Open(Other); await f.Open(Conversation);
        Assert.True(f.Inbox.HasComposerAction);
        Assert.Equal("Мой текст", f.Inbox.Draft);
        f.Inbox.CancelComposerActionCommand.Execute(null);
        f.Inbox.Messages[0].EditCommand.Execute(null);
        f.Inbox.Draft = "Правка";
        await f.Open(Other); await f.Open(Conversation);
        f.Inbox.CancelComposerActionCommand.Execute(null);
        Assert.Equal("Мой текст", f.Inbox.Draft);
        Assert.False(f.Inbox.HasComposerAction);
    }

    [AvaloniaFact]
    public async Task Late_success_settles_origin_after_switch_without_clearing_other_text()
    {
        using var f = new Fixture();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = _ => release.Task;
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Отправлено";
        var pending = f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.True(f.Inbox.Sending);
        await f.Open(Other); f.Inbox.Draft = "Второй текст";
        release.SetResult(AccountClientTestSupport.Json(Message(), System.Net.HttpStatusCode.Created));
        await pending;
        Assert.Equal("Второй текст", f.Inbox.Draft);
        await f.Open(Conversation);
        Assert.Equal("", f.Inbox.Draft);
    }

    [AvaloniaFact]
    public async Task Late_success_keeps_newer_typing_and_mode_even_when_text_returns_to_same_value()
    {
        using var f = new Fixture();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = _ => release.Task;
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Первый";
        var pending = f.Inbox.SendCommand.ExecuteAsync(null);
        f.Inbox.Draft = "Второй"; f.Inbox.Draft = "Первый";
        f.Inbox.Messages[0].ReplyCommand.Execute(null);
        release.SetResult(AccountClientTestSupport.Json(Message(), System.Net.HttpStatusCode.Created));
        await pending;
        Assert.Equal("Первый", f.Inbox.Draft);
        Assert.True(f.Inbox.HasComposerAction);
    }

    [AvaloniaFact]
    public async Task Failed_send_preserves_text_and_error_through_refresh_and_switch()
    {
        using var f = new Fixture();
        f.Send = _ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Повторить";
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.NotEmpty(f.Inbox.ComposerError);
        await f.Inbox.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(f.Inbox.ComposerError);
        await f.Open(Other); await f.Open(Conversation);
        Assert.Equal("Повторить", f.Inbox.Draft);
        Assert.NotEmpty(f.Inbox.ComposerError);
    }

    [AvaloniaFact]
    public async Task Oversized_text_is_retained_but_cannot_be_sent_and_separate_profile_has_no_draft()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = new string('я', 2001);
        Assert.False(f.Inbox.SendCommand.CanExecute(null));
        Assert.NotEmpty(f.Inbox.DraftValidation);
        Assert.Equal(2001, f.Inbox.Draft.Length);
        using var other = new Fixture();
        await other.Inbox.ActivateAsync(); await other.Open(Conversation);
        Assert.Equal("", other.Inbox.Draft);
    }

    [AvaloniaFact]
    public async Task Successful_edit_restores_saved_ordinary_text()
    {
        using var f = new Fixture();
        f.Send = _ => Task.FromResult(AccountClientTestSupport.Json(Message()));
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Несохранённый обычный текст";
        f.Inbox.Messages[0].EditCommand.Execute(null);
        f.Inbox.Draft = "Правка";
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.Equal("Несохранённый обычный текст", f.Inbox.Draft);
        Assert.False(f.Inbox.HasComposerAction);
    }

    [AvaloniaFact]
    public async Task Cancel_edit_restores_the_reply_target_with_its_text()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Ответ с черновиком";
        f.Inbox.Messages[0].ReplyCommand.Execute(null);
        f.Inbox.Messages[0].EditCommand.Execute(null);
        await f.Open(Other); await f.Open(Conversation);
        f.Inbox.CancelComposerActionCommand.Execute(null);
        Assert.Equal("Ответ с черновиком", f.Inbox.Draft);
        Assert.Equal("Ответ", f.Inbox.ActionCaption);
        Assert.True(f.Inbox.HasComposerAction);
    }

    [AvaloniaFact]
    public async Task Successful_edit_restores_reply_target_and_the_next_send_uses_it()
    {
        using var f = new Fixture();
        var bodies = new List<string>();
        f.Send = async request =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return AccountClientTestSupport.Json(Message(), request.RequestUri!.AbsolutePath.EndsWith("/edit")
                ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Created);
        };
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        var target = f.Inbox.Messages[0].Id;
        f.Inbox.Draft = "Ответ с черновиком";
        f.Inbox.Messages[0].ReplyCommand.Execute(null);
        f.Inbox.Messages[0].EditCommand.Execute(null);
        f.Inbox.Draft = "Правка";
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.Equal("Ответ с черновиком", f.Inbox.Draft);
        Assert.Equal("Ответ", f.Inbox.ActionCaption);
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.Contains(target.ToString("D"), bodies[1]);
    }

    [AvaloniaFact]
    public async Task Emoji_and_crlf_count_as_unicode_characters_and_invalid_controls_remain_for_correction()
    {
        using var f = new Fixture();
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = string.Concat(Enumerable.Repeat("😀", 1998)) + "\r\nя";
        Assert.True(f.Inbox.SendCommand.CanExecute(null));
        Assert.Equal("2000 / 2000", f.Inbox.DraftLengthHint);
        f.Inbox.Draft += "я";
        Assert.False(f.Inbox.SendCommand.CanExecute(null));
        f.Inbox.Draft = "Текст\0с ошибкой";
        Assert.False(f.Inbox.SendCommand.CanExecute(null));
        Assert.NotEmpty(f.Inbox.DraftValidation);
        Assert.Contains('\0', f.Inbox.Draft);
    }

    [AvaloniaFact]
    public async Task Pending_send_blocks_duplicate_and_disposed_profile_ignores_acknowledgement()
    {
        using var f = new Fixture();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        f.Send = _ => { requests++; return release.Task; };
        await f.Inbox.ActivateAsync(); await f.Open(Conversation);
        f.Inbox.Draft = "Отправка";
        var pending = f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.False(f.Inbox.SendCommand.CanExecute(null));
        await f.Inbox.SendCommand.ExecuteAsync(null);
        Assert.Equal(1, requests);
        f.Services.Work.Suspend();
        release.SetResult(AccountClientTestSupport.Json(Message(), System.Net.HttpStatusCode.Created));
        await pending;
        Assert.Equal("Отправка", f.Inbox.Draft);
    }
}
