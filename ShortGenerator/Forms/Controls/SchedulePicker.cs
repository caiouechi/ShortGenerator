namespace ShortGenerator.Forms.Controls;

/// <summary>
/// A day and a time to post at, as two drop-downs (the next 30 days, times in 15 minute steps). It replaces the
/// native DateTimePicker: that common control answers accessibility queries through UI Automation from inside
/// comctl32, a COM call-out on the UI thread which, with a debugger attached, surfaced as "External component has
/// thrown an exception" while a client such as Visual Studio watched the dialog.
/// </summary>
public sealed class SchedulePicker : FlowLayoutPanel
{
    private readonly ComboBox _day = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, FlatStyle = FlatStyle.Flat };
    private readonly ComboBox _time = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 74, FlatStyle = FlatStyle.Flat };
    private readonly DateTime _firstDay = DateTime.Today;
    private bool _filling;

    public event EventHandler? ValueChanged;

    public SchedulePicker()
    {
        AutoSize = true; WrapContents = false; Margin = Padding.Empty;
        foreach (var c in new[] { _day, _time })
        {
            c.Margin = new Padding(0, 0, 6, 0);
            c.SelectedIndexChanged += (_, _) => { if (!_filling) ValueChanged?.Invoke(this, EventArgs.Empty); };
        }
        for (int i = 0; i < 30; i++)
        {
            var d = _firstDay.AddDays(i);
            _day.Items.Add(i == 0 ? "Today" : i == 1 ? "Tomorrow" : d.ToString("ddd d MMM"));
        }
        for (int m = 0; m < 24 * 60; m += 15) _time.Items.Add(TimeSpan.FromMinutes(m).ToString(@"hh\:mm"));
        Controls.Add(_day); Controls.Add(_time);
        Value = DateTime.Now;
    }

    /// <summary>The chosen moment, to the quarter hour. Setting a day outside the list snaps to its nearest end.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get
        {
            var day = _firstDay.AddDays(Math.Max(0, _day.SelectedIndex));
            return day.AddMinutes(Math.Max(0, _time.SelectedIndex) * 15);
        }
        set
        {
            _filling = true;
            try
            {
                _day.SelectedIndex = Math.Clamp((int)(value.Date - _firstDay).TotalDays, 0, _day.Items.Count - 1);
                int quarter = (int)Math.Round(value.TimeOfDay.TotalMinutes / 15.0);
                _time.SelectedIndex = Math.Clamp(quarter, 0, _time.Items.Count - 1);
            }
            finally { _filling = false; }
        }
    }

}
