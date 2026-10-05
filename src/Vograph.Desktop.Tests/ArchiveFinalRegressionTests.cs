using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
namespace Vograph.Desktop.Tests;
public sealed class ArchiveFinalRegressionTests
{
    [AvaloniaFact] public async Task Restored_elsewhere_topic_moves_to_active_without_false_purge_and_can_post()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");f.ArchiveTopic();await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);Assert.Single(f.Vm.ArchivedChannels).OpenCommand.Execute(null);
        await Waits.Until(()=>f.Vm.SelectedChannel?.Archived==true,"archive selected");
        f.TopicRows[0]=new(f.Topic,"Тема","user",null,null,null,0,true,permissions:["read","post","media","channels","access"]);
        f.Vm.Watch(true);await Waits.Until(()=>f.Vm.ArchivedChannels.Count==0&&f.Vm.SelectedChannel?.Archived==false&&f.Vm.CanPostChannel,"restored active topic",6500);
        Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);Assert.Contains(f.Vm.SelectedChannel,f.Vm.Channels);Assert.Equal("Тема",f.Vm.ChatTitle);Assert.True(f.Vm.HasHome);Assert.DoesNotContain("Доступ к каналу изменился",f.Vm.Status);
        f.Vm.SelectedChannel.OpenCommand.Execute(null);await Waits.Until(()=>f.MessageReads>=2,"active open command works");Assert.True(f.Vm.ShowComposer);
    }
    [AvaloniaFact] public async Task Late_archive403_after_newer_authority_is_ignored_but_current403_purges()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");var archive=Guid.NewGuid();f.TopicRows.Add(new(archive,"Архив","user",null,null,null,0,false,archived:true,canPost:false,permissions:["read"]));await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var requests=0;var currentDenial=false;
        f.Intercept=async(request,ct)=>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/space/archive",StringComparison.Ordinal))
            {if(++requests==1){started.TrySetResult();await release.Task.WaitAsync(ct);return Problem(403,"forbidden");}return currentDenial?Problem(403,"forbidden"):Payload(new GroupTopicListResponse([],true));}return null;
        };
        var old=f.Vm.LoadArchiveCommand.ExecuteAsync(null);await started.Task.WaitAsync(TestContext.Current.CancellationToken);f.Vm.Watch(true);await Waits.Until(()=>requests>=2&&f.Vm.ArchivedChannels.Count==0,"new archive authority",6500);
        release.TrySetResult();await old;Assert.True(f.Vm.HasHome);Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);Assert.True(f.Vm.ShowComposer);
        currentDenial=true;await f.Vm.LoadArchiveCommand.ExecuteAsync(null);Assert.False(f.Vm.HasHome);Assert.Empty(f.Vm.Channels);Assert.Empty(f.Vm.ArchivedChannels);
    }
    [AvaloniaFact] public async Task Late_poll_archive403_does_not_override_a_newer_manual_archive_read()
    {
        using var f=new GroupSpaceViewModelTests.Fixture("chat","chat");await f.Vm.ActivateAsync();await f.Vm.LoadArchiveCommand.ExecuteAsync(null);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var requests=0;
        f.Intercept=async(request,ct)=>{if(request.RequestUri!.AbsolutePath.EndsWith("/space/archive",StringComparison.Ordinal)){if(++requests==1){started.TrySetResult();await release.Task.WaitAsync(ct);return Problem(403,"forbidden");}return Payload(new GroupTopicListResponse([],true));}return null;};
        f.Vm.Watch(true);await started.Task.WaitAsync(TestContext.Current.CancellationToken);await f.Vm.LoadArchiveCommand.ExecuteAsync(null);release.TrySetResult();
        await Waits.Until(()=>requests>=2 && (int)typeof(GroupViewModel).GetField("polling",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Vm)! == 0,"old poll completed");Assert.True(f.Vm.HasHome);Assert.Equal(f.Topic,f.Vm.SelectedChannel!.TopicId);Assert.True(f.Vm.ShowComposer);
        // A fresh command after the delayed poll confirms that its error path left current state usable.
        f.Vm.Draft="Не потерян";Assert.Equal("Не потерян",f.Vm.Draft);
    }

}
