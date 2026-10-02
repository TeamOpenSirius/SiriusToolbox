using System.Text.Json;
using System.Windows.Forms;
using Sirius.Toolbox.Master.Creation;

namespace Sirius.ToolboxUI;

internal sealed class MasterCreationPage : UserControl
{
    private readonly Func<string?> _getDatabasePath;
    private readonly Func<MasterCreationDraft, Task> _applyDraft;
    private readonly ComboBox _kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly NumericUpDown _idBox = new() { Minimum = 1, Maximum = long.MaxValue, Value = 900000 };
    private readonly NumericUpDown _baseIdBox = new() { Minimum = 1, Maximum = long.MaxValue, Value = 900001 };
    private readonly DataGridView _fieldsGrid = CreateFieldGrid();
    private readonly DataGridView _baseFieldsGrid = CreateFieldGrid();
    private readonly CheckBox _skipResourceBox = new() { AutoSize = true, Text = "跳过资源路径提示" };
    private readonly Button _previewButton = new() { AutoSize = true, Text = "预览" };
    private readonly Button _createButton = new() { AutoSize = true, Text = "创建并写入" };
    private readonly RichTextBox _resultBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private MasterCreationDraft? _preview;

    public MasterCreationPage(Func<string?> getDatabasePath, Func<MasterCreationDraft, Task> applyDraft)
    {
        _getDatabasePath = getDatabasePath;
        _applyDraft = applyDraft;
        _kindBox.Items.AddRange(["音乐", "活动", "卡面"]);
        _kindBox.SelectedIndex = 0;
        BuildUi();
        _kindBox.SelectedIndexChanged += (_, _) => UpdateMode();
        _previewButton.Click += (_, _) => Preview();
        _createButton.Click += async (_, _) => await CreateAsync();
        UpdateMode();
    }

    private void BuildUi()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = false };
        top.Controls.Add(new Label { Text = "创建类型", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        top.Controls.Add(_kindBox);
        top.Controls.Add(new Label { Text = "主 ID", AutoSize = true, Padding = new Padding(12, 6, 6, 0) });
        top.Controls.Add(_idBox);
        top.Controls.Add(new Label { Text = "基础 ID（卡面）", AutoSize = true, Padding = new Padding(12, 6, 6, 0) });
        top.Controls.Add(_baseIdBox);
        top.Controls.Add(_skipResourceBox);
        top.Controls.Add(_previewButton);
        top.Controls.Add(_createButton);

        var editors = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8)
        };
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        editors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editors.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        editors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editors.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        editors.Controls.Add(new Label { Text = "主记录字段（可视化）", AutoSize = true }, 0, 0);
        editors.Controls.Add(new Label { Text = "预览与校验结果", AutoSize = true }, 1, 0);
        editors.Controls.Add(_fieldsGrid, 0, 1);
        editors.Controls.Add(_resultBox, 1, 1);
        editors.Controls.Add(new Label { Text = "卡面基础角色字段（仅卡面）", AutoSize = true }, 0, 2);
        editors.Controls.Add(_baseFieldsGrid, 0, 3);
        editors.SetColumnSpan(_baseFieldsGrid, 2);
        Controls.Add(editors);
        Controls.Add(top);
        PopulateFields();
    }

    private void UpdateMode()
    {
        var card = string.Equals(_kindBox.SelectedItem?.ToString(), "卡面", StringComparison.Ordinal);
        _baseIdBox.Visible = card;
        _baseFieldsGrid.Visible = card;
        PopulateFields();
    }

    private void Preview()
    {
        try
        {
            var draft = BuildDraft();
            _preview = draft;
            _resultBox.Text = string.Join(Environment.NewLine, draft.Records.Select(x =>
                $"{x.TableName}: {JsonSerializer.Serialize(x.Fields)}"));
            _createButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _preview = null;
            _resultBox.Text = ex.Message;
        }
    }

    private async Task CreateAsync()
    {
        if (_getDatabasePath() is null)
        {
            _resultBox.Text = "请先在文件菜单中打开 MasterMemory 数据库。";
            return;
        }
        Preview();
        if (_preview is null) return;
        _previewButton.Enabled = false;
        _createButton.Enabled = false;
        try { await _applyDraft(_preview); }
        finally { _previewButton.Enabled = true; _createButton.Enabled = true; }
    }

    private MasterCreationDraft BuildDraft()
    {
        var kind = _kindBox.SelectedItem?.ToString() ?? throw new InvalidOperationException("请选择创建类型。");
        var fields = ReadFields(_fieldsGrid);
        return kind switch
        {
            "音乐" => MasterCreationDefinitions.Music((long)_idBox.Value, fields),
            "活动" => MasterCreationDefinitions.Event((long)_idBox.Value, fields),
            "卡面" => MasterCreationDefinitions.Card((long)_idBox.Value, (long)_baseIdBox.Value,
                fields, ReadFields(_baseFieldsGrid)),
            _ => throw new InvalidOperationException("未知创建类型。")
        } with { SkipResourceChecks = _skipResourceBox.Checked };
    }

    private void PopulateFields()
    {
        var fields = _kindBox.SelectedItem?.ToString() switch
        {
            "活动" => new[] { "Name", "EventType", "StartDate", "EndDate", "ForceEndDate", "ExchangeShopMasterId" },
            "卡面" => new[] { "Name", "Description", "AssetId", "Rarity", "Attribute", "CharacterBaseMasterId", "DisplayStartAt", "DisplayEndAt", "UnlockText" },
            _ => new[] { "Name", "Description", "PronounceName", "LyricWriter", "Composer", "Arranger", "UnlockText", "IsLongVersion", "ReleasedAt", "StaminaConsumption", "MusicTimeSecond", "UnlockConditionType", "UnlockConditionValue", "MusicVideoType", "MusicCoverType" }
        };
        FillGrid(_fieldsGrid, fields);
        FillGrid(_baseFieldsGrid, new[] { "Name", "Description", "School", "Grade", "BirthMonth", "BirthDay", "Height", "Hobby", "CompanyMasterId", "ProfileImageAssetId", "Age", "DefaultCostumeMasterId", "CharacterBaseType" });
    }

    private static void FillGrid(DataGridView grid, IEnumerable<string> fields)
    {
        EnsureColumns(grid);
        var old = grid.Rows.Cast<DataGridViewRow>().ToDictionary(x => Convert.ToString(x.Cells[0].Value) ?? "", x => Convert.ToString(x.Cells[1].Value) ?? "", StringComparer.OrdinalIgnoreCase);
        grid.Rows.Clear();
        foreach (var field in fields) grid.Rows.Add(field, old.GetValueOrDefault(field) ?? string.Empty);
    }

    private static IReadOnlyDictionary<string, object?> ReadFields(DataGridView grid)
    {
        return grid.Rows.Cast<DataGridViewRow>()
            .Where(x => !x.IsNewRow && !string.IsNullOrWhiteSpace(Convert.ToString(x.Cells[0].Value)))
            .ToDictionary(x => Convert.ToString(x.Cells[0].Value)!, x => (object?)(Convert.ToString(x.Cells[1].Value) ?? ""), StringComparer.OrdinalIgnoreCase);
    }

    private static DataGridView CreateFieldGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        ColumnHeadersVisible = true
    };

    private static void EnsureColumns(DataGridView grid)
    {
        if (grid.Columns.Count != 0) return;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "字段", ReadOnly = true, Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "值", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }
}
