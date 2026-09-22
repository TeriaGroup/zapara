using System.Text;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkFileStoreTests
{
    [Fact]
    public void A_Removed_Draft_File_Does_Not_Come_Back_On_Save()
    {
        var root = Path.Combine(Path.GetTempPath(), "zapara-hw-" + Guid.NewGuid().ToString("N"));
        var store = new HomeworkFileStore(root);
        try
        {
            var kept = store.StageBytes("draft", "document", "конспект.pdf", Encoding.UTF8.GetBytes("привет"), 0);
            var dropped = store.StageBytes("draft", "document", "черновик.txt", Encoding.UTF8.GetBytes("нет"), 1);
            store.DiscardFile("draft", dropped.Id);
            store.Commit("draft", 7, Array.Empty<string>());
            var left = store.List(7);
            Assert.Equal(new[] { "конспект.pdf" }, left.Select(file => file.Name).ToArray());
            Assert.Equal("привет", File.ReadAllText(store.PathOf(7, kept.Id)!));
            store.DeleteHomework(7);
            Assert.Empty(store.List(7));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_Crafted_Id_Stays_Inside_The_Homework_Folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "zapara-hw-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "zapara-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "secret.txt");
        File.WriteAllText(victim, "keep");
        var store = new HomeworkFileStore(root);
        try
        {
            store.Commit("draftsafe1", 7, new[] { "..\\secret.txt", victim });
            Assert.True(File.Exists(victim));
            Assert.Null(store.PathOf(7, "..\\secret.txt"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}
