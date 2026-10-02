using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Master.Operations;

namespace Sirius.ToolboxUI;

internal sealed class MusicOperationsPage : UserControl
{
    private readonly MasterOperationsService _service;
    private readonly MusicOperationsAdapter _adapter;

    private readonly TextBox _searchBox = new() { PlaceholderText = "搜索乐曲 ID / 标题...", Width = 280 };
    private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
    private readonly DataGridView _grid = CreateGrid();
    private readonly TextBox _titleBox = new() { Width = 320 };
    private readonly NumericUpDown _unlockTypeBox = new() { Minimum = 0, Maximum = 999, Width = 100 };
    private readonly TextBox _unlockTextBox = new() { Width = 420 };
    private readonly Button _saveButton = new() { Text = "保存选中乐曲", AutoSize = true };
    private readonly Button _selectedDefaultButton = new() { Text = "勾选乐曲 -> 默认解锁", AutoSize = true };
    private readonly Button _allPurchaseDefaultButton = new() { Text = "所有购买/剧情购买曲 -> 默认解锁", AutoSize = true };
    private readonly Label _countLabel = new() { AutoSize = true };
    private MasterBusinessRow? _selected;

    public MusicOperationsPage(MasterOperationsService service)
    {
        _service = service;
        _adapter = new MusicOperationsAdapter(service);
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
        var rows = await Task.Run(() => _adapter.List(path, search.Length == 0 ? null : search));
        _grid.Rows.Clear();
        foreach (var row in rows)
        {
            var longProperty = MasterOperationJson.FindProperty(row.Values, "IsLongVersion", "IsLong");
            var coverProperty = MasterOperationJson.FindProperty(row.Values, "MusicCoverType", "CoverType");
            var unlockProperty = MasterOperationsService.FindUnlockTypeProperty(row.Values);
            var unlockTextProperty = MasterOperationsService.FindUnlockTextProperty(row.Values);
            var isLong = longProperty is not null && Convert.ToBoolean(row.Values[longProperty] ?? false);
            var cover = coverProperty is null ? string.Empty : Convert.ToString(row.Values[coverProperty]) ?? string.Empty;
            var unlock = unlockProperty is null ? string.Empty : Convert.ToString(row.Values[unlockProperty]) ?? string.Empty;
            var unlockText = unlockTextProperty is null ? string.Empty : Convert.ToString(row.Values[unlockTextProperty]) ?? string.Empty;
            var index = _grid.Rows.Add(false, row.Key, row.DisplayName, isLong ? "是" : "否", cover, unlock, unlockText);
            _grid.Rows[index].Tag = row;
        }
        _countLabel.Text = $"{rows.Count:N0} 首";
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[1];
        }
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
        top.Controls.Add(new Label { Text = "乐曲", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 6, 12, 0) });
        top.Controls.Add(_searchBox);
        top.Controls.Add(_refreshButton);
        top.Controls.Add(_countLabel);
        top.Controls.Add(_selectedDefaultButton);
        top.Controls.Add(_allPurchaseDefaultButton);

        var detail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 3
        };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.Controls.Add(new Label { Text = "标题", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
        detail.Controls.Add(_titleBox, 1, 0);
        detail.Controls.Add(new Label { Text = "解锁类型", AutoSize = true, Padding = new Padding(8, 6, 6, 0) }, 2, 0);
        detail.Controls.Add(_unlockTypeBox, 3, 0);
        detail.Controls.Add(new Label { Text = "解锁提示", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
        detail.Controls.Add(_unlockTextBox, 1, 1);
        detail.SetColumnSpan(_unlockTextBox, 3);
        detail.Controls.Add(new Label
        {
            Text = "常用值：Default=1，Buy=10，ReadEpisodeAndBuy=11。改为 Default 时会自动清空 UnlockValue / UnlockText。",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        }, 0, 2);
        detail.SetColumnSpan(detail.GetControlFromPosition(0, 2)!, 3);
        detail.Controls.Add(_saveButton, 3, 2);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(detail);
        SplitContainerLayout.ConfigureWhenSized(split, Orientation.Horizontal, 260, 140, 610);

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
        _saveButton.Click += async (_, _) => await SaveSelectedAsync();
        _selectedDefaultButton.Click += async (_, _) => await MakeSelectedDefaultAsync();
        _allPurchaseDefaultButton.Click += async (_, _) => await MakeAllPurchaseDefaultAsync();
    }

    private void LoadSelected()
    {
        _selected = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as MasterBusinessRow : null;
        var row = _selected;
        if (row is null) return;
        _titleBox.Text = row.DisplayName;
        var unlockProperty = MasterOperationsService.FindUnlockTypeProperty(row.Values);
        _unlockTypeBox.Value = unlockProperty is null
            ? 0
            : Math.Clamp(MasterOperationJson.ToInt64(row.Values[unlockProperty]) ?? 0, 0, 999);
        var unlockTextProperty = MasterOperationsService.FindUnlockTextProperty(row.Values);
        _unlockTextBox.Text = unlockTextProperty is null ? string.Empty : Convert.ToString(row.Values[unlockTextProperty]) ?? string.Empty;
    }

    private async Task SaveSelectedAsync()
    {
        var row = _selected;
        if (row is null || ApplyPlanAsync is null) return;
        var patch = new Dictionary<string, object?>(StringComparer.Ordinal);
        var titleProperty = MasterOperationJson.FindProperty(row.Values, "Name", "MusicName", "Title", "DisplayName");
        if (titleProperty is not null) patch[titleProperty] = _titleBox.Text;
        var unlockProperty = MasterOperationsService.FindUnlockTypeProperty(row.Values);
        if (unlockProperty is not null) patch[unlockProperty] = (int)_unlockTypeBox.Value;
        var unlockTextProperty = MasterOperationsService.FindUnlockTextProperty(row.Values);
        if (unlockTextProperty is not null) patch[unlockTextProperty] = _unlockTextBox.Text.Length == 0 ? null : _unlockTextBox.Text;
        if ((int)_unlockTypeBox.Value == 1)
        {
            foreach (var name in row.Values.Keys)
            {
                if (name.Contains("Unlock", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("Value", StringComparison.OrdinalIgnoreCase))
                    patch[name] = null;
            }
            if (unlockTextProperty is not null) patch[unlockTextProperty] = null;
        }
        var plan = _service.CreateRecordPatchPlan("MusicMaster", row.Key, patch, $"编辑乐曲 {row.DisplayName}");
        await ApplyPlanAsync(plan);
    }

    private async Task MakeSelectedDefaultAsync()
    {
        var path = DatabasePath;
        if (path is null || ApplyPlanAsync is null) return;
        var keys = _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells[0].Value is true)
            .Select(row => (row.Tag as MasterBusinessRow)?.Key)
            .Where(key => key is not null)
            .Cast<string>()
            .ToArray();
        if (keys.Length == 0 && _selected is not null) keys = [_selected.Key];
        if (keys.Length == 0) return;
        var plan = await Task.Run(() => _adapter.MakeDefault(path, keys));
        await ApplyPlanAsync(plan);
    }

    private async Task MakeAllPurchaseDefaultAsync()
    {
        var path = DatabasePath;
        if (path is null || ApplyPlanAsync is null) return;
        var plan = await Task.Run(() => _adapter.MakeAllPurchaseLockedDefault(path));
        await ApplyPlanAsync(plan);
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Checked", HeaderText = "选", Width = 45, FillWeight = 25 });
        grid.Columns.Add("Key", "主键");
        grid.Columns.Add("Title", "标题");
        grid.Columns.Add("Long", "Long");
        grid.Columns.Add("CoverType", "类型");
        grid.Columns.Add("UnlockType", "解锁类型");
        grid.Columns.Add("UnlockText", "解锁提示");
        foreach (DataGridViewColumn column in grid.Columns.Cast<DataGridViewColumn>().Skip(1))
            column.ReadOnly = true;
        return grid;
    }
}
