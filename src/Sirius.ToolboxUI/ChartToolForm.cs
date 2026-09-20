using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Sirius.Toolbox.Charts;

namespace Sirius.ToolboxUI;

public sealed class ChartToolForm : Form
{
    private enum ChartOperation
    {
        Text,
        Encode,
        Decode,
        SelfTest
    }

    private readonly ComboBox _operationBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly TextBox _inputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _textOutputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _keyBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _ignoreWaveOffsetBox = new() { AutoSize = true, Text = "忽略 WAVEOFFSET" };
    private readonly CheckBox _strictBox = new() { AutoSize = true, Text = "严格模式" };
    private readonly CheckBox _overwriteBox = new() { AutoSize = true, Text = "允许覆盖已有文件" };
    private readonly Button _executeButton = new() { AutoSize = true, Text = "执行" };
    private readonly Button _selfTestButton = new() { AutoSize = true, Text = "运行自测" };
    private readonly RichTextBox _logBox = new()
    {
        BackColor = SystemColors.Window,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9F),
        ReadOnly = true,
        WordWrap = false
    };

    private bool _busy;

    public ChartToolForm()
    {
        Text = "谱面工具";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 600);
        Size = new Size(1080, 720);
        _keyBox.Text = "REMOVED_SECRET";

        BuildUi();
        _operationBox.SelectedIndexChanged += (_, _) => UpdateOperationState();
        _executeButton.Click += async (_, _) => await ExecuteAsync();
        _selfTestButton.Click += async (_, _) => await RunSelfTestAsync();
        UpdateOperationState();
    }

    private void BuildUi()
    {
        _operationBox.Items.AddRange(["SUS 转换为文本", "SUS 转换并编码为 ENC", "ENC 解码为文本", "加解密自测"]);
        _operationBox.SelectedIndex = 0;

        var inputBrowse = new Button { AutoSize = true, Text = "选择文件..." };
        inputBrowse.Click += (_, _) => BrowseInput();
        var outputBrowse = new Button { AutoSize = true, Text = "选择文件..." };
        outputBrowse.Click += (_, _) => BrowseOutput();
        var textOutputBrowse = new Button { AutoSize = true, Text = "选择文件..." };
        textOutputBrowse.Click += (_, _) => BrowseTextOutput();

        var form = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            RowCount = 7
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { AutoSize = true, Text = "操作：", Anchor = AnchorStyles.Left }, 0, 0);
        form.Controls.Add(_operationBox, 1, 0);
        form.SetColumnSpan(_operationBox, 2);
        form.Controls.Add(new Label { AutoSize = true, Text = "输入文件：", Anchor = AnchorStyles.Left }, 0, 1);
        form.Controls.Add(_inputBox, 1, 1);
        form.Controls.Add(inputBrowse, 2, 1);
        form.Controls.Add(new Label { AutoSize = true, Text = "输出文件：", Anchor = AnchorStyles.Left }, 0, 2);
        form.Controls.Add(_outputBox, 1, 2);
        form.Controls.Add(outputBrowse, 2, 2);
        form.Controls.Add(new Label { AutoSize = true, Text = "中间文本：", Anchor = AnchorStyles.Left }, 0, 3);
        form.Controls.Add(_textOutputBox, 1, 3);
        form.Controls.Add(textOutputBrowse, 2, 3);
        form.Controls.Add(new Label { AutoSize = true, Text = "谱面密钥：", Anchor = AnchorStyles.Left }, 0, 4);
        form.Controls.Add(_keyBox, 1, 4);
        form.SetColumnSpan(_keyBox, 2);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        options.Controls.Add(_ignoreWaveOffsetBox);
        options.Controls.Add(_strictBox);
        options.Controls.Add(_overwriteBox);
        options.Controls.Add(_executeButton);
        options.Controls.Add(_selfTestButton);
        form.Controls.Add(options, 0, 5);
        form.SetColumnSpan(options, 3);

        var help = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "ENC 操作需要 32 字节 UTF-8 密钥；密钥不会写入日志。"
        };
        form.Controls.Add(help, 0, 6);
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
        using var dialog = new OpenFileDialog
        {
            Filter = _operationBox.SelectedIndex == (int)ChartOperation.Decode
                ? "Chart ENC 文件 (*.enc)|*.enc|所有文件 (*.*)|*.*"
                : "SUS 文件 (*.sus)|*.sus|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _inputBox.Text = dialog.FileName;
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = _operationBox.SelectedIndex == (int)ChartOperation.Encode
                ? "Chart ENC 文件 (*.enc)|*.enc|所有文件 (*.*)|*.*"
                : "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.FileName;
    }

    private void BrowseTextOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _textOutputBox.Text = dialog.FileName;
    }

    private void UpdateOperationState()
    {
        var operation = (ChartOperation)_operationBox.SelectedIndex;
        var needsInput = operation != ChartOperation.SelfTest;
        var needsKey = operation is ChartOperation.Encode or ChartOperation.Decode;
        _inputBox.Enabled = needsInput;
        _outputBox.Enabled = needsInput;
        _textOutputBox.Enabled = operation == ChartOperation.Encode;
        _keyBox.Enabled = needsKey;
        _ignoreWaveOffsetBox.Enabled = operation is ChartOperation.Text or ChartOperation.Encode;
        _strictBox.Enabled = operation is ChartOperation.Text or ChartOperation.Encode;
        _overwriteBox.Enabled = needsInput;
    }

    private async Task ExecuteAsync()
    {
        if (_busy) return;
        try
        {
            var operation = (ChartOperation)_operationBox.SelectedIndex;
            if (operation == ChartOperation.SelfTest)
            {
                await RunSelfTestAsync();
                return;
            }

            var input = RequirePath(_inputBox.Text, "请输入输入文件路径。");
            var output = RequirePath(_outputBox.Text, "请输入输出文件路径。");
            if (!File.Exists(input)) throw new FileNotFoundException("找不到输入文件。", input);
            var key = operation is ChartOperation.Encode or ChartOperation.Decode ? RequireKey() : string.Empty;
            var textOutput = operation == ChartOperation.Encode && !string.IsNullOrWhiteSpace(_textOutputBox.Text)
                ? Path.GetFullPath(_textOutputBox.Text.Trim())
                : null;
            EnsureOutput(output);
            if (textOutput is not null) EnsureOutput(textOutput);
            SetBusy(true);
            AppendLog($"开始：{_operationBox.SelectedItem}");

            var service = new ChartToolService();
            var options = new ChartConvertOptions(_ignoreWaveOffsetBox.Checked, _strictBox.Checked);
            if (operation == ChartOperation.Text)
            {
                var result = await Task.Run(() => service.ConvertToText(input, output, options));
                AppendResult(result.NoteCount, result.OutputPath, result.Warnings);
            }
            else if (operation == ChartOperation.Encode)
            {
                var result = await Task.Run(() => service.Encode(input, output, key, textOutput, options));
                AppendResult(result.NoteCount, result.OutputPath, result.Warnings);
                if (result.TextOutputPath is not null) AppendLog($"中间文本：{result.TextOutputPath}");
            }
            else
            {
                var result = await Task.Run(() => service.Decode(input, output, key));
                AppendLog($"已解码：{result.OutputPath}");
            }
        }
        catch (Exception exception)
        {
            AppendLog($"失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "Chart 工具错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunSelfTestAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            var result = await Task.Run(() => new ChartToolService().SelfTest());
            AppendLog($"自测通过：{result.EncodedLength} 字节");
        }
        catch (Exception exception)
        {
            AppendLog($"自测失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "Chart 工具错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string RequireKey()
    {
        var key = _keyBox.Text;
        if (Encoding.UTF8.GetByteCount(key) != 32)
            throw new ArgumentException("Chart 密钥必须正好编码为 32 个 UTF-8 字节。");
        return key;
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
        _selfTestButton.Enabled = !busy;
        if (!busy) UpdateOperationState();
    }

    private void AppendResult(int noteCount, string outputPath, IReadOnlyList<string> warnings)
    {
        AppendLog($"完成：{noteCount} 个音符，输出：{outputPath}");
        foreach (var warning in warnings) AppendLog($"警告：{warning}");
    }

    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
