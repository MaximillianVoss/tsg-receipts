using Tsg.Domain;

namespace Tsg.Desktop;

internal static class Ui
{
    public static readonly Color Navy = Color.FromArgb(31, 51, 78);
    public static readonly Color Blue = Color.FromArgb(41, 105, 172);
    public static string Role(UserRole role) => role switch
    { UserRole.Administrator => "Администратор", UserRole.Accountant => "Бухгалтер", _ => "Жилец" };
    public static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 35, MinimumSize = new Size(110, 35),
            Padding = new Padding(8, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = Blue, ForeColor = Color.White,
            Margin = new Padding(6), UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        return button;
    }
    public static TextBox Text(int width = 170) => new() { Width = width, Margin = new Padding(5), MaxLength = 200 };
    public static NumericUpDown Number(decimal min = 0, decimal max = 1_000_000_000m, int decimals = 2) => new()
    { Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 125, ThousandsSeparator = true, Margin = new Padding(5) };
    public static DateTimePicker Month() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM",
        ShowUpDown = true, Width = 105, Value = new DateTime(2026, 10, 1), MinDate = new DateTime(2000, 1, 1),
        MaxDate = new DateTime(2099, 12, 1), Margin = new Padding(5) };
    public static string Period(DateTimePicker month) => month.Value.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
    public static ComboBox Combo(int width = 235) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        panel.Controls.AddRange(controls); return panel;
    }
    public static Control Field(string label, Control input)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(2) };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(5, 0, 5, 0) });
        panel.Controls.Add(input); return panel;
    }
    public static DataGridView Grid(params string[] headings)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Navy; grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(221, 235, 250);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.DefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);
        foreach (var heading in headings) grid.Columns.Add("c" + grid.Columns.Count, heading);
        return grid;
    }
    public static T? Selected<T>(DataGridView grid) where T : class => grid.CurrentRow?.Tag as T;
    public static void Fill<T>(DataGridView grid, IEnumerable<T> items, Func<T, object[]> values)
    {
        grid.Rows.Clear();
        foreach (var item in items) { var index = grid.Rows.Add(values(item)); grid.Rows[index].Tag = item; }
        if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[0].Cells[0];
    }
    public static void Bind<T>(ComboBox combo, IEnumerable<T> items, Func<T, string> label) where T : class
    {
        var previous = (combo.SelectedItem as Choice<T>)?.Value;
        var choices = items.Select(x => new Choice<T>(x, label(x))).ToList();
        combo.DataSource = choices;
        if (previous is not null)
        {
            var found = choices.FindIndex(c => Equals(c.Value, previous));
            if (found >= 0) combo.SelectedIndex = found;
        }
    }
    public static T ChoiceValue<T>(ComboBox combo) where T : class => (combo.SelectedItem as Choice<T>)?.Value
        ?? throw new AppException("Выберите значение из списка.");
    private sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
}
