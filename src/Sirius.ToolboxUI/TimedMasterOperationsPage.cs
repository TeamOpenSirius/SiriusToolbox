using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Master.Operations;

namespace Sirius.ToolboxUI;

internal sealed class TimedMasterOperationsPage : UserControl
{
    private readonly string _caption;
    private readonly string _tableName;
    private readonly MasterOperationsService _service;
    private readonly Func<string, string?, IReadOnlyList<MasterBusinessRow>> _list;
    private readonly Func<string, string, DateTimeOffset, bool, MasterOperationPlan> _extend;

    private readonly TextBox _searchBox = new() { PlaceholderText = "搜索 ID / 名称...", Width = 280 };
    private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
    private readonly DataGridView _grid = CreateGrid();
    private readonly TextBox _nameBox = new() { Width = 320 };
    private readonly DateTimePicker _startPicker = CreateDatePicker();
    private readonly DateTimePicker _endPicker = CreateDatePicker();
    private readonly DateTimePicker _forceEndPicker = CreateDatePicker();
    private readonly Button _saveButton = new() { Text = "保存基本字段", AutoSize = true };
    private readonly Button _saveRelatedButton = new() { Text = "保存并同步关联时间", AutoSize = true };
    private readonly DateTimePicker _extendPicker = CreateDatePicker(checkedByDefault: true);
    private readonly Button _extendOnlyButton = new() { Text = "延长本条", AutoSize = true };
    private readonly Button _extendRelatedButton = new() { Text = "延长本条 + 关联数据", AutoSize = true };
    private readonly Label _infoLabel = new() { AutoSize = true };

    private MasterBusinessRow? _selected;

