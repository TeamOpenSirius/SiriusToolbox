using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Master.Operations;

namespace Sirius.ToolboxUI;

internal sealed class ExchangeShopOperationsPage : UserControl
{
    private readonly MasterOperationsService _service;
    private readonly ExchangeShopOperationsAdapter _adapter;

    private readonly TextBox _searchBox = new() { PlaceholderText = "搜索商店 ID / 名称...", Width = 280 };
    private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
    private readonly DataGridView _shopGrid = CreateShopGrid();
    private readonly DataGridView _itemGrid = CreateItemGrid();
    private readonly TextBox _nameBox = new() { Width = 300 };
    private readonly DateTimePicker _startPicker = TimedMasterOperationsPage.CreateDatePicker();
    private readonly DateTimePicker _endPicker = TimedMasterOperationsPage.CreateDatePicker();
    private readonly Button _saveShopButton = new() { Text = "保存商店基本字段", AutoSize = true };
    private readonly DateTimePicker _extendPicker = TimedMasterOperationsPage.CreateDatePicker(checkedByDefault: true);
    private readonly Button _extendShopButton = new() { Text = "仅延长商店", AutoSize = true };
    private readonly Button _extendAllButton = new() { Text = "延长商店 + 全部商品", AutoSize = true };
    private readonly Button _extendItemButton = new() { Text = "仅延长选中商品", AutoSize = true };
    private readonly Label _countLabel = new() { AutoSize = true };

    private MasterBusinessRow? _selectedShop;
    private MasterNestedRow? _selectedItem;

    public ExchangeShopOperationsPage(MasterOperationsService service)
    {
        _service = service;
        _adapter = new ExchangeShopOperationsAdapter(service);
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
        _shopGrid.Rows.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            var index = _shopGrid.Rows.Add(
                row.Key,
                row.DisplayName,
                TimedMasterOperationsPage.FormatDate(row.Start),
                TimedMasterOperationsPage.FormatDate(row.End ?? row.ForceEnd),
                row.Status(now));
            _shopGrid.Rows[index].Tag = row;
        }
        _countLabel.Text = $"{rows.Count:N0} 个商店";
        if (_shopGrid.Rows.Count > 0)
        {
            _shopGrid.Rows[0].Selected = true;
            _shopGrid.CurrentCell = _shopGrid.Rows[0].Cells[0];
        }
        else
        {
            _selectedShop = null;
            _itemGrid.Rows.Clear();
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
        top.Controls.Add(new Label { Text = "交换商店", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 6, 12, 0) });
        top.Controls.Add(_searchBox);
        top.Controls.Add(_refreshButton);
        top.Controls.Add(_countLabel);

