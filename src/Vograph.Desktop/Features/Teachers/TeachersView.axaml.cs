using Avalonia.Controls;

namespace Vograph.Desktop.Features.Teachers;

public partial class TeachersView : UserControl
{
    public TeachersView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        var compact = Bounds.Width > 0 && Bounds.Width < 850;
        TeachersColumns.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "320,24,*");
        TeachersColumns.RowDefinitions = new RowDefinitions(compact ? "Auto,*" : "*");
        Grid.SetColumn(TeachersDetail, compact ? 0 : 2);
        Grid.SetRow(TeachersDetail, compact ? 1 : 0);
        TeachersList.MaxHeight = compact ? 300 : double.PositiveInfinity;
    }
}
