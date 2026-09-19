using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;
using Sirius.MasterData;
using Sirius.MasterTool.MasterMemory;
using Sirius.Toolbox.IO;

namespace Sirius.ToolboxUI;

public sealed class MasterToolForm : Form
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions EditorJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ToolStripButton _openButton = new("打开");
    private readonly ToolStripButton _saveButton = new("保存");
    private readonly ToolStripButton _saveAsButton = new("另存为");
    private readonly ToolStripButton _verifyButton = new("校验");
    private readonly ToolStripButton _refreshButton = new("刷新");
    private readonly ToolStripButton _newButton = new("新建");
    private readonly ToolStripButton _duplicateButton = new("复制");
    private readonly ToolStripButton _deleteButton = new("删除");

    private readonly TextBox _tableFilterBox = new() { PlaceholderText = "筛选表...", Dock = DockStyle.Top };
    private readonly ListView _tableList = new()
    {
        Dock = DockStyle.Fill,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        View = View.Details
    };

    private readonly DataGridView _recordsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
    };

    private readonly DataGridView _schemaGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    private readonly TextBox _jsonEditor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 10F)
    };

    private readonly Label _editorModeLabel = new() { AutoSize = true, Text = "未选择记录" };
    private readonly Button _applyRecordButton = new() { Text = "保存记录", AutoSize = true };
    private readonly Button _resetEditorButton = new() { Text = "重置", AutoSize = true };
    private readonly TextBox _keyLookupBox = new() { Width = 260, PlaceholderText = "主键（例如 1001 或 Id=1001;Type=2）" };
    private readonly Button _keyLookupButton = new() { Text = "查询", AutoSize = true };

    private readonly Button _previousPageButton = new() { Text = "上一页", AutoSize = true };
    private readonly Button _nextPageButton = new() { Text = "下一页", AutoSize = true };
    private readonly NumericUpDown _pageSizeBox = new() { Minimum = 10, Maximum = 1000, Value = 100, Width = 70 };
    private readonly Label _pageLabel = new() { AutoSize = true, Text = "0 / 0" };

    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _fileLabel = new() { Text = "未打开数据库" };
    private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 100 };

    private readonly List<MasterTableSummary> _allTables = [];
    private IReadOnlyList<MasterSchemaProperty> _currentSchema = [];
    private IReadOnlyList<MasterRecordView> _currentPage = [];

    private string? _sourcePath;
    private string? _workingPath;
    private string? _workingDirectory;
    private string? _currentTable;
    private string? _editingKey;
    private string? _startupPath;
    private string _editorOriginalText = string.Empty;
    private int _offset;
    private int _totalRows;
    private bool _dirty;
    private bool _editorIsNew;
    private bool _busy;
    private bool _suppressTableSelection;

    public MasterToolForm(string? startupPath)
    {
        _startupPath = startupPath;
        Text = "主数据编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 700);
        Size = new Size(1500, 920);
        AllowDrop = true;

        BuildUi();
        WireEvents();
        UpdateUiState();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (string.IsNullOrWhiteSpace(_startupPath)) return;

        var path = _startupPath!;
        _startupPath = null;
        if (File.Exists(path))
            await OpenDatabaseAsync(path);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(
                this,
                "数据库有未保存的更改，是否关闭并放弃这些更改？",
                "未保存的更改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        CleanupWorkingCopy();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        var fileMenu = new ToolStripMenuItem("文件");
        fileMenu.DropDownItems.Add("打开...", null, async (_, _) => await PromptOpenAsync());
        fileMenu.DropDownItems.Add("保存", null, async (_, _) => await SaveAsync());
        fileMenu.DropDownItems.Add("另存为...", null, async (_, _) => await SaveAsAsync());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("退出", null, (_, _) => Close());

        var databaseMenu = new ToolStripMenuItem("数据库");
        databaseMenu.DropDownItems.Add("校验", null, async (_, _) => await VerifyAsync());
        databaseMenu.DropDownItems.Add("刷新", null, async (_, _) => await RefreshAllAsync());
        menu.Items.Add(fileMenu);
        menu.Items.Add(databaseMenu);
        MainMenuStrip = menu;

        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden
        };
        toolStrip.Items.AddRange([
            _openButton,
            _saveButton,
            _saveAsButton,
            new ToolStripSeparator(),
            _verifyButton,
            _refreshButton,
            new ToolStripSeparator(),
            _newButton,
            _duplicateButton,
            _deleteButton
        ]);

        _tableList.Columns.Add("表", 215);
        _tableList.Columns.Add("行数", 70, HorizontalAlignment.Right);

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            ColumnCount = 1,
            RowCount = 3
        };
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var filterLabel = new Label { Text = "主数据表", AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
        _tableFilterBox.Margin = new Padding(0, 0, 0, 6);
        leftPanel.Controls.Add(filterLabel, 0, 0);
        leftPanel.Controls.Add(_tableFilterBox, 0, 1);
        leftPanel.Controls.Add(_tableList, 0, 2);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildRecordsTab());
        tabs.TabPages.Add(BuildSchemaTab());

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 310,
            FixedPanel = FixedPanel.Panel1
        };
        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(tabs);

        var status = new StatusStrip { Dock = DockStyle.Bottom };
        status.Items.Add(_statusLabel);
        status.Items.Add(_progress);
        status.Items.Add(_fileLabel);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(toolStrip);
        Controls.Add(menu);
    }

    private TabPage BuildRecordsTab()
    {
        var tab = new TabPage("记录");

        var pagingPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5, 6, 5, 0)
        };
        pagingPanel.Controls.Add(_previousPageButton);
        pagingPanel.Controls.Add(_nextPageButton);
        pagingPanel.Controls.Add(new Label { Text = "每页数量：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        pagingPanel.Controls.Add(_pageSizeBox);
        pagingPanel.Controls.Add(_pageLabel);
        pagingPanel.Controls.Add(new Label { Text = "主键：", AutoSize = true, Padding = new Padding(18, 5, 0, 0) });
        pagingPanel.Controls.Add(_keyLookupBox);
        pagingPanel.Controls.Add(_keyLookupButton);

        var editorButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5, 5, 5, 0)
        };
        editorButtons.Controls.Add(_editorModeLabel);
        editorButtons.Controls.Add(_applyRecordButton);
        editorButtons.Controls.Add(_resetEditorButton);

        var editorPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        editorPanel.Controls.Add(_jsonEditor);
        editorPanel.Controls.Add(editorButtons);

        var recordSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        recordSplit.Resize += (_, _) => ConfigureRecordSplitter(recordSplit);
        recordSplit.Panel1.Controls.Add(_recordsGrid);
        recordSplit.Panel1.Controls.Add(pagingPanel);
        recordSplit.Panel2.Controls.Add(editorPanel);

        tab.Controls.Add(recordSplit);
        return tab;
    }

    private static void ConfigureRecordSplitter(SplitContainer split)
    {
        const int panel1MinSize = 220;
        const int panel2MinSize = 180;
        if (split.Height < panel1MinSize + panel2MinSize)
        {
            return;
        }

        split.Panel1MinSize = panel1MinSize;
        split.Panel2MinSize = panel2MinSize;

        var minimum = split.Panel1MinSize;
        var maximum = split.Height - split.Panel2MinSize;
        if (maximum >= minimum)
        {
            split.SplitterDistance = Math.Clamp(500, minimum, maximum);
        }
    }

    private TabPage BuildSchemaTab()
    {
        var tab = new TabPage("结构");
        _schemaGrid.Columns.Add("Key", "键");
        _schemaGrid.Columns.Add("Name", "属性");
        _schemaGrid.Columns.Add("Type", "类型");
        _schemaGrid.Columns.Add("Writable", "可写");
        _schemaGrid.Columns.Add("PrimaryKey", "主键");
        tab.Controls.Add(_schemaGrid);
        return tab;
    }

    private void WireEvents()
    {
        _openButton.Click += async (_, _) => await PromptOpenAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _saveAsButton.Click += async (_, _) => await SaveAsAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _refreshButton.Click += async (_, _) => await RefreshAllAsync();
        _newButton.Click += (_, _) => BeginNewRecord(copySelected: false);
        _duplicateButton.Click += (_, _) => BeginNewRecord(copySelected: true);
        _deleteButton.Click += async (_, _) => await DeleteSelectedRecordAsync();

        _tableFilterBox.TextChanged += (_, _) => ApplyTableFilter();
        _tableList.SelectedIndexChanged += async (_, _) =>
        {
            if (!_suppressTableSelection)
                await LoadSelectedTableAsync(resetPage: true);
        };

        _previousPageButton.Click += async (_, _) =>
        {
            _offset = Math.Max(0, _offset - (int)_pageSizeBox.Value);
            await LoadPageAsync();
        };
        _nextPageButton.Click += async (_, _) =>
        {
            var pageSize = (int)_pageSizeBox.Value;
            if (_offset + pageSize < _totalRows) _offset += pageSize;
            await LoadPageAsync();
        };
        _pageSizeBox.ValueChanged += async (_, _) =>
        {
            _offset = 0;
            await LoadPageAsync();
        };

        _recordsGrid.SelectionChanged += (_, _) => LoadSelectedRecordIntoEditor();
        _recordsGrid.CellDoubleClick += (_, _) => _jsonEditor.Focus();
        _applyRecordButton.Click += async (_, _) => await ApplyEditorAsync();
        _resetEditorButton.Click += (_, _) => _jsonEditor.Text = _editorOriginalText;
        _keyLookupButton.Click += async (_, _) => await LookupRecordAsync();
        _keyLookupBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LookupRecordAsync();
        };

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += async (_, e) =>
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (files is { Length: > 0 }) await OpenDatabaseAsync(files[0]);
        };
    }

    private async Task PromptOpenAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            Title = "打开 MasterMemory 数据库"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await OpenDatabaseAsync(dialog.FileName);
    }

    private async Task OpenDatabaseAsync(string path)
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(
                this,
                "当前数据库有未保存的更改，是否放弃更改并打开其他文件？",
                "未保存的更改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        await RunBusyAsync("正在打开数据库...", async () =>
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.Combine(Path.GetTempPath(), "Sirius.ToolboxUI", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var workingPath = Path.Combine(directory, "mastermemory.db");
            MasterDatabaseVerification verification;
            try
            {
                File.Copy(fullPath, workingPath, overwrite: true);
                verification = await Task.Run(() => MasterMemoryDatabaseService.Verify(workingPath));
            }
            catch
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
                throw;
            }

            CleanupWorkingCopy();
            _sourcePath = fullPath;
            _workingDirectory = directory;
            _workingPath = workingPath;
            _dirty = false;
            _currentTable = null;
            _offset = 0;

            await RefreshTableListAsync(selectTable: null);
            var firstTable = _allTables.FirstOrDefault();
            if (firstTable is not null)
            {
                ApplyTableFilter(firstTable.Name);
                await LoadTableAsync(firstTable.Name, resetPage: true);
            }
            SetStatus($"已打开 {Path.GetFileName(fullPath)} - {verification.TableCount} 个表，{verification.RowCount:N0} 行");
            UpdateUiState();
        });
    }

    private async Task RefreshAllAsync()
    {
        if (_workingPath is null) return;
        var current = _currentTable;
        await RunBusyAsync("正在刷新...", async () =>
        {
            await RefreshTableListAsync(current);
            if (current is not null)
                await LoadTableAsync(current, resetPage: false);
        });
    }

    private async Task RefreshTableListAsync(string? selectTable)
    {
        var workingPath = _workingPath;
        if (workingPath is null) return;
        var tables = await Task.Run(() => MasterMemoryDatabaseService.GetTables(workingPath));
        _allTables.Clear();
        _allTables.AddRange(tables);
        ApplyTableFilter(selectTable);
    }

    private void ApplyTableFilter(string? preferredTable = null)
    {
        var filter = _tableFilterBox.Text.Trim();
        var previouslySelected = preferredTable ?? _currentTable;

        _suppressTableSelection = true;
        try
        {
            _tableList.BeginUpdate();
            _tableList.Items.Clear();
            foreach (var table in _allTables)
            {
                if (filter.Length != 0 && !table.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                var item = new ListViewItem(table.Name) { Tag = table };
                item.SubItems.Add(table.RowCount.ToString("N0"));
                _tableList.Items.Add(item);
                if (string.Equals(table.Name, previouslySelected, StringComparison.Ordinal))
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }
            _tableList.EndUpdate();
        }
        finally
        {
            _suppressTableSelection = false;
        }
    }

    private async Task LoadSelectedTableAsync(bool resetPage)
    {
        if (_tableList.SelectedItems.Count == 0) return;
        var summary = _tableList.SelectedItems[0].Tag as MasterTableSummary;
        if (summary is null) return;
        await RunBusyAsync($"正在加载 {summary.Name}...", () => LoadTableAsync(summary.Name, resetPage));
    }

    private async Task LoadTableAsync(string tableName, bool resetPage)
    {
        var workingPath = _workingPath;
        if (workingPath is null) return;
        _currentTable = tableName;
        if (resetPage) _offset = 0;

        var summary = _allTables.FirstOrDefault(x => string.Equals(x.Name, tableName, StringComparison.Ordinal));
        _totalRows = summary?.RowCount ?? 0;
        _currentSchema = await Task.Run(() => MasterMemoryDatabaseService.GetSchema(workingPath, tableName));
        PopulateSchemaGrid();
        BuildRecordColumns();
        await LoadPageAsync();
        UpdateUiState();
    }

    private async Task LoadPageAsync()
    {
        var workingPath = _workingPath;
        var currentTable = _currentTable;
        if (workingPath is null || currentTable is null) return;
        var pageSize = (int)_pageSizeBox.Value;
        var lastStart = _totalRows == 0 ? 0 : ((_totalRows - 1) / pageSize) * pageSize;
        _offset = Math.Clamp(_offset, 0, lastStart);

        _currentPage = await Task.Run(() =>
            MasterMemoryDatabaseService.ListRecords(workingPath, currentTable, _offset, pageSize));
        PopulateRecordGrid();
        UpdatePagingState();
    }

    private void BuildRecordColumns()
    {
        _recordsGrid.Columns.Clear();
        _recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "__row", HeaderText = "行号", Width = 70, Frozen = true });
        _recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "__key", HeaderText = "主键", Width = 190, Frozen = true });
        foreach (var property in _currentSchema)
        {
            var width = property.Type.Contains("String", StringComparison.Ordinal) ? 220 : 140;
            _recordsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "p_" + property.Name,
                HeaderText = property.PrimaryKey ? property.Name + " [PK]" : property.Name,
                Width = width,
                Tag = property
            });
        }
    }

    private void PopulateRecordGrid()
    {
        _recordsGrid.SuspendLayout();
        try
        {
            _recordsGrid.Rows.Clear();
            foreach (var record in _currentPage)
            {
                var cells = new object[_currentSchema.Count + 2];
                cells[0] = record.Row;
                cells[1] = record.PrimaryKey;
                var map = record.Record as IReadOnlyDictionary<string, object?>;
                for (var i = 0; i < _currentSchema.Count; i++)
                {
                    var property = _currentSchema[i];
                    var value = map is not null && map.TryGetValue(property.Name, out var found) ? found : null;
                    cells[i + 2] = FormatCellValue(value);
                }
                var rowIndex = _recordsGrid.Rows.Add(cells);
                _recordsGrid.Rows[rowIndex].Tag = record;
            }
        }
        finally
        {
            _recordsGrid.ResumeLayout();
        }

        if (_recordsGrid.Rows.Count > 0)
        {
            _recordsGrid.Rows[0].Selected = true;
            _recordsGrid.CurrentCell = _recordsGrid.Rows[0].Cells[0];
        }
        else
        {
            ClearEditor();
        }
    }

    private void PopulateSchemaGrid()
    {
        _schemaGrid.Rows.Clear();
        foreach (var property in _currentSchema)
        {
            _schemaGrid.Rows.Add(
                property.Key,
                property.Name,
                property.Type,
            property.Writable ? "是" : "否",
            property.PrimaryKey ? "是" : "否");
        }
    }

    private static string FormatCellValue(object? value)
    {
        if (value is null) return "null";
        if (value is string text) return text;
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is IFormattable scalar)
            return scalar.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return JsonSerializer.Serialize(value, CompactJson);
    }

    private void LoadSelectedRecordIntoEditor()
    {
        if (_recordsGrid.SelectedRows.Count == 0)
        {
            ClearEditor();
            return;
        }

        if (_recordsGrid.SelectedRows[0].Tag is not MasterRecordView record)
        {
            ClearEditor();
            return;
        }

        _editorIsNew = false;
        _editingKey = record.PrimaryKey;
        _jsonEditor.Text = JsonSerializer.Serialize(record.Record, EditorJson);
        _editorOriginalText = _jsonEditor.Text;
        _editorModeLabel.Text = $"编辑：{record.PrimaryKey}";
        UpdateUiState();
    }

    private void BeginNewRecord(bool copySelected)
    {
        if (_workingPath is null || _currentTable is null) return;

        var template = copySelected && _editingKey is not null && !_editorIsNew
            ? KeepWritableProperties(_editorOriginalText)
            : "{\r\n  \r\n}";
        _editorIsNew = true;
        _editingKey = null;
        _jsonEditor.Text = template;
        _editorOriginalText = template;
        _editorModeLabel.Text = copySelected ? "新建记录（已从当前记录复制）" : "新建记录";
        _jsonEditor.Focus();
        UpdateUiState();
    }

    private void ClearEditor()
    {
        _editorIsNew = false;
        _editingKey = null;
        _jsonEditor.Clear();
        _editorOriginalText = string.Empty;
        _editorModeLabel.Text = "未选择记录";
        UpdateUiState();
    }

    private async Task ApplyEditorAsync()
    {
        var workingPath = _workingPath;
        var table = _currentTable;
        if (workingPath is null || table is null) return;
        var json = _jsonEditor.Text;
        if (string.IsNullOrWhiteSpace(json)) return;

        var wasNew = _editorIsNew;
        await RunBusyAsync(wasNew ? "正在添加记录..." : "正在更新记录...", async () =>
        {
            if (wasNew)
            {
                await Task.Run(() => MasterMemoryDatabaseService.AddRecord(workingPath, table, json, workingPath));
            }
            else
            {
                var key = _editingKey ?? throw new InvalidOperationException("未选择记录主键。");
                var (patchJson, changeCount) = CreateUpdatePatch(_editorOriginalText, json);
                if (changeCount == 0)
                {
                    SetStatus("没有需要保存的记录更改。");
                    return;
                }
                await Task.Run(() => MasterMemoryDatabaseService.UpdateRecord(workingPath, table, key, patchJson, workingPath));
            }

            _dirty = true;
            await RefreshTableListAsync(table);
            await LoadTableAsync(table, resetPage: false);
            SetStatus(wasNew ? "记录已添加。" : "记录已更新。");
            UpdateUiState();
        });
    }

    private string KeepWritableProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("记录 JSON 必须是对象。");

        var writable = _currentSchema
            .Where(property => property.Writable)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (writable.Contains(property.Name))
                result[property.Name] = property.Value.Clone();
        }
        return JsonSerializer.Serialize(result, EditorJson);
    }

    private static (string Json, int ChangeCount) CreateUpdatePatch(string originalJson, string editedJson)
    {
        using var original = JsonDocument.Parse(originalJson);
        using var edited = JsonDocument.Parse(editedJson);
        if (original.RootElement.ValueKind != JsonValueKind.Object || edited.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("记录 JSON 必须是对象。");

        var originalProperties = original.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => CanonicalJson(property.Value), StringComparer.OrdinalIgnoreCase);
        var patch = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in edited.RootElement.EnumerateObject())
        {
            var canonical = CanonicalJson(property.Value);
            if (!originalProperties.TryGetValue(property.Name, out var before)
                || !string.Equals(before, canonical, StringComparison.Ordinal))
            {
                patch[property.Name] = property.Value.Clone();
            }
        }

        return (JsonSerializer.Serialize(patch, PrettyJson), patch.Count);
    }

    private static string CanonicalJson(JsonElement element) =>
        JsonSerializer.Serialize(element, CompactJson);

    private async Task DeleteSelectedRecordAsync()
    {
        var workingPath = _workingPath;
        var table = _currentTable;
        var key = _editingKey;
        if (workingPath is null || table is null || key is null || _editorIsNew) return;

        var answer = MessageBox.Show(
            this,
            $"确定要从 {table} 中删除记录 {key} 吗？",
            "删除记录",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync("正在删除记录...", async () =>
        {
            await Task.Run(() => MasterMemoryDatabaseService.DeleteRecord(workingPath, table, key, workingPath));
            _dirty = true;
            await RefreshTableListAsync(table);
            await LoadTableAsync(table, resetPage: false);
            SetStatus("记录已删除。");
            UpdateUiState();
        });
    }

    private async Task LookupRecordAsync()
    {
        var workingPath = _workingPath;
        var table = _currentTable;
        if (workingPath is null || table is null) return;
        var key = _keyLookupBox.Text.Trim();
        if (key.Length == 0) return;

        await RunBusyAsync("正在查询记录...", async () =>
        {
            var record = await Task.Run(() => MasterMemoryDatabaseService.GetRecord(workingPath, table, key));
            _editorIsNew = false;
            _editingKey = record.PrimaryKey;
            _jsonEditor.Text = JsonSerializer.Serialize(record.Record, EditorJson);
            _editorOriginalText = _jsonEditor.Text;
            _editorModeLabel.Text = $"编辑：{record.PrimaryKey}（查询结果）";
            UpdateUiState();
        });
    }

    private async Task VerifyAsync()
    {
        var workingPath = _workingPath;
        if (workingPath is null) return;
        await RunBusyAsync("正在校验数据库...", async () =>
        {
            var result = await Task.Run(() => MasterMemoryDatabaseService.Verify(workingPath));
            SetStatus($"校验通过 - {result.TableCount} 个表，{result.RowCount:N0} 行，SHA-256 {result.Sha256[..12]}...");
            MessageBox.Show(
                this,
                $"MasterMemory 数据库有效。\r\n\r\n表数量：{result.TableCount}\r\n行数：{result.RowCount:N0}\r\nSHA-256：{result.Sha256}",
                "校验成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private async Task SaveAsync()
    {
        var sourcePath = _sourcePath;
        if (_workingPath is null || sourcePath is null) return;
        await SaveToAsync(sourcePath, updateSourcePath: false);
    }

    private async Task SaveAsAsync()
    {
        if (_workingPath is null) return;
        var sourcePath = _sourcePath;
        using var dialog = new SaveFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            FileName = sourcePath is null ? "mastermemory.db" : Path.GetFileName(sourcePath) ?? "mastermemory.db",
            Title = "另存为 MasterMemory 数据库"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await SaveToAsync(dialog.FileName, updateSourcePath: true);
    }

    private async Task SaveToAsync(string targetPath, bool updateSourcePath)
    {
        var workingPath = _workingPath;
        if (workingPath is null) return;
        await RunBusyAsync("正在保存数据库...", async () =>
        {
            var fullTarget = Path.GetFullPath(targetPath);
            var bytes = await File.ReadAllBytesAsync(workingPath);
            await Task.Run(() => MasterMemoryDatabaseService.Verify(workingPath));

            if (File.Exists(fullTarget))
                File.Copy(fullTarget, fullTarget + ".bak", overwrite: true);
            AtomicFile.WriteAllBytes(fullTarget, bytes);

            if (updateSourcePath) _sourcePath = fullTarget;
            _dirty = false;
            SetStatus($"已保存 {fullTarget}");
            UpdateUiState();
        });
    }

    private void UpdatePagingState()
    {
        var pageSize = (int)_pageSizeBox.Value;
        var page = _totalRows == 0 ? 0 : (_offset / pageSize) + 1;
        var pages = _totalRows == 0 ? 0 : ((_totalRows - 1) / pageSize) + 1;
        _pageLabel.Text = $"第 {page:N0} / {pages:N0} 页   共 {_totalRows:N0} 行";
        _previousPageButton.Enabled = !_busy && _offset > 0;
        _nextPageButton.Enabled = !_busy && _offset + pageSize < _totalRows;
    }

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(message);
        UpdateUiState();
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
        SetStatus("操作失败。");
        MessageBox.Show(this, ex.Message, "Sirius 主数据工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateUiState();
        }
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void UpdateUiState()
    {
        var hasDatabase = _workingPath is not null;
        var hasTable = hasDatabase && _currentTable is not null;
        var hasRecord = hasTable && _editingKey is not null && !_editorIsNew;

        _openButton.Enabled = !_busy;
        _saveButton.Enabled = !_busy && hasDatabase && _dirty;
        _saveAsButton.Enabled = !_busy && hasDatabase;
        _verifyButton.Enabled = !_busy && hasDatabase;
        _refreshButton.Enabled = !_busy && hasDatabase;
        _newButton.Enabled = !_busy && hasTable;
        _duplicateButton.Enabled = !_busy && hasRecord;
        _deleteButton.Enabled = !_busy && hasRecord;
        _tableList.Enabled = !_busy && hasDatabase;
        _tableFilterBox.Enabled = !_busy && hasDatabase;
        _recordsGrid.Enabled = !_busy && hasTable;
        _jsonEditor.Enabled = !_busy && hasTable;
        _applyRecordButton.Enabled = !_busy && hasTable && (_editorIsNew || hasRecord);
        _resetEditorButton.Enabled = !_busy && hasRecord;
        _keyLookupBox.Enabled = !_busy && hasTable;
        _keyLookupButton.Enabled = !_busy && hasTable;
        _pageSizeBox.Enabled = !_busy && hasTable;
        _progress.Visible = _busy;
        var sourcePath = _sourcePath;
        _fileLabel.Text = sourcePath is null
            ? "未打开数据库"
            : (_dirty ? "* " : string.Empty) + Path.GetFileName(sourcePath);
        Text = sourcePath is null
            ? "Sirius 主数据工具"
            : $"Sirius 主数据工具 - {(_dirty ? "*" : string.Empty)}{Path.GetFileName(sourcePath)}";
        UpdatePagingState();
    }

    private void CleanupWorkingCopy()
    {
        _workingPath = null;
        if (_workingDirectory is null) return;
        try
        {
            if (Directory.Exists(_workingDirectory)) Directory.Delete(_workingDirectory, recursive: true);
        }
        catch
        {
            // A stale temp directory is harmless and can be removed by the OS/temp cleanup later.
        }
        finally
        {
            _workingDirectory = null;
        }
    }
}
