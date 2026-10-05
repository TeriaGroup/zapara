using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zapara.Web.Pages;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

#pragma warning disable BL0006

public sealed class TeacherPageNavigationTests
{
    [Fact]
    public async Task Teacher_page_consumes_its_query_and_opens_the_unique_matching_teacher()
    {
        using var http = new HttpClient(new Input()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api);
        var services = new ServiceCollection().AddLogging().AddSingleton(state).AddSingleton(api)
            .AddSingleton(new BrowserLecturerStore(http, storage)).AddSingleton(new BrowserCatalogViewState()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<Teachers>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Teachers.RequestedQuery)] = "Барт Е.Л." }));
            return page.ToHtmlString();
        });
        Assert.Contains("Барт Елена Леонидовна", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Выберите преподавателя</h2>", WebUtility.HtmlDecode(html));
        Assert.Contains("value=\"Барт Е.Л.\"", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Choosing_a_teacher_requests_the_timetable_once()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timetableCalls = 0;
        using var http = new HttpClient(new Gate(release, started, () => Interlocked.Increment(ref timetableCalls))) { BaseAddress = new("https://zapara.test/app/") };
        var browser = new MemoryBrowser();
        await using var storage = new BrowserStorage(browser);
        var api = new BrowserApiClient(http, storage);
        var state = new WebAppState(http, storage, api);
        await using var services = new ServiceCollection().AddLogging().AddSingleton<Microsoft.JSInterop.IJSRuntime>(browser).AddSingleton(state).AddSingleton(api)
            .AddSingleton(new BrowserLecturerStore(http, storage)).AddSingleton(new BrowserCatalogViewState()).BuildServiceProvider();
        await using var renderer = new TeacherRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.Mount<Teachers>();
            await renderer.Render(page);
            for (var i = 0; i < 50 && !renderer.Contains(page, "Барт Елена Леонидовна"); i++) await Task.Delay(20);
            var click = renderer.Click(page, "Барт Елена Леонидовна");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.TrySetResult();
            await click.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Volatile.Read(ref timetableCalls));
        });
    }
    private sealed class Gate(TaskCompletionSource release, TaskCompletionSource started, Func<int> count) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<Timetable><Lecturer IdLecturer=\"one\" LecturerName=\"Барт Елена Леонидовна\" Kafedra=\"О6\"/></Timetable>") };
            if (request.RequestUri.AbsolutePath.Contains("/timetable", StringComparison.Ordinal))
            {
                count();
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class TeacherRenderer(IServiceProvider services, ILoggerFactory logging) : Renderer(services, logging)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component render failed", exception);
        public int Mount<T>() where T : IComponent => AssignRootComponentId(InstantiateComponent(typeof(T)));
        public Task Render(int id) => RenderRootComponentAsync(id, ParameterView.Empty);
        public bool Contains(int component, string text)
        {
            var frames = GetCurrentRenderTreeFrames(component);
            return frames.Array.Take(frames.Count).Any(frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent.Contains(text, StringComparison.Ordinal));
        }
        public async Task Click(int component, string label)
        {
            var frames = GetCurrentRenderTreeFrames(component);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames.Array[i];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button") continue;
                var children = frames.Array.Skip(i + 1).Take(frame.ElementSubtreeLength - 1).ToArray();
                if (!children.Any(child => child.FrameType == RenderTreeFrameType.Text && child.TextContent.Contains(label, StringComparison.Ordinal))) continue;
                var click = children.First(child => child.FrameType == RenderTreeFrameType.Attribute && child.AttributeName == "onclick");
                await DispatchEventAsync(click.AttributeEventHandlerId, null, new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
                return;
            }
            throw new InvalidOperationException("Rendered button was not found: " + label);
        }
    }

    private sealed class Input : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(".xml") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<Timetable><Lecturer IdLecturer=\"one\" LecturerName=\"Барт Елена Леонидовна\" Kafedra=\"О6\"/></Timetable>") } : new(HttpStatusCode.NotFound));
    }
}
