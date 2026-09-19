using System.Drawing;
using System.Windows.Forms;
using Sirius.AssetTool.Episodes;
using Sirius.Toolbox.Episodes.Protocol;

namespace Sirius.ToolboxUI;

public sealed class EpisodeEditorForm : Form
{
    private readonly TextBox _pathBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly ListBox _detailsList = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _idBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _episodeMasterIdBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _orderBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _groupOrderBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _speakerNameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _titleBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _effectBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _backgroundBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _backgroundCharacterBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _bgmBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _seBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _voiceBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _stillPhotoBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _movieBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _speakerIconBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _phraseBox = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true
    };
    private readonly Button _applyButton = new() { AutoSize = true, Text = "应用当前记录" };
    private readonly Button _resetButton = new() { AutoSize = true, Text = "撤销本次编辑" };
    private readonly Button _newButton = new() { AutoSize = true, Text = "新增" };
    private readonly Button _duplicateButton = new() { AutoSize = true, Text = "复制" };
    private readonly Button _deleteButton = new() { AutoSize = true, Text = "删除" };
    private readonly ToolStripButton _openButton = new("打开");
    private readonly ToolStripButton _saveButton = new("保存");
    private readonly ToolStripButton _saveAsButton = new("另存为");
    private readonly ToolStripButton _packButton = new("导出 BIN");
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private readonly EpisodeEditorService _service = new();
    private EpisodeEditorDocument? _document;
    private string? _sourcePath;
    private int _currentIndex = -1;
    private bool _suppressSelection;
    private bool _loadingEditor;
    private bool _pendingEditorChanges;
    private bool _busy;
    private bool _dirty;

    public EpisodeEditorForm()
    {
        Text = "剧情编辑器";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1460, 900);

        BuildUi();
        WireEvents();
        UpdateUiState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if ((_dirty || _pendingEditorChanges) && !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill };
        var fileMenu = new ToolStripMenuItem("文件");
        fileMenu.DropDownItems.Add("打开...", null, async (_, _) => await OpenDialogAsync());
        fileMenu.DropDownItems.Add("保存", null, async (_, _) => await SaveAsync(saveAs: false));
        fileMenu.DropDownItems.Add("另存为...", null, async (_, _) => await SaveAsync(saveAs: true));
        fileMenu.DropDownItems.Add("导出 BIN...", null, async (_, _) => await PackAsync());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("退出", null, (_, _) => Close());
        menu.Items.Add(fileMenu);

        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden
        };
        toolStrip.Items.AddRange([_openButton, _saveButton, _saveAsButton, _packButton]);

        var pathPanel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 6),
            RowCount = 1
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "当前文件：" }, 0, 0);
        pathPanel.Controls.Add(_pathBox, 1, 0);
        var pathButton = new Button { AutoSize = true, Text = "选择 JSON..." };
        pathButton.Click += async (_, _) => await OpenDialogAsync();
        pathPanel.Controls.Add(pathButton, 2, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Orientation = Orientation.Vertical
        };
        split.Resize += (_, _) => ConfigureEditorSplitter(split);
        split.Panel1.Controls.Add(BuildListPanel());
        split.Panel2.Controls.Add(BuildEditorPanel());

        var status = new StatusStrip { Dock = DockStyle.Fill };
        status.Items.Add(_statusLabel);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(toolStrip, 0, 1);
        root.Controls.Add(pathPanel, 0, 2);
        root.Controls.Add(split, 0, 3);
        root.Controls.Add(status, 0, 4);
        MainMenuStrip = menu;
        Controls.Add(root);
    }

    private static void ConfigureEditorSplitter(SplitContainer split)
    {
        const int leftMinimum = 260;
        const int editorMinimum = 560;
        if (split.Width < leftMinimum + editorMinimum)
            return;

        split.Panel1MinSize = leftMinimum;
        split.Panel2MinSize = editorMinimum;
        split.SplitterDistance = Math.Clamp(310, leftMinimum, split.Width - editorMinimum);
    }

    private Control BuildListPanel()
    {
        var label = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(6),
            Text = "剧情段落"
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4),
            WrapContents = false
        };
        buttons.Controls.Add(_newButton);
        buttons.Controls.Add(_duplicateButton);
        buttons.Controls.Add(_deleteButton);

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        panel.Controls.Add(_detailsList);
        panel.Controls.Add(buttons);
        panel.Controls.Add(label);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Padding = new Padding(8),
            RowCount = 8
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddFieldPair(fields, 0, "记录 ID：", _idBox, "剧情 ID：", _episodeMasterIdBox);
        AddFieldPair(fields, 1, "顺序：", _orderBox, "组顺序：", _groupOrderBox);
        AddFieldPair(fields, 2, "说话人：", _speakerNameBox, "标题：", _titleBox);
        AddFieldPair(fields, 3, "效果：", _effectBox, "背景图片：", _backgroundBox);
        AddFieldPair(fields, 4, "背景角色：", _backgroundCharacterBox, "BGM：", _bgmBox);
        AddFieldPair(fields, 5, "SE：", _seBox, "语音：", _voiceBox);
        AddFieldPair(fields, 6, "静态图：", _stillPhotoBox, "影片：", _movieBox);
        AddFieldPair(fields, 7, "说话人图标：", _speakerIconBox, "", new TextBox { Visible = false });

        var phrasePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 8) };
        phrasePanel.Controls.Add(_phraseBox);
        _phraseBox.BringToFront();
        var editorButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 0, 8, 5),
            WrapContents = false
        };
        editorButtons.Controls.Add(_applyButton);
        editorButtons.Controls.Add(_resetButton);
        phrasePanel.Controls.Add(editorButtons);

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(fields, 0, 0);
        content.Controls.Add(phrasePanel, 0, 1);
        return content;
    }

    private static void AddFieldPair(TableLayoutPanel table, int row, string leftLabel, TextBox leftBox, string rightLabel, TextBox rightBox)
    {
        table.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = leftLabel }, 0, row);
        table.Controls.Add(leftBox, 1, row);
        table.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = rightLabel }, 2, row);
        table.Controls.Add(rightBox, 3, row);
    }

    private void WireEvents()
    {
        _openButton.Click += async (_, _) => await OpenDialogAsync();
        _saveButton.Click += async (_, _) => await SaveAsync(saveAs: false);
        _saveAsButton.Click += async (_, _) => await SaveAsync(saveAs: true);
        _packButton.Click += async (_, _) => await PackAsync();
        _detailsList.SelectedIndexChanged += (_, _) => HandleSelectionChanged();
        _applyButton.Click += (_, _) => TryApplyCurrent();
        _resetButton.Click += (_, _) => LoadCurrentDetail();
        _newButton.Click += (_, _) => AddDetail(duplicate: false);
        _duplicateButton.Click += (_, _) => AddDetail(duplicate: true);
        _deleteButton.Click += (_, _) => DeleteDetail();
        foreach (var box in EditableBoxes())
            box.TextChanged += (_, _) =>
            {
                if (!_loadingEditor && _document is not null && _currentIndex >= 0)
                    _pendingEditorChanges = true;
            };
    }

    private async Task OpenDialogAsync()
    {
        if (_busy || ((_dirty || _pendingEditorChanges) && !ConfirmDiscard())) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "剧情 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "打开剧情 JSON"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await OpenPathAsync(dialog.FileName);
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            SetBusy(true);
            var document = await Task.Run(() => _service.Load(path));
            _document = document;
            _sourcePath = Path.GetFullPath(path);
            _dirty = false;
            _pendingEditorChanges = false;
            RefreshDetails(0);
            _statusLabel.Text = $"已打开：{_sourcePath}，共 {_document.Details.Count} 条记录";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "打开剧情错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = $"打开失败：{exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        if (_busy || _document is null) return;
        try
        {
            TryApplyCurrentOrThrow();
            var path = saveAs ? ChooseJsonPath() : _sourcePath;
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Path.GetFullPath(path);
            var overwrite = ConfirmOverwrite(path);
            if (overwrite is null) return;
            SetBusy(true);
            var result = await Task.Run(() => _service.Save(_document, path, overwrite.Value));
            _sourcePath = result.OutputPath;
            _pathBox.Text = _sourcePath;
            _dirty = false;
            _statusLabel.Text = $"已保存：{result.OutputPath}，共 {result.DetailCount} 条记录";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"保存失败：{exception.Message}";
            MessageBox.Show(this, exception.Message, "保存剧情错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task PackAsync()
    {
        if (_busy || _document is null) return;
        try
        {
            TryApplyCurrentOrThrow();
            var path = ChooseBinPath();
            if (path is null) return;
            var overwrite = ConfirmOverwrite(path);
            if (overwrite is null) return;
            SetBusy(true);
            var result = await Task.Run(() => _service.Pack(_document, path, overwrite.Value));
            _statusLabel.Text = $"BIN 已导出：{result.OutputPath}，{result.DetailCount} 条记录，{result.ByteCount} 字节";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"导出失败：{exception.Message}";
            MessageBox.Show(this, exception.Message, "导出剧情 BIN 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddDetail(bool duplicate)
    {
        if (_document is null) return;
        try
        {
            TryApplyCurrentOrThrow();
            var source = _currentIndex >= 0 && _currentIndex < _document.Details.Count
                ? _document.Details[_currentIndex]
                : null;
            var detail = duplicate && source is not null ? CloneDetail(source) : new EpisodeDetailResult
            {
                CharacterMotions = []
            };
            detail.Id = _document.Details.Count == 0 ? 1 : _document.Details.Max(static item => item.Id) + 1;
            detail.EpisodeMasterId = source?.EpisodeMasterId
                ?? _document.Details.FirstOrDefault()?.EpisodeMasterId
                ?? 0;
            detail.Order = _document.Details.Count == 0 ? 1 : _document.Details.Max(static item => item.Order) + 1;
            detail.GroupOrder = source?.GroupOrder ?? 1;
            if (duplicate) detail.Phrase = $"{detail.Phrase}（副本）";
            _document.Details.Add(detail);
            _dirty = true;
            RefreshDetails(_document.Details.Count - 1);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "新增剧情错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteDetail()
    {
        if (_document is null || _currentIndex < 0 || _currentIndex >= _document.Details.Count) return;
        try
        {
            TryApplyCurrentOrThrow();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "记录格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, "确定删除当前剧情记录吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _document.Details.RemoveAt(_currentIndex);
        _dirty = true;
        RefreshDetails(Math.Min(_currentIndex, _document.Details.Count - 1));
    }

    private void HandleSelectionChanged()
    {
        if (_suppressSelection) return;
        var nextIndex = _detailsList.SelectedIndex;
        if (_pendingEditorChanges && _currentIndex >= 0 && nextIndex != _currentIndex)
        {
            var answer = MessageBox.Show(
                this,
                "当前记录有未应用的修改，是否先应用？\r\n选择“否”将放弃本次编辑。",
                "未应用的修改",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel)
            {
                _suppressSelection = true;
                _detailsList.SelectedIndex = _currentIndex;
                _suppressSelection = false;
                return;
            }

            if (answer == DialogResult.Yes)
            {
                try
                {
                    TryApplyCurrentOrThrow();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "记录格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _suppressSelection = true;
                    _detailsList.SelectedIndex = _currentIndex;
                    _suppressSelection = false;
                    return;
                }
            }
            else
            {
                _pendingEditorChanges = false;
            }

            _suppressSelection = true;
            _detailsList.SelectedIndex = nextIndex;
            _suppressSelection = false;
        }

        LoadCurrentDetail();
    }

    private void TryApplyCurrent()
    {
        try
        {
            TryApplyCurrentOrThrow();
            _statusLabel.Text = "当前记录已应用。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "记录格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TryApplyCurrentOrThrow()
    {
        if (_document is null || _currentIndex < 0 || _currentIndex >= _document.Details.Count) return;
        var detail = _document.Details[_currentIndex];
        detail.EpisodeMasterId = ParseLong(_episodeMasterIdBox.Text, "剧情 ID");
        detail.Order = ParseInt(_orderBox.Text, "顺序");
        detail.GroupOrder = ParseInt(_groupOrderBox.Text, "组顺序");
        detail.SpeakerName = _speakerNameBox.Text;
        detail.Title = _titleBox.Text;
        detail.Effect = _effectBox.Text;
        detail.BackgroundImageFileName = _backgroundBox.Text;
        detail.BackgroundCharacterImageFileName = _backgroundCharacterBox.Text;
        detail.BgmFileName = _bgmBox.Text;
        detail.SeFileName = _seBox.Text;
        detail.VoiceFileName = _voiceBox.Text;
        detail.StillPhotoFileName = _stillPhotoBox.Text;
        detail.MovieFileName = _movieBox.Text;
        detail.SpeakerIconId = _speakerIconBox.Text;
        detail.Phrase = _phraseBox.Text;
        detail.CharacterMotions ??= [];
        _dirty = true;
        _pendingEditorChanges = false;
        RefreshListText(_currentIndex);
    }

    private void RefreshDetails(int selectedIndex)
    {
        _suppressSelection = true;
        _detailsList.BeginUpdate();
        try
        {
            _detailsList.Items.Clear();
            if (_document is not null)
            {
                foreach (var detail in _document.Details)
                    _detailsList.Items.Add(FormatDetail(detail));
            }
            _currentIndex = _document is null || _document.Details.Count == 0
                ? -1
                : Math.Clamp(selectedIndex, 0, _document.Details.Count - 1);
            _detailsList.SelectedIndex = _currentIndex;
        }
        finally
        {
            _detailsList.EndUpdate();
            _suppressSelection = false;
        }
        LoadCurrentDetail();
        _pathBox.Text = _sourcePath ?? string.Empty;
        UpdateUiState();
    }

    private void RefreshListText(int index)
    {
        if (_document is null || index < 0 || index >= _detailsList.Items.Count) return;
        _suppressSelection = true;
        _detailsList.Items[index] = FormatDetail(_document.Details[index]);
        _detailsList.SelectedIndex = index;
        _suppressSelection = false;
    }

    private void LoadCurrentDetail()
    {
        if (_document is null || _detailsList.SelectedIndex < 0 || _detailsList.SelectedIndex >= _document.Details.Count)
        {
            _currentIndex = -1;
            _pendingEditorChanges = false;
            ClearEditor();
            UpdateUiState();
            return;
        }

        _currentIndex = _detailsList.SelectedIndex;
        var detail = _document.Details[_currentIndex];
        _loadingEditor = true;
        try
        {
            _idBox.Text = detail.Id.ToString();
            _episodeMasterIdBox.Text = detail.EpisodeMasterId.ToString();
            _orderBox.Text = detail.Order.ToString();
            _groupOrderBox.Text = detail.GroupOrder.ToString();
            _speakerNameBox.Text = detail.SpeakerName;
            _titleBox.Text = detail.Title;
            _effectBox.Text = detail.Effect;
            _backgroundBox.Text = detail.BackgroundImageFileName;
            _backgroundCharacterBox.Text = detail.BackgroundCharacterImageFileName;
            _bgmBox.Text = detail.BgmFileName;
            _seBox.Text = detail.SeFileName;
            _voiceBox.Text = detail.VoiceFileName;
            _stillPhotoBox.Text = detail.StillPhotoFileName;
            _movieBox.Text = detail.MovieFileName;
            _speakerIconBox.Text = detail.SpeakerIconId;
            _phraseBox.Text = detail.Phrase;
            _pendingEditorChanges = false;
        }
        finally
        {
            _loadingEditor = false;
        }
        UpdateUiState();
    }

    private void ClearEditor()
    {
        _loadingEditor = true;
        foreach (var box in EditableBoxes(includeReadOnly: true))
        {
            box.Clear();
        }
        _loadingEditor = false;
        _pendingEditorChanges = false;
    }

    private IEnumerable<TextBox> EditableBoxes(bool includeReadOnly = false)
    {
        if (includeReadOnly)
            yield return _idBox;
        yield return _episodeMasterIdBox;
        yield return _orderBox;
        yield return _groupOrderBox;
        yield return _speakerNameBox;
        yield return _titleBox;
        yield return _effectBox;
        yield return _backgroundBox;
        yield return _backgroundCharacterBox;
        yield return _bgmBox;
        yield return _seBox;
        yield return _voiceBox;
        yield return _stillPhotoBox;
        yield return _movieBox;
        yield return _speakerIconBox;
        yield return _phraseBox;
    }

    private void UpdateUiState()
    {
        var hasDocument = _document is not null;
        var hasDetail = hasDocument && _currentIndex >= 0;
        _saveButton.Enabled = hasDocument && !_busy;
        _saveAsButton.Enabled = hasDocument && !_busy;
        _packButton.Enabled = hasDocument && !_busy;
        _newButton.Enabled = hasDocument && !_busy;
        _duplicateButton.Enabled = hasDetail && !_busy;
        _deleteButton.Enabled = hasDetail && !_busy;
        _applyButton.Enabled = hasDetail && !_busy;
        _resetButton.Enabled = hasDetail && !_busy;
        _detailsList.Enabled = hasDocument && !_busy;
        foreach (var box in new[]
        {
            _episodeMasterIdBox, _orderBox, _groupOrderBox, _speakerNameBox, _titleBox, _effectBox,
            _backgroundBox, _backgroundCharacterBox, _bgmBox, _seBox, _voiceBox, _stillPhotoBox,
            _movieBox, _speakerIconBox, _phraseBox
        })
        {
            box.Enabled = hasDetail && !_busy;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        UpdateUiState();
    }

    private bool ConfirmDiscard()
        => MessageBox.Show(this, "当前剧情有未保存的更改，是否放弃？", "未保存的更改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    private bool? ConfirmOverwrite(string path)
    {
        if (!File.Exists(path)) return true;
        var answer = MessageBox.Show(this, $"文件已存在，是否覆盖？\r\n{path}", "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return answer == DialogResult.Yes ? true : null;
    }

    private string? ChooseJsonPath()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "剧情 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = Path.GetFileName(_sourcePath ?? "episode.json")
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private string? ChooseBinPath()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "剧情 BIN (*.bin)|*.bin|所有文件 (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(_sourcePath ?? "episode") + ".bin"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private static string FormatDetail(EpisodeDetailResult detail)
    {
        var phrase = detail.Phrase.Replace("\r", " ").Replace("\n", " ");
        if (phrase.Length > 40) phrase = phrase[..40] + "...";
        return $"{detail.Order,4} | {detail.SpeakerName} | {phrase}";
    }

    private static long ParseLong(string value, string name)
        => long.TryParse(value, out var result) ? result : throw new ArgumentException($"{name} 必须是整数。");

    private static int ParseInt(string value, string name)
        => int.TryParse(value, out var result) ? result : throw new ArgumentException($"{name} 必须是整数。");

    private static EpisodeDetailResult CloneDetail(EpisodeDetailResult source)
        => new()
        {
            Id = source.Id,
            EpisodeMasterId = source.EpisodeMasterId,
            Order = source.Order,
            GroupOrder = source.GroupOrder,
            Effect = source.Effect,
            SpeakerName = source.SpeakerName,
            FontSize = source.FontSize,
            Phrase = source.Phrase,
            Title = source.Title,
            BackgroundImageFileName = source.BackgroundImageFileName,
            BackgroundCharacterImageFileName = source.BackgroundCharacterImageFileName,
            BackgroundImageFileFadeType = source.BackgroundImageFileFadeType,
            FadeValue1 = source.FadeValue1,
            FadeValue2 = source.FadeValue2,
            FadeValue3 = source.FadeValue3,
            BgmFileName = source.BgmFileName,
            SeFileName = source.SeFileName,
            StillPhotoFileName = source.StillPhotoFileName,
            MovieFileName = source.MovieFileName,
            WindowEffect = source.WindowEffect,
            SceneCameraMasterId = source.SceneCameraMasterId,
            VoiceFileName = source.VoiceFileName,
            CharacterMotions = source.CharacterMotions?.Select(CloneMotion).ToArray() ?? [],
            SpeakerIconId = source.SpeakerIconId
        };

    private static EpisodeDetailCharacterMotionResult CloneMotion(EpisodeDetailCharacterMotionResult motion)
        => new()
        {
            SlotNumber = motion.SlotNumber,
            FacialExpressionMasterId = motion.FacialExpressionMasterId,
            HeadMotionMasterId = motion.HeadMotionMasterId,
            HeadDirectionMasterId = motion.HeadDirectionMasterId,
            BodyMotionMasterId = motion.BodyMotionMasterId,
            LipSyncMasterId = motion.LipSyncMasterId,
            SpineId = motion.SpineId,
            CharacterAppearanceType = motion.CharacterAppearanceType,
            CharacterPosition = motion.CharacterPosition,
            CharacterLayerType = motion.CharacterLayerType,
            SpineSize = motion.SpineSize
        };
}