        var grids = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        grids.Panel1.Controls.Add(_shopGrid);
        grids.Panel2.Controls.Add(_itemGrid);
        SplitContainerLayout.ConfigureWhenSized(grids, Orientation.Vertical, 320, 320, 650);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 3
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.Controls.Add(new Label { Text = "商店名称", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
        details.Controls.Add(_nameBox, 1, 0);
        details.Controls.Add(_saveShopButton, 2, 0);
        details.SetColumnSpan(_saveShopButton, 2);
        details.Controls.Add(new Label { Text = "开始 (UTC)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
        details.Controls.Add(_startPicker, 1, 1);
        details.Controls.Add(new Label { Text = "结束 (UTC)", AutoSize = true, Padding = new Padding(10, 6, 6, 0) }, 2, 1);
        details.Controls.Add(_endPicker, 3, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        actions.Controls.Add(new Label { Text = "运营延长到 (UTC)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        actions.Controls.Add(_extendPicker);
        actions.Controls.Add(_extendShopButton);
        actions.Controls.Add(_extendAllButton);
        actions.Controls.Add(_extendItemButton);
        details.Controls.Add(actions, 0, 2);
        details.SetColumnSpan(actions, 4);

        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        outer.Panel1.Controls.Add(grids);
        outer.Panel2.Controls.Add(details);
        SplitContainerLayout.ConfigureWhenSized(outer, Orientation.Horizontal, 260, 150, 560);

        Controls.Add(outer);
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
        _shopGrid.SelectionChanged += async (_, _) => await LoadShopSelectionAsync();
        _itemGrid.SelectionChanged += (_, _) =>
            _selectedItem = _itemGrid.SelectedRows.Count > 0 ? _itemGrid.SelectedRows[0].Tag as MasterNestedRow : null;
        _saveShopButton.Click += async (_, _) => await SaveShopAsync();
        _extendShopButton.Click += async (_, _) => await ExtendShopAsync(includeItems: false);
        _extendAllButton.Click += async (_, _) => await ExtendShopAsync(includeItems: true);
        _extendItemButton.Click += async (_, _) => await ExtendItemAsync();
    }

    private async Task LoadShopSelectionAsync()
    {
        _selectedShop = _shopGrid.SelectedRows.Count > 0 ? _shopGrid.SelectedRows[0].Tag as MasterBusinessRow : null;
        _selectedItem = null;
        _itemGrid.Rows.Clear();
        var row = _selectedShop;
        var path = DatabasePath;
        if (row is null || path is null) return;

        _nameBox.Text = row.DisplayName;
        TimedMasterOperationsPage.SetPicker(_startPicker, row.Start);
        TimedMasterOperationsPage.SetPicker(_endPicker, row.End ?? row.ForceEnd);
        var suggested = row.ForceEnd ?? row.End ?? DateTimeOffset.UtcNow;
        if (suggested < DateTimeOffset.UtcNow) suggested = DateTimeOffset.UtcNow;
        _extendPicker.Checked = true;
        _extendPicker.Value = TimedMasterOperationsPage.ClampPickerDate(suggested.AddMonths(1).UtcDateTime);

        var items = await Task.Run(() => _adapter.ListItems(path, row.Key));
        foreach (var item in items)
        {
            var index = _itemGrid.Rows.Add(
                item.Key,
                item.DisplayName,
                TimedMasterOperationsPage.FormatDate(item.Start),
                TimedMasterOperationsPage.FormatDate(item.End));
            _itemGrid.Rows[index].Tag = item;
        }
        if (_itemGrid.Rows.Count > 0)
        {
            _itemGrid.Rows[0].Selected = true;
            _itemGrid.CurrentCell = _itemGrid.Rows[0].Cells[0];
        }
    }

    private async Task SaveShopAsync()
    {
        var path = DatabasePath;
        var row = _selectedShop;
        if (path is null || row is null || ApplyPlanAsync is null) return;
        var patch = _service.CreateCommonFieldPatch(
            row,
            _nameBox.Text,
            TimedMasterOperationsPage.PickerValue(_startPicker),
            TimedMasterOperationsPage.PickerValue(_endPicker),
            null);
        var plan = _service.CreateRecordPatchPlan("ExchangeShopMaster", row.Key, patch, $"编辑交换商店 {row.DisplayName}");
        await ApplyPlanAsync(plan);
    }

    private async Task ExtendShopAsync(bool includeItems)
    {
        var path = DatabasePath;
        var row = _selectedShop;
        var target = TimedMasterOperationsPage.PickerValue(_extendPicker);
        if (path is null || row is null || target is null || ApplyPlanAsync is null) return;
        var plan = await Task.Run(() => _adapter.ExtendShop(path, row.Key, target.Value, includeItems));
        await ApplyPlanAsync(plan);
    }

    private async Task ExtendItemAsync()
    {
        var path = DatabasePath;
        var shop = _selectedShop;
        var item = _selectedItem;
        var target = TimedMasterOperationsPage.PickerValue(_extendPicker);
        if (path is null || shop is null || item is null || target is null || ApplyPlanAsync is null) return;
        var plan = await Task.Run(() => _adapter.ExtendItem(path, shop.Key, item.Key, target.Value));
        await ApplyPlanAsync(plan);
    }

    private static DataGridView CreateShopGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add("Key", "商店主键");
        grid.Columns.Add("Name", "商店名称");
        grid.Columns.Add("Start", "开始 (UTC)");
        grid.Columns.Add("End", "结束 (UTC)");
        grid.Columns.Add("Status", "状态");
        return grid;
    }

    private static DataGridView CreateItemGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add("Key", "商品 ID");
        grid.Columns.Add("Name", "商品");
        grid.Columns.Add("Start", "开始 (UTC)");
        grid.Columns.Add("End", "结束 (UTC)");
        return grid;
    }

    private static DataGridView BaseGrid() => new()
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
}
