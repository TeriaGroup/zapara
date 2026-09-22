using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupHomeworkTests
{
    private const string Subject = "лек ВЫСШ. МАТЕМАТ";
    private const string Text = "§5, задачи 1–12";

    private static async Task<(HomeworkShareOutcome Outcome, List<(string Subject, string Text)> Local, List<(string Subject, string Text)> Sent)> Save(
        bool share, bool signedIn, string communityId, bool failSend = false)
    {
        var local = new List<(string, string)>();
        var sent = new List<(string, string)>();
        var outcome = await GroupHomework.SaveEditorAsync(new(Subject, Text, share, true), signedIn, communityId, (savedSubject, savedText) =>
        {
            local.Add((savedSubject, savedText));
            return Task.CompletedTask;
        }, (savedSubject, savedText) =>
        {
            sent.Add((savedSubject, savedText));
            if (failSend) throw new InvalidOperationException("down");
            return Task.CompletedTask;
        });
        return (outcome, local, sent);
    }

    [Fact]
    public async Task Four_Share_Starts_Keep_The_Saved_Task_And_Send_Only_When_Allowed()
    {
        var off = await Save(false, true, "community");
        Assert.Equal([(Subject, Text)], off.Local);
        Assert.Empty(off.Sent);
        Assert.True(off.Outcome.Stored);
        Assert.False(off.Outcome.Sent);
        Assert.Equal("", off.Outcome.Note);

        var signedOut = await Save(true, false, "community");
        Assert.Equal([(Subject, Text)], signedOut.Local);
        Assert.Empty(signedOut.Sent);
        Assert.Equal(GroupHomework.SignInNote, signedOut.Outcome.Note);

        var noCommunity = await Save(true, true, "  ");
        Assert.Equal([(Subject, Text)], noCommunity.Local);
        Assert.Empty(noCommunity.Sent);
        Assert.Equal(GroupHomework.LocalOnlyNote, noCommunity.Outcome.Note);

        var failed = await Save(true, true, "community", failSend: true);
        Assert.Equal([(Subject, Text)], failed.Local);
        Assert.Equal([(Subject, Text)], failed.Sent);
        Assert.True(failed.Outcome.Stored);
        Assert.False(failed.Outcome.Sent);
        Assert.Equal(GroupHomework.FailedNote, failed.Outcome.Note);

        var shared = await Save(true, true, "community");
        Assert.Equal([(Subject, Text)], shared.Local);
        Assert.Equal([(Subject, Text)], shared.Sent);
        Assert.True(shared.Outcome.Sent);
        Assert.Equal(GroupHomework.SharedNote, shared.Outcome.Note);

        var sentOnEdit = false;
        var edited = await GroupHomework.SaveEditorAsync(
            new HomeworkEditorShare(Subject, Text, true, false), true, "community",
            (_, _) => Task.CompletedTask,
            (_, _) => { sentOnEdit = true; return Task.CompletedTask; });
        Assert.False(sentOnEdit);
        Assert.False(edited.Sent);
        Assert.Equal("", edited.Note);
    }

    [Fact]
    public void One_Members_Done_Does_Not_Flip_The_Other_Copy_Or_The_Local_Task()
    {
        var local = new List<LocalHomeworkMark> { new("mine", false) };
        var copies = new List<GroupCopyMark>
        {
            new("hw", "anna", false),
            new("hw", "boris", false),
            new("other", "anna", true),
        };
        var next = GroupHomework.Complete(local, copies, "anna", "hw", true);
        Assert.False(local[0].Done);
        Assert.False(copies[0].Completed);
        Assert.False(copies[1].Completed);
        Assert.False(Assert.Single(next.Local).Done);
        Assert.Equal("mine", Assert.Single(next.Local).Id);
        Assert.True(next.Copies[0].Completed);
        Assert.False(next.Copies[1].Completed);
        Assert.True(next.Copies[2].Completed);
    }
}
