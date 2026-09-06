using System.ComponentModel;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class LocTests
{
    [Fact]
    public void Indexer_And_T_Return_Russian_By_Default()
    {
        var loc = new Loc(new I18nService("ru"));

        Assert.Equal("Сегодня", loc["today"]);
        Assert.Equal("Расписание", loc.T("navSchedule"));
        Assert.Equal("след. 14.09", loc.T("nextShort", "14.09"));
    }

    [Fact]
    public void LocString_Updates_On_Language_Change()
    {
        var loc = new Loc(new I18nService("ru"));
        var s = new LocString(loc, "tomorrow");
        string? changed = null;
        ((INotifyPropertyChanged)s).PropertyChanged += (_, e) => changed = e.PropertyName;

        Assert.Equal("Завтра", s.Value);
        loc.SetLanguage("en");

        Assert.Equal("Tomorrow", s.Value);
        Assert.Equal(nameof(LocString.Value), changed);
    }

    [Fact]
    public void LocString_Is_Cached_Per_Key_And_Follows_Language()
    {
        var loc = new Loc(new I18nService("ru"));
        var a = loc.String("today");
        Assert.Same(a, loc.String("today"));
        Assert.Same(a, loc.String("TODAY")); // keys are case-insensitive in Core
        Assert.Equal("Сегодня", a.Value);
        loc.SetLanguage("en");
        Assert.Equal("Today", a.Value);
    }

    [Theory]
    [InlineData(1, "1 пара")]
    [InlineData(2, "2 пары")]
    [InlineData(4, "4 пары")]
    [InlineData(5, "5 пар")]
    [InlineData(11, "11 пар")]
    [InlineData(21, "21 пара")]
    [InlineData(22, "22 пары")]
    [InlineData(112, "112 пар")]
    public void Plural_Follows_Russian_Rules(int n, string expected)
    {
        var loc = new Loc(new I18nService("ru"));
        Assert.Equal(expected, loc.Plural(n, "lessons1", "lessons2", "lessons5"));
    }

    [Fact]
    public void Plural_English_Has_Two_Forms()
    {
        var loc = new Loc(new I18nService("en"));
        Assert.Equal("1 lesson", loc.Plural(1, "lessons1", "lessons2", "lessons5"));
        Assert.Equal("3 lessons", loc.Plural(3, "lessons1", "lessons2", "lessons5"));
    }

    [Fact]
    public void Every_New_Key_Exists_In_Both_Languages()
    {
        var ru = new I18nService("ru");
        var en = new I18nService("en");
        foreach (var key in NewKeys)
        {
            Assert.NotEqual(key, ru.T(key)); // T returns the key itself when missing
            Assert.NotEqual(key, en.T(key));
        }
    }

    public static readonly string[] NewKeys =
    {
        "navSchedule","navWeek","navSummary","navTools","navTeachers","navMaps","navFriends","navHomework","navSettings",
        "goToday","prevDay","nextDay","lessons1","lessons2","lessons5","weekOf","parityWeek","nextShort",
        "noLessonsDay","noLessonsSunday","nextLessonHint","typeLek","typePr","typeLab","typeKons","typeZach","typeEkz","typeKurs","typePraktika",
        "remote","originalLabel","hwLabel","hwBurningTomorrow","hwBurningToday","hwOverdue","hwDone","hwDueOn",
        "hwInLessons1","hwInLessons2","hwInLessons5","hwMarkDone","hwUndo","hwEdit","hwDelete","hwAdd","hwDeleteConfirm","hwEditTitle",
        "renameTip","mapTip","placeholderTitle","placeholderHint","loadingTitle","themeToggleTip","sidebarToggleTip",
        "groupPickTitle","search","groupSearchHint","select","confirm","delete","updatedChip","errorTitle",
        "bootstrapError","bootstrapHint","retry","friendAbsent","inter100","inter75","inter50","inter25","savedOk","noGroup","noGroupHint",
        "winMinimize","winMaximize","winClose",
        "refreshOk","refreshNone","refreshFail","refreshTip",
        "weekCurrentSuffix","weekOpenDayTip",
        "summaryTotal","summaryByDay","summaryByType","summarySubjects","summaryTeachers","summaryRooms","summaryBothShort",
        "teachersSearchHint","teachersOnlyMine","teachersCount","teachersPick","teachersPickHint","teachersLoading",
        "teachersLoadFail","teachersNoSource","teachersMine","teachersTeachesMine","teachersNotMine",
        "mapNextLesson","mapLessonPrefix","mapPickPlan","mapFloorN","mapInMinutes","mapInHours","mapInDays","mapNow",
        "mapToNext","mapVc","mapDownloadAll","mapOpenFolder","mapVerify","mapCacheStatus","mapDownloaded",
        "mapFullscreen","mapExitFullscreen","mapFit","mapZoomIn","mapZoomOut","mapReset","mapMore","mapNoImage","mapRemoteHint",
        "friendsSubtitle","friendsCount","friendsAdd","friendsMax","friendsNames","friendsEnabled","friendsRemove","friendsRemoveConfirm",
        "friendsEmpty","friendsEmptyHint","friendsColor","friendAdded","intersections","strictnessHint","alwaysShowAll","alwaysShowAllHint",
        "previewTitle","previewNone","strictTick25","strictTick50","strictTick75","strictTick100",
        "hwGroupUrgent","hwGroupBurning","hwGroupApproaching","hwGroupFar","hwGroupOverdue","hwGroupDone",
        "hwOpen1","hwOpen2","hwOpen5","hwDoneCount","hwAddShort","hwEmpty","hwEmptyHint","hwPickSubject","hwPickSubjectHint",
        "setAppearance","setTheme","themeSystem","themeLight","themeDark","setCompactSidebar","setAnimations","setSchedule","setChange",
        "setAutoCheckAt","setNever","setAbout","setVersion","setReleases","setSources","setSourceTimetable","setSourceMaps","setDataFolder",
        "setNotifications","notifEnabled","notifTime1Label","notifTime2Label","notifSave","notifTest","notifBadTime","notifSaved",
        "setSync","syncExport","syncImport","syncShowQr","syncHideQr","syncQrHint","syncQrServerHint","syncLan","syncLanAddress","syncLanFail","syncExported"
    };
}
