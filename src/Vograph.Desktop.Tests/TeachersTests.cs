using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class TeachersTests : UiTest
{
    private static readonly DateTime Wed9 = new(2026, 9, 9, 12, 0, 0); // even week, Wednesday
    private static string LecturerXml => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-lecturers.xml"));

    private static async Task<TeachersViewModel> Make(TestDb db, ShellViewModel? shell = null)
    {
        shell ??= new ShellViewModel(db.Services);
        await db.Services.Lecturers.LoadXmlAsync(LecturerXml); // no files, no network
        var vm = new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false);
        await vm.LoadAsync();
        return vm;
    }

    [Theory]
    [InlineData("Барт Е.Л.", "Барт Е.Л.", true)]
    [InlineData("Барт Елена Леонидовна", "Барт Е.Л.", true)]   // full name vs short
    [InlineData("Аббу Фадда Т.М.", "Аббу Фадда Т.М.", true)]  // two-word surname
    [InlineData("Бартенев Е.Л.", "Барт Е.Л.", false)]
    [InlineData("Барт А.А.", "Барт Е.Л.", false)]              // same surname, other initials
    [InlineData("Иванов С.П.", "Иванов", true)]                // short form without initials
    public void SameTeacher_Compares_Surname_And_Initials(string lecturerName, string shortName, bool expected) =>
        Assert.Equal(expected, TeacherSearch.SameTeacher(lecturerName, shortName));

    [Fact]
    public async Task My_Teachers_Filter_Search_And_Count()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);

        Assert.True(vm.OnlyMine);
        Assert.Equal(new[] { "Барт Е.Л.", "Лысенко Е.М." }, vm.Items.Select(i => i.Info.Name));
        Assert.All(vm.Items, i => Assert.True(i.IsMine));
        Assert.Equal("2 из 3", vm.CountText);

        vm.OnlyMine = false;
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("3", vm.CountText);

        vm.Query = "физ"; // subject search
        Assert.Equal("Чужой А.А.", Assert.Single(vm.Items).Info.Name);
        vm.Query = "матем";
        Assert.Equal("Барт Е.Л.", Assert.Single(vm.Items).Info.Name);
        vm.Query = "р7"; // department
        Assert.Equal("Лысенко Е.М.", Assert.Single(vm.Items).Info.Name);
        vm.Query = "";
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task Selecting_A_Teacher_Builds_The_Week_With_Parity_Filter()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);
        Assert.Null(vm.Detail);

        vm.Selected = vm.Items.Single(i => i.Info.Id == "1287");
        var d = Assert.IsType<TeacherDetailViewModel>(vm.Detail);
        Assert.Equal("Барт Е.Л.", d.Name);
        Assert.Equal("О6", d.Kafedra);
        Assert.True(d.IsMine);
        Assert.Equal(0, d.ParityIndex);
        Assert.Equal(6, d.Days.Count);
        Assert.Equal(2, d.Days[0].Rows.Count);          // both Mondays
        Assert.True(d.Days[2].IsToday);                 // Wednesday
        var wed = Assert.Single(d.Days[2].Rows);
        Assert.Equal(("14:55", "16:30", "ВЫСШ. МАТЕМАТ", "практика", "ВЦ 280", "А863С", "чет", true),
            (wed.Time, wed.TimeEnd, wed.Name, wed.TypeLabel, wed.Room, wed.Groups, wed.ParityLabel, wed.IsMine));
        Assert.Equal("А863С, А864С", d.Days[0].Rows[0].Groups);

        d.ParityIndex = 1; // odd
        Assert.Single(d.Days[0].Rows);
        Assert.Empty(d.Days[2].Rows);
        d.ParityIndex = 2; // even
        Assert.Single(d.Days[0].Rows);
        Assert.Single(d.Days[2].Rows);
    }

    [Fact]
    public async Task Detail_Says_Whether_The_Lecturer_Teaches_My_Group()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);
        vm.OnlyMine = false;
        vm.Selected = vm.Items.Single(i => i.Info.Id == "1287");
        Assert.Equal("Ведёт у вашей группы", vm.Detail!.MineLine);
        vm.Selected = vm.Items.Single(i => i.Info.Id == "5000");
        Assert.Equal("Не ведёт у вашей группы", vm.Detail!.MineLine);
        Assert.False(vm.Detail.IsMine);
    }

    /// <summary>The «моя» chip also matches by group Number when the lecturer XML carries no IdGroup.</summary>
    [Fact]
    public void Row_Is_Mine_By_Group_Number_Too()
    {
        var ru = new Loc(new I18nService("ru"));
        var lesson = new LecturerLesson { DayOfWeek = 1, Parity = 1, TimeStart = "09:00", TimeEnd = "10:35", DisciplineRaw = "лек ФИЗИКА", TypeRaw = "лек", ClassroomRaw = "493;",
            Groups = { new GroupRef { IdGroup = "", Number = "А863С" } } };
        var detail = new TeacherDetailViewModel(new LecturerInfo { Id = "x", Name = "Кто-то К.К." }, new[] { lesson }, false, "3313", "А863С", false, ru, Wed9);
        Assert.True(Assert.Single(detail.Days[0].Rows).IsMine);
        Assert.Equal("493", detail.Days[0].Rows[0].Room);
    }

    [Fact]
    public void Lecturer_Without_Lessons_Shows_Six_Empty_Days()
    {
        var ru = new Loc(new I18nService("ru"));
        var detail = new TeacherDetailViewModel(new LecturerInfo { Id = "x", Name = "Пустой П.П.", Kafedra = "" }, Array.Empty<LecturerLesson>(), false, "3313", "А863С", false, ru, Wed9);
        Assert.Equal(6, detail.Days.Count);
        Assert.All(detail.Days, d => Assert.Empty(d.Rows));
        Assert.False(detail.HasKafedra);
    }

    [Fact]
    public async Task Detach_Stops_Following_Group_Changes()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = await Make(db, shell);
        Assert.Equal("2 из 3", vm.CountText);
        vm.Detach();

        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "9999"; // no lessons → no «my» lecturers
        db.Services.Db.SaveSettings(s);
        shell.RaiseGroupChanged();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal("2 из 3", vm.CountText); // still the old group: detached
    }

    [Fact]
    public async Task Refilter_Keeps_The_Parity_Segment()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);
        vm.Selected = vm.Items.Single(i => i.Info.Id == "1287");
        vm.Detail!.ParityIndex = 2;
        vm.Query = "барт";
        Assert.NotNull(vm.Detail);
        Assert.Equal("1287", vm.Selected!.Info.Id);
        Assert.Equal(2, vm.Detail!.ParityIndex);
    }

    [Fact]
    public async Task Parity_Labels_Follow_Inversion()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.ParityInvert = true;
        db.Services.Db.SaveSettings(s);
        var vm = await Make(db);
        vm.Selected = vm.Items.Single(i => i.Info.Id == "1609");
        Assert.Equal("чет", Assert.Single(vm.Detail!.Days[0].Rows).ParityLabel); // XML odd shows as the user's even week
    }

    /// <summary>Inversion is flipped in Settings, which saves it and raises ScheduleChanged: the open lecturer
    /// detail has to re-read ParityInvert. Listening to GroupChanged alone leaves the Monday row labelled «нечет»
    /// (and the user's «нечет/чет» segment must survive the rebuild).</summary>
    [Fact]
    public async Task Detail_Follows_A_Parity_Flip_From_Settings()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = await Make(db, shell);
        vm.Selected = vm.Items.Single(i => i.Info.Id == "1609");
        Assert.Equal("нечет", Assert.Single(vm.Detail!.Days[0].Rows).ParityLabel);
        vm.Detail.ParityIndex = 2; // «чет»: that Monday lecture is an odd-week one, so the day goes empty
        Assert.Empty(vm.Detail.Days[0].Rows);

        var settings = new SettingsViewModel(db.Services, shell, () => Wed9);
        await settings.LoadAsync();
        settings.ParityInvert = true; // saves under the gate, then raises ScheduleChanged
        await Waits.Until(() => vm.Detail?.Days[0].Rows.FirstOrDefault()?.ParityLabel == "чет", "parity flip rebuilds the detail");
        Assert.Equal(2, vm.Detail!.ParityIndex); // the segment the user picked survives the rebuild
    }

    [Fact]
    public async Task Missing_Reference_Shows_An_Error_Instead_Of_Throwing()
    {
        using var db = TestDb.Create();
        // The build copies the real TimetableLecturer50.xml next to the test binaries, and the user's own
        // %LocalAppData% may hold a cache: point the store at paths that cannot exist so "no source" is honest.
        db.Services.Lecturers = new LecturerStore(new LecturerService(db.Services.Db, Path.Combine(db.Dir, "no-cache.xml"), Path.Combine(db.Dir, "no-bundled.xml")), db.Services.Log);
        var shell = new ShellViewModel(db.Services);
        var vm = new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false); // nothing loaded, no local files in tests
        await vm.LoadAsync();
        Assert.False(vm.IsLoading);
        Assert.NotNull(vm.LoadError);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Failed_Load_Retries_On_The_Next_Activation()
    {
        using var db = TestDb.Create();
        // Same setup as Missing_Reference_Shows_An_Error_Instead_Of_Throwing: no cache, no bundled copy, no network.
        db.Services.Lecturers = new LecturerStore(new LecturerService(db.Services.Db, Path.Combine(db.Dir, "no-cache.xml"), Path.Combine(db.Dir, "no-bundled.xml")), db.Services.Log);
        var shell = new ShellViewModel(db.Services);
        var vm = new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false);
        await vm.LoadAsync();
        Assert.NotNull(vm.LoadError);
        Assert.Empty(vm.Items);

        // A source becomes available (e.g. a later refresh elsewhere): the next activation must retry, not no-op.
        var store = new LecturerStore(new LecturerService(db.Services.Db), db.Services.Log);
        await store.LoadXmlAsync(LecturerXml);
        db.Services.Lecturers = store;

        await vm.ActivateAsync();
        Assert.Null(vm.LoadError);
        Assert.NotEmpty(vm.Items);
    }

    [Fact]
    public async Task Overlapping_Loads_Share_One_Task()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        await db.Services.Lecturers.LoadXmlAsync(LecturerXml); // no files, no network
        var vm = new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false);

        // Two activations back to back (ShellViewModel.NavigateTo fires ActivateAsync without awaiting, and
        // GetOrCreate hands back this same cached instance) must not race two loads into the shared LecturerService.
        var t1 = vm.LoadAsync();
        var t2 = vm.LoadAsync();
        Assert.Same(t1, t2);
        await t1;
        Assert.Equal(2, vm.Items.Count); // «Только мои», same fixture and default filter as Make()
        Assert.Null(vm.LoadError);

        // Once the in-flight load has completed, the next call must start a fresh one, not replay the old task.
        var t3 = vm.LoadAsync();
        Assert.NotSame(t1, t3);
        await t3;
    }

    [Fact]
    public async Task Retry_Button_Reloads_After_A_Failed_Load()
    {
        using var db = TestDb.Create();
        db.Services.Lecturers = new LecturerStore(new LecturerService(db.Services.Db, Path.Combine(db.Dir, "no-cache.xml"), Path.Combine(db.Dir, "no-bundled.xml")), db.Services.Log);
        var shell = new ShellViewModel(db.Services);
        var vm = new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false);
        await vm.LoadAsync();
        Assert.NotNull(vm.LoadError);

        var store = new LecturerStore(new LecturerService(db.Services.Db), db.Services.Log);
        await store.LoadXmlAsync(LecturerXml);
        db.Services.Lecturers = store;
        await vm.RetryCommand.ExecuteAsync(null); // the «Повторить» button under the error text (T4 #2)

        Assert.Null(vm.LoadError);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task Search_Ignores_The_Lesson_Type_Token()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);
        vm.OnlyMine = false;
        vm.Query = "лек";   // every lecture starts with it; matching the token would list everyone
        Assert.Empty(vm.Items);
        vm.Query = "физ";   // the subject itself still matches
        Assert.Equal("Чужой А.А.", Assert.Single(vm.Items).Info.Name);
    }

    [Fact]
    public async Task Stored_English_Still_Keeps_Russian_Count()
    {
        using var db = TestDb.Create();
        var vm = await Make(db);
        Assert.Equal("2 из 3", vm.CountText);
        db.Services.Loc.SetLanguage("en");
        Assert.Equal("ru", db.Services.Loc.Language);
        Assert.Equal("2 из 3", vm.CountText);
    }

    [Fact]
    public void Teachers_Section_Does_Not_Allow_Network_In_Tests()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        Assert.False(shell.Section<TeachersViewModel>(SectionKey.Teachers).AllowNetwork);
    }

    [AvaloniaFact]
    public async Task Teachers_Render_Both_Themes_And_Click_Selects()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        await db.Services.Lecturers.LoadXmlAsync(LecturerXml);
        shell.Register(SectionKey.Teachers, () => new TeachersViewModel(db.Services, shell, () => Wed9, allowNetwork: false));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.NavigateTo(SectionKey.Teachers);
        var vm = Assert.IsType<TeachersViewModel>(shell.Current);
        await Waits.Until(() => vm.Items.Count > 0, "teacher items");
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "teachers-empty-dark");

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Click(window, list.GetVisualDescendants().OfType<ListBoxItem>().First());
        Assert.NotNull(vm.Detail);
        Pump();
        Frames.Capture(window, "teachers-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "teachers-light");
        AssertNoBindingErrors();
    }
}
