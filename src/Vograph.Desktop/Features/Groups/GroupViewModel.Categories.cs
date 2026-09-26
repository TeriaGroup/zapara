using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    public ObservableCollection<SpaceCategoryBucket> ChannelCategories {get;}=[];
    private readonly Dictionary<Guid,SpaceCategoryBucket> categoryBuckets=[];
    private void RefreshCategories()
    {
        var groups=FilteredChannels.GroupBy(x=>x.CategoryId??Guid.Empty)
            .OrderBy(x=>Categories.FirstOrDefault(c=>c.CategoryId==x.Key)?.Position??-1).ToArray();
        var ordered=new List<SpaceCategoryBucket>();
        foreach(var group in groups)
        {
            if(!categoryBuckets.TryGetValue(group.Key,out var bucket)){bucket=new(group.Key);categoryBuckets[group.Key]=bucket;}
            bucket.Title=Categories.FirstOrDefault(x=>x.CategoryId==group.Key)?.Title??"Темы";
            var topics=group.OrderByDescending(x=>x.Pinned).ThenBy(x=>x.Position).ToArray();
            for(var i=0;i<topics.Length;i++){var old=bucket.Topics.IndexOf(topics[i]);if(old<0)bucket.Topics.Insert(i,topics[i]);else if(old!=i)bucket.Topics.Move(old,i);}
            while(bucket.Topics.Count>topics.Length)bucket.Topics.RemoveAt(bucket.Topics.Count-1);
            ordered.Add(bucket);
        }
        for(var i=0;i<ordered.Count;i++){var old=ChannelCategories.IndexOf(ordered[i]);if(old<0)ChannelCategories.Insert(i,ordered[i]);else if(old!=i)ChannelCategories.Move(old,i);}
        while(ChannelCategories.Count>ordered.Count)ChannelCategories.RemoveAt(ChannelCategories.Count-1);
    }
}
public sealed partial class SpaceCategoryBucket(Guid id):ObservableObject
{
    public Guid Id{get;}=id;
    [ObservableProperty] private string title="";
    [ObservableProperty] private bool expanded=true;
    public ObservableCollection<GroupChannelRow> Topics{get;}=[];
    [RelayCommand] private void Toggle()=>Expanded=!Expanded;
}