    public TimedMasterOperationsPage(
        string caption,
        string tableName,
        MasterOperationsService service,
        Func<string, string?, IReadOnlyList<MasterBusinessRow>> list,
        Func<string, string, DateTimeOffset, bool, MasterOperationPlan> extend)
    {
        _caption = caption;
        _tableName = tableName;
        _service = service;
        _list = list;
        _extend = extend;
        Dock = DockStyle.Fill;
        BuildUi();
        WireEvents();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? DatabasePath { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<MasterOperationPlan, Task>? ApplyPlanAsync { get; set; }

    public async Task RefreshAsync()
    {
        var path = DatabasePath;
        if (path is null) return;
        var search = _searchBox.Text.Trim();
        var rows = await Task.Run(() => _list(path, search.Length == 0 ? null : search));
        Populate(rows);
    }

    private void BuildUi()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        top.Controls.Add(new Label { Text = _caption, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 6, 12, 0) });
        top.Controls.Add(_searchBox);
        top.Controls.Add(_refreshButton);
        top.Controls.Add(_infoLabel);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 4,
            AutoSize = false
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        details.Controls.Add(new Label { Text = "名称", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
        details.Controls.Add(_nameBox, 1, 0);
        var savePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        savePanel.Controls.Add(_saveButton);
        savePanel.Controls.Add(_saveRelatedButton);
        details.Controls.Add(savePanel, 2, 0);
        details.SetColumnSpan(savePanel, 2);

        details.Controls.Add(new Label { Text = "开始时间 (UTC)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
        details.Controls.Add(_startPicker, 1, 1);
        details.Controls.Add(new Label { Text = "结束时间 (UTC)", AutoSize = true, Padding = new Padding(10, 6, 6, 0) }, 2, 1);
        details.Controls.Add(_endPicker, 3, 1);

        details.Controls.Add(new Label { Text = "强制结束 (UTC)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 2);
        details.Controls.Add(_forceEndPicker, 1, 2);

        var extendPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        extendPanel.Controls.Add(new Label { Text = "运营延长到 (UTC)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        extendPanel.Controls.Add(_extendPicker);
        extendPanel.Controls.Add(_extendOnlyButton);
        extendPanel.Controls.Add(_extendRelatedButton);
        details.Controls.Add(extendPanel, 2, 2);
        details.SetColumnSpan(extendPanel, 2);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(details);
        SplitContainerLayout.ConfigureWhenSized(split, Orientation.Horizontal, 220, 170, 500);

        Controls.Add(split);
        Controls.Add(top);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RefreshAsync();
        };
        _grid.SelectionChanged += (_, _) => LoadSelected();
        _saveButton.Click += async (_, _) => await SaveSelectedAsync(includeRelated: false);
        _saveRelatedButton.Click += async (_, _) => await SaveSelectedAsync(includeRelated: true);
        _extendOnlyButton.Click += async (_, _) => await ExtendAsync(includeRelated: false);
        _extendRelatedButton.Click += async (_, _) => await ExtendAsync(includeRelated: true);
    }

    private void Populate(IReadOnlyList<MasterBusinessRow> rows)
    {
        _grid.Rows.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            var index = _grid.Rows.Add(
                row.Key,
                row.DisplayName,
                FormatDate(row.Start),
                FormatDate(row.End),
                FormatDate(row.ForceEnd),
                row.Status(now));
            _grid.Rows[index].Tag = row;
        }
        _infoLabel.Text = $"{rows.Count:N0} 条";
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        else
        {
            _selected = null;
            ClearDetails();
        }
    }

    private void LoadSelected()
    {
        _selected = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as MasterBusinessRow : null;
        var row = _selected;
        if (row is null)
        {
            ClearDetails();
            return;
        }
        _nameBox.Text = row.DisplayName;
        SetPicker(_startPicker, row.Start);
        SetPicker(_endPicker, row.End);
        SetPicker(_forceEndPicker, row.ForceEnd);
        var suggested = row.ForceEnd ?? row.End ?? DateTimeOffset.UtcNow;
        if (suggested < DateTimeOffset.UtcNow) suggested = DateTimeOffset.UtcNow;
        suggested = suggested.AddMonths(1);
        _extendPicker.Checked = true;
        _extendPicker.Value = ClampPickerDate(suggested.UtcDateTime);
    }

    private async Task SaveSelectedAsync(bool includeRelated)
    {
        var row = _selected;
        var path = DatabasePath;
        if (row is null || path is null || ApplyPlanAsync is null) return;
        var name = _nameBox.Text;
        var start = PickerValue(_startPicker);
        var end = PickerValue(_endPicker);
        var forceEnd = PickerValue(_forceEndPicker);
        var plan = includeRelated
            ? await Task.Run(() => _service.CreateTimedUpdatePlan(
                path, _tableName, row.Key, name, start, end, forceEnd, includeRelated: true))
            : _service.CreateRecordPatchPlan(
                _tableName,
                row.Key,
                _service.CreateCommonFieldPatch(row, name, start, end, forceEnd),
                $"编辑 {_caption}: {row.DisplayName}");
        await ApplyPlanAsync(plan);
    }

    private async Task ExtendAsync(bool includeRelated)
    {
        var row = _selected;
        var path = DatabasePath;
        if (row is null || path is null || ApplyPlanAsync is null || !_extendPicker.Checked) return;
        var plan = await Task.Run(() => _extend(path, row.Key, PickerValue(_extendPicker)!.Value, includeRelated));
        await ApplyPlanAsync(plan);
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add("Key", "主键");
        grid.Columns.Add("Name", "名称");
        grid.Columns.Add("Start", "开始 (UTC)");
        grid.Columns.Add("End", "结束 (UTC)");
        grid.Columns.Add("ForceEnd", "强制结束 (UTC)");
        grid.Columns.Add("Status", "状态");
        return grid;
    }

    internal static DateTimePicker CreateDatePicker(bool checkedByDefault = false)
    {
        return new DateTimePicker
        {
            Width = 210,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm:ss",
            ShowCheckBox = true,
            Checked = checkedByDefault,
            MinDate = new DateTime(2000, 1, 1),
            MaxDate = new DateTime(2299, 12, 31)
        };
    }

    internal static DateTimeOffset? PickerValue(DateTimePicker picker)
    {
        if (!picker.Checked) return null;
        return new DateTimeOffset(DateTime.SpecifyKind(picker.Value, DateTimeKind.Utc));
    }

    internal static void SetPicker(DateTimePicker picker, DateTimeOffset? value)
    {
        picker.Checked = value is not null;
        if (value is not null) picker.Value = ClampPickerDate(value.Value.UtcDateTime);
    }

    internal static DateTime ClampPickerDate(DateTime value)
    {
        if (value < new DateTime(2000, 1, 1)) return new DateTime(2000, 1, 1);
        if (value > new DateTime(2299, 12, 31)) return new DateTime(2299, 12, 31);
        return value;
    }

    internal static string FormatDate(DateTimeOffset? value)
        => value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    private void ClearDetails()
    {
        _nameBox.Clear();
        _startPicker.Checked = false;
        _endPicker.Checked = false;
        _forceEndPicker.Checked = false;
    }
}
