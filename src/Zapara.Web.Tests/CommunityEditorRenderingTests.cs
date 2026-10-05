using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Zapara.Contracts.Communities;
using Zapara.Web.Components.Communities;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

#pragma warning disable BL0006 // This test renderer exercises real compiled Razor parameter/event propagation.
public sealed class CommunityEditorRenderingTests
{
    [Theory]
    [InlineData("Объявления", "Объявление")]
    [InlineData("Задания", "Задание")]
    [InlineData("Опросы", "Опрос")]
    public async Task Editor_receives_null_and_actual_service_error_not_a_literal_expression(string section, string add)
    {
        using var backend = new Backend();
        using var http = new HttpClient(backend) { BaseAddress = new("https://zapara.test/app/") };
        var browser = new MemoryBrowser();
        await using var storage = new BrowserStorage(browser);
        await using var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api); await state.InitializeAsync();
        using var service = new BrowserCommunityService(api, storage, state);
        await service.LoadAsync(); await service.OpenAsync(backend.Id);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(browser)
            .AddSingleton(state).AddSingleton(api).AddSingleton(service).AddSingleton<NavigationManager>(new Navigation()).BuildServiceProvider();
        await using var renderer = new TestRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.Mount<Zapara.Web.Pages.Community>();
            await renderer.Render(page);
            await renderer.Click(page, section);
            await renderer.Click(page, add);
            var editor = renderer.Editor(page);
            Assert.Null(editor.Error);
            Assert.False(renderer.HasAlert(editor.Id));

            Assert.False(await service.SaveAnnouncementAsync(backend.Id, api.Session.FamilyId!.Value, "", "Текст"));
            Assert.NotNull(service.Error);
            await renderer.Render(page);
            editor = renderer.Editor(page);
            Assert.Equal(service.Error, editor.Error);
            Assert.True(renderer.HasAlert(editor.Id));

            await service.OpenAsync(backend.Id);
            await renderer.Render(page);
            editor = renderer.Editor(page);
            Assert.Null(editor.Error);
            Assert.False(renderer.HasAlert(editor.Id));
        });
    }

    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory logging) : Renderer(services, logging)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component render failed", exception);
        public int Mount<T>() where T : IComponent => AssignRootComponentId(InstantiateComponent(typeof(T)));
        public Task Render(int id) => RenderRootComponentAsync(id, ParameterView.Empty);
        public async Task Click(int component, string label)
        {
            var frames = GetCurrentRenderTreeFrames(component);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames.Array[i];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button") continue;
                var end = i + frame.ElementSubtreeLength;
                var children = frames.Array.Skip(i + 1).Take(end - i - 1).ToArray();
                if (!children.Any(c => c.FrameType == RenderTreeFrameType.Text && c.TextContent.Trim() == label || c.FrameType == RenderTreeFrameType.Markup && c.MarkupContent.Trim() == label)) continue;
                var click = children.First(c => c.FrameType == RenderTreeFrameType.Attribute && c.AttributeName == "onclick");
                await DispatchEventAsync(click.AttributeEventHandlerId, null, new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
                return;
            }
            throw new InvalidOperationException("Rendered button was not found: " + label);
        }
        public (int Id, string? Error) Editor(int page)
        {
            var frames = GetCurrentRenderTreeFrames(page);
            foreach (var frame in frames.Array.Take(frames.Count))
                if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    if (frame.Component is CommunityContentEditor content) return (frame.ComponentId, content.Error);
                    if (frame.Component is CommunityPollEditor poll) return (frame.ComponentId, poll.Error);
                }
            throw new InvalidOperationException("Editor not rendered");
        }
        public bool HasAlert(int component)
        {
            var frames = GetCurrentRenderTreeFrames(component);
            foreach (var frame in frames.Array.Take(frames.Count))
            {
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "role" && Equals(frame.AttributeValue, "alert")) return true;
                if (frame.FrameType == RenderTreeFrameType.Component && HasAlert(frame.ComponentId)) return true;
            }
            return false;
        }
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://zapara.test/app/", "https://zapara.test/app/community");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
        protected override void SetNavigationLockState(bool value) { }
    }
    private sealed class Backend : HttpMessageHandler
    {
        public readonly Guid Id = Guid.NewGuid(); private readonly Guid family = Guid.NewGuid();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/session") return Task.FromResult(BrowserApiClientTests.Session(family));
            var community = new CommunityResponse(Id, "Сообщество", "Описание", 1, "headman");
            if (path == "/web-api/communities") return Json(new[] { community });
            if (path == $"/web-api/communities/{Id:D}") return Json(community);
            if (path.EndsWith("/join-request", StringComparison.Ordinal)) return Json(new OwnJoinRequestResponse(null));
            if (path.StartsWith("/web-api/communities/", StringComparison.Ordinal)) return Json(Array.Empty<object>());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
        private static Task<HttpResponseMessage> Json<T>(T value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
    }
}
#pragma warning restore BL0006
