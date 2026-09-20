using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Episodes;

namespace Sirius.ToolboxUI;

public sealed class EpisodeToolForm : Form
{
    private enum EpisodeOperation
    {
        Pack,
        PackDirectory,
        UnpackDirectory,
        Unpack,
        Inspect
    }

    private readonly ComboBox _operationBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly TextBox _inputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _overwriteBox = new() { AutoSize = true, Text = "允许覆盖已有文件" };
    private readonly Button _executeButton = new() { AutoSize = true, Text = "执行" };
    private readonly RichTextBox _logBox = new()
    {
        BackColor = SystemColors.Window,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9F),
        ReadOnly = true,
        WordWrap = false
    };

    private bool _busy;

    public EpisodeToolForm()
    {
        Text = "剧情工具";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 600);
        Size = new Size(1080, 720);

        BuildUi();
        _operationBox.SelectedIndexChanged += (_, _) => UpdateOperationState();
        _executeButton.Click += async (_, _) => await ExecuteAsync();
        UpdateOperationState();
    }

    private void BuildUi()
    {
        _operationBox.Items.AddRange(["JSON 打包为 BIN", "目录批量打包", "目录批量解包", "BIN 解包为 JSON", "检查 BIN 文件"]);
        _operationBox.SelectedIndex = 0;

        var inputBrowse = new Button { AutoSize = true, Text = "选择..." };
        inputBrowse.Click += (_, _) => BrowseInput();
        var outputBrowse = new Button { AutoSize = true, Text = "选择..." };
        outputBrowse.Click += (_, _) => BrowseOutput();

        var form = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            RowCount = 5
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { AutoSize = true, Text = "操作：", Anchor = AnchorStyles.Left }, 0, 0);
        form.Controls.Add(_operationBox, 1, 0);
        form.SetColumnSpan(_operationBox, 2);
        form.Controls.Add(new Label { AutoSize = true, Text = "输入路径：", Anchor = AnchorStyles.Left }, 0, 1);
        form.Controls.Add(_inputBox, 1, 1);
        form.Controls.Add(inputBrowse, 2, 1);
        form.Controls.Add(new Label { AutoSize = true, Text = "输出路径：", Anchor = AnchorStyles.Left }, 0, 2);
        form.Controls.Add(_outputBox, 1, 2);
        form.Controls.Add(outputBrowse, 2, 2);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        options.Controls.Add(_overwriteBox);
        options.Controls.Add(_executeButton);
        form.Controls.Add(options, 0, 3);
        form.SetColumnSpan(options, 3);

        var help = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "打包会执行 MessagePack LZ4 往返校验；检查操作不会修改文件。"
        };
        form.Controls.Add(help, 0, 4);
        form.SetColumnSpan(help, 3);

        var logLabel = new Label { AutoSize = true, Dock = DockStyle.Top, Text = "运行日志" };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) };
        content.Controls.Add(_logBox);
        content.Controls.Add(logLabel);
        Controls.Add(content);
        Controls.Add(form);
    }

    private void BrowseInput()
    {
        if (_operationBox.SelectedIndex is (int)EpisodeOperation.PackDirectory or (int)EpisodeOperation.UnpackDirectory)
        {
            using var folder = new FolderBrowserDialog
            {
                Description = _operationBox.SelectedIndex == (int)EpisodeOperation.PackDirectory
                    ? "选择剧情 JSON 目录"
                    : "选择剧情 BIN 目录"
            };
            if (folder.ShowDialog(this) == DialogResult.OK) _inputBox.Text = folder.SelectedPath;
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = _operationBox.SelectedIndex == (int)EpisodeOperation.Pack
                ? "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
                : "BIN 文件 (*.bin)|*.bin|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _inputBox.Text = dialog.FileName;
    }

    private void BrowseOutput()
    {
        if (_operationBox.SelectedIndex is (int)EpisodeOperation.PackDirectory or (int)EpisodeOperation.UnpackDirectory)
        {
            using var folder = new FolderBrowserDialog { Description = "选择输出目录" };
            if (folder.ShowDialog(this) == DialogResult.OK) _outputBox.Text = folder.SelectedPath;
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = _operationBox.SelectedIndex == (int)EpisodeOperation.Pack
                ? "BIN 文件 (*.bin)|*.bin|所有文件 (*.*)|*.*"
                : "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dialog.FileName;
    }

    private void UpdateOperationState()
    {
        var operation = (EpisodeOperation)_operationBox.SelectedIndex;
        var inspect = operation == EpisodeOperation.Inspect;
        var directory = operation is EpisodeOperation.PackDirectory or EpisodeOperation.UnpackDirectory;
        _inputBox.Enabled = true;
        _outputBox.Enabled = !inspect;
        _overwriteBox.Enabled = !inspect;
        _outputBox.PlaceholderText = directory
            ? operation == EpisodeOperation.PackDirectory
                ? "留空则使用输入目录名后缀 -bin"
                : "留空则使用输入目录名后缀 -json"
            : "可留空以使用默认扩展名";
    }

    private async Task ExecuteAsync()
    {
        if (_busy) return;
        try
        {
            var operation = (EpisodeOperation)_operationBox.SelectedIndex;
            var input = RequirePath(_inputBox.Text, "请输入输入路径。");
            var output = string.IsNullOrWhiteSpace(_outputBox.Text) ? null : Path.GetFullPath(_outputBox.Text.Trim());
            if (operation is EpisodeOperation.PackDirectory or EpisodeOperation.UnpackDirectory)
            {
                if (!Directory.Exists(input)) throw new DirectoryNotFoundException($"找不到输入目录：{input}");
            }
            else if (!File.Exists(input))
            {
                throw new FileNotFoundException("找不到输入文件。", input);
            }

            if (operation is EpisodeOperation.Pack or EpisodeOperation.Unpack && output is not null)
                EnsureOutput(output);
            SetBusy(true);
            AppendLog($"开始：{_operationBox.SelectedItem}");
            var service = new EpisodeToolService();
            switch (operation)
            {
                case EpisodeOperation.Pack:
                {
                    var result = await Task.Run(() => service.Pack(input, output, _overwriteBox.Checked));
                    AppendLog($"完成：{result.OutputPath}，{result.DetailCount} 条剧情记录，{result.ByteCount} 字节");
                    break;
                }
                case EpisodeOperation.PackDirectory:
                {
                    var result = await Task.Run(() => service.PackDirectory(input, output, _overwriteBox.Checked));
                    AppendBatchResult(input, result);
                    break;
                }
                case EpisodeOperation.UnpackDirectory:
                {
                    var result = await Task.Run(() => service.UnpackDirectory(input, output, _overwriteBox.Checked));
                    AppendBatchResult(input, result);
                    break;
                }
                case EpisodeOperation.Unpack:
                {
                    var result = await Task.Run(() => service.Unpack(input, output, _overwriteBox.Checked));
                    AppendLog($"完成：{result.OutputPath}，{result.DetailCount} 条剧情记录");
                    break;
                }
                default:
                {
                    var result = await Task.Run(() => service.Inspect(input));
                    AppendLog($"文件大小：{result.ByteCount} 字节，EF BF BD 替换序列：{result.ReplacementCount}");
                    AppendLog(result.IsProbablyCorrupt
                        ? $"判断：{result.Error}"
                        : result.Decoded
                            ? $"反序列化成功，剧情记录：{result.DetailCount}"
                            : $"反序列化失败：{result.Error}");
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            AppendLog($"失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "Episode 工具错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string RequirePath(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
        return Path.GetFullPath(value.Trim());
    }

    private void EnsureOutput(string path)
    {
        if (File.Exists(path) && !_overwriteBox.Checked)
            throw new IOException($"输出文件已存在：{path}。请勾选“允许覆盖已有文件”。");
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _operationBox.Enabled = !busy;
        _executeButton.Enabled = !busy;
        if (!busy) UpdateOperationState();
    }

    private void AppendBatchResult(string inputDirectory, EpisodeBatchResult result)
    {
        var succeeded = result.Items.Count(item => item.Succeeded);
        var skipped = result.Items.Count(item => item.Skipped);
        var failed = result.Items.Count - succeeded - skipped;
        foreach (var item in result.Items)
        {
            var relative = Path.GetRelativePath(inputDirectory, item.InputPath);
            AppendLog(item.Skipped
                ? $"跳过：{relative}，{item.Error}"
                : item.Succeeded
                    ? $"成功：{relative}，{item.ByteCount} 字节"
                    : $"失败：{relative}，{item.Error}");
        }
        AppendLog($"批量完成：成功 {succeeded}，跳过 {skipped}，失败 {failed}，输出目录：{result.OutputDirectory}");
    }

    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
