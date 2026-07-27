using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ClipDwg.Style;

namespace ClipDwg.Ui;

/// <summary>CLIPDWGCFG 옵션 대화상자. 색상별 선두께와 출력 축척을 편집한다.</summary>
public sealed class OptionsForm : Form
{
    private readonly SettingsFile _settings;

    private readonly ComboBox _profileBox = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _fontGrid = new();
    private readonly TextBox _defaultShxFont = new();
    private readonly TextBox _defaultWidth = new();
    private readonly TextBox _minWidth = new();
    private readonly TextBox _mmPerUnit = new();
    private readonly TextBox _outputScale = new();
    private readonly TextBox _margin = new();
    private readonly CheckBox _whiteToBlack = new();
    private readonly CheckBox _forceBlack = new();
    private readonly Label _pathLabel = new();

    private Profile _current;
    private bool _switchingProfile;

    /// <summary>[저장]을 눌렀을 때의 최종 설정. 취소하면 null.</summary>
    public SettingsFile? Result { get; private set; }

    public OptionsForm(SettingsFile settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (_settings.Profiles.Count == 0)
            _settings.Profiles.Add(new Profile());

        _current = _settings.GetActiveProfile();

        BuildLayout();
        LoadProfileList();
        LoadProfile(_current);
    }

    // ---- 레이아웃 -------------------------------------------------------

    private void BuildLayout()
    {
        Text = "clipdwg 옵션";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 560);
        MinimumSize = new Size(460, 480);
        Font = SystemFonts.MessageBoxFont;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var widthTab = new TabPage("색상별 선두께") { Padding = new Padding(8), UseVisualStyleBackColor = true };
        widthTab.Controls.Add(BuildWidthGroup());
        var fontTab = new TabPage("SHX 글꼴 대체") { Padding = new Padding(8), UseVisualStyleBackColor = true };
        fontTab.Controls.Add(BuildFontGroup());
        tabs.TabPages.Add(widthTab);
        tabs.TabPages.Add(fontTab);

        root.Controls.Add(BuildProfileRow(), 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(BuildScaleGroup(), 0, 2);
        root.Controls.Add(BuildPathLabel(), 0, 3);
        root.Controls.Add(BuildButtonRow(), 0, 4);

        Controls.Add(root);
    }

    private Control BuildProfileRow()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };

        panel.Controls.Add(new Label { Text = "프로파일", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });

        _profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileBox.Width = 180;
        _profileBox.SelectedIndexChanged += OnProfileChanged;
        panel.Controls.Add(_profileBox);

        panel.Controls.Add(MakeButton("추가", OnAddProfile));
        panel.Controls.Add(MakeButton("이름 변경", OnRenameProfile));
        panel.Controls.Add(MakeButton("삭제", OnDeleteProfile));

        return panel;
    }

    private Control BuildFontGroup()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        _fontGrid.Dock = DockStyle.Fill;
        _fontGrid.AllowUserToAddRows = false;
        _fontGrid.AllowUserToResizeRows = false;
        _fontGrid.RowHeadersVisible = false;
        _fontGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _fontGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _fontGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "shx", HeaderText = "SHX 글꼴 (확장자 생략 가능)", FillWeight = 50 });
        _fontGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "font", HeaderText = "대신 쓸 TrueType 글꼴", FillWeight = 50 });

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        top.Controls.Add(new Label
        {
            Text = "규칙 없는 SHX 기본 글꼴",
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
        });
        _defaultShxFont.Width = 160;
        top.Controls.Add(_defaultShxFont);

        var hint = new Label
        {
            Text = "SHX는 윤곽선 글꼴이 아니라 EMF에 글자로 넣을 수 없어 TrueType으로 바꿔 그립니다. 자간이 원본과 다를 수 있습니다.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 32,
            ForeColor = SystemColors.GrayText,
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        buttons.Controls.Add(MakeButton("행 추가", (_, _) => _fontGrid.Rows.Add("", "")));
        buttons.Controls.Add(MakeButton("행 삭제", (_, _) => DeleteSelectedRows(_fontGrid)));

        panel.Controls.Add(_fontGrid);
        panel.Controls.Add(buttons);
        panel.Controls.Add(top);
        panel.Controls.Add(hint);
        return panel;
    }

    private Control BuildWidthGroup()
    {
        var box = new Panel { Dock = DockStyle.Fill };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "color",
            HeaderText = "색상 (ACI 번호 또는 #RRGGBB)",
            FillWeight = 55,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "width",
            HeaderText = "두께 (mm)",
            FillWeight = 25,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "note",
            HeaderText = "",
            FillWeight = 20,
            ReadOnly = true,
        });

        _grid.CellFormatting += OnCellFormatting;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        buttons.Controls.Add(MakeButton("행 추가", (_, _) => _grid.Rows.Add("", "0.25", "")));
        buttons.Controls.Add(MakeButton("행 삭제", (_, _) => DeleteSelectedRows(_grid)));

        box.Controls.Add(_grid);
        box.Controls.Add(buttons);
        return box;
    }

    private Control BuildScaleGroup()
    {
        var box = new GroupBox { Text = "출력", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));

        AddField(table, "규칙 없는 색 두께 (mm)", _defaultWidth, 0, 0);
        AddField(table, "최소 두께 (mm)", _minWidth, 2, 0);
        AddField(table, "도면 단위 1 = ? mm", _mmPerUnit, 0, 1);
        AddField(table, "출력 축척", _outputScale, 2, 1);
        AddField(table, "여백 (mm)", _margin, 0, 2);

        _whiteToBlack.Text = "흰색을 검정으로 (흰 배경 문서용)";
        _whiteToBlack.AutoSize = true;
        _forceBlack.Text = "모든 선을 검정으로";
        _forceBlack.AutoSize = true;

        table.Controls.Add(_whiteToBlack, 2, 2);
        table.SetColumnSpan(_whiteToBlack, 2);
        table.Controls.Add(_forceBlack, 0, 3);
        table.SetColumnSpan(_forceBlack, 4);

        box.Controls.Add(table);
        return box;
    }

    private Control BuildPathLabel()
    {
        _pathLabel.Text = "설정 파일: " + SettingsStore.FilePath;
        _pathLabel.AutoSize = true;
        _pathLabel.ForeColor = SystemColors.GrayText;
        _pathLabel.Margin = new Padding(0, 8, 0, 4);
        return _pathLabel;
    }

    private Control BuildButtonRow()
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };

        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "저장", AutoSize = true };
        save.Click += OnSave;

        panel.Controls.Add(cancel);
        panel.Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        return panel;
    }

    private static void AddField(TableLayoutPanel table, string label, TextBox box, int column, int row)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, column, row);
        box.Dock = DockStyle.Fill;
        table.Controls.Add(box, column + 1, row);
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        b.Click += onClick;
        return b;
    }

    // ---- 프로파일 -------------------------------------------------------

    private void LoadProfileList()
    {
        _switchingProfile = true;
        _profileBox.Items.Clear();
        foreach (Profile p in _settings.Profiles)
            _profileBox.Items.Add(p.Name);
        _profileBox.SelectedItem = _current.Name;
        _switchingProfile = false;
    }

    private void LoadProfile(Profile p)
    {
        _current = p;

        _grid.Rows.Clear();
        foreach (WidthRule rule in p.Widths)
        {
            string color = !string.IsNullOrWhiteSpace(rule.Rgb)
                ? rule.Rgb!
                : rule.Aci.ToString(CultureInfo.InvariantCulture);
            _grid.Rows.Add(color, Format(rule.Mm), "");
        }

        _fontGrid.Rows.Clear();
        foreach (FontSubstitute s in p.ShxSubstitutes)
            _fontGrid.Rows.Add(s.Shx ?? string.Empty, s.Font ?? string.Empty);

        _defaultShxFont.Text = p.DefaultShxFont;
        _defaultWidth.Text = Format(p.DefaultWidthMm);
        _minWidth.Text = Format(p.MinWidthMm);
        _mmPerUnit.Text = Format(p.MmPerDrawingUnit);
        _outputScale.Text = Format(p.OutputScale);
        _margin.Text = Format(p.MarginMm);
        _whiteToBlack.Checked = p.WhiteToBlack;
        _forceBlack.Checked = p.ForceBlack;
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        if (_switchingProfile || _profileBox.SelectedItem is null)
            return;

        // 전환 전에 현재 화면 내용을 프로파일에 담아 둔다.
        if (!TryCollectInto(_current, showErrors: true))
        {
            _switchingProfile = true;
            _profileBox.SelectedItem = _current.Name;
            _switchingProfile = false;
            return;
        }

        string name = (string)_profileBox.SelectedItem;
        Profile? next = _settings.Profiles.Find(p => p.Name == name);
        if (next is not null)
            LoadProfile(next);
    }

    private void OnAddProfile(object? sender, EventArgs e)
    {
        if (!TryCollectInto(_current, showErrors: true))
            return;

        string? name = Prompt("새 프로파일 이름", "profile" + (_settings.Profiles.Count + 1));
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_settings.Profiles.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 프로파일이 이미 있습니다.", "clipdwg");
            return;
        }

        Profile copy = _current.Clone();
        copy.Name = name!.Trim();
        _settings.Profiles.Add(copy);
        _current = copy;
        LoadProfileList();
        LoadProfile(copy);
    }

    private void OnRenameProfile(object? sender, EventArgs e)
    {
        string? name = Prompt("프로파일 이름", _current.Name);
        if (string.IsNullOrWhiteSpace(name) || name == _current.Name)
            return;

        if (_settings.Profiles.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 프로파일이 이미 있습니다.", "clipdwg");
            return;
        }

        _current.Name = name!.Trim();
        LoadProfileList();
    }

    private void OnDeleteProfile(object? sender, EventArgs e)
    {
        if (_settings.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "프로파일이 하나뿐이라 삭제할 수 없습니다.", "clipdwg");
            return;
        }

        if (MessageBox.Show(this, $"프로파일 '{_current.Name}' 을(를) 삭제할까요?", "clipdwg",
                MessageBoxButtons.OKCancel) != DialogResult.OK)
            return;

        _settings.Profiles.Remove(_current);
        _current = _settings.Profiles[0];
        LoadProfileList();
        LoadProfile(_current);
    }

    private static void DeleteSelectedRows(DataGridView grid)
    {
        var doomed = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in grid.SelectedRows)
        {
            if (!row.IsNewRow)
                doomed.Add(row);
        }

        foreach (DataGridViewRow row in doomed)
            grid.Rows.Remove(row);
    }

    // ---- 화면 → 모델 ----------------------------------------------------

    private void OnSave(object? sender, EventArgs e)
    {
        if (!TryCollectInto(_current, showErrors: true))
            return;

        _settings.ActiveProfile = _current.Name;
        Result = _settings;
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool TryCollectInto(Profile target, bool showErrors)
    {
        var rules = new List<WidthRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
                continue;

            string color = (row.Cells["color"].Value as string ?? string.Empty).Trim();
            string width = (row.Cells["width"].Value as string ?? string.Empty).Trim();

            if (color.Length == 0 && width.Length == 0)
                continue;

            if (!TryParseNumber(width, out double mm) || mm < 0)
                return Fail(showErrors, $"'{color}' 행의 두께 '{width}' 를 읽을 수 없습니다.");

            if (!seen.Add(color))
                return Fail(showErrors, $"색상 '{color}' 규칙이 중복됩니다.");

            if (ColorWeightMap.TryParseRgb(color, out int rgb))
            {
                rules.Add(new WidthRule { Rgb = ColorWeightMap.FormatRgb(rgb), Mm = mm });
            }
            else if (int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aci)
                     && aci is >= 1 and <= 255)
            {
                rules.Add(new WidthRule { Aci = aci, Mm = mm });
            }
            else
            {
                return Fail(showErrors, $"'{color}' 는 ACI 번호(1~255)도 #RRGGBB 도 아닙니다.");
            }
        }

        if (!TryParseNumber(_defaultWidth.Text, out double defaultWidth) || defaultWidth < 0)
            return Fail(showErrors, "'규칙 없는 색 두께' 값을 읽을 수 없습니다.");
        if (!TryParseNumber(_minWidth.Text, out double minWidth) || minWidth < 0)
            return Fail(showErrors, "'최소 두께' 값을 읽을 수 없습니다.");
        if (!TryParseNumber(_mmPerUnit.Text, out double mmPerUnit) || mmPerUnit <= 0)
            return Fail(showErrors, "'도면 단위' 값은 0보다 커야 합니다.");
        if (!TryParseNumber(_outputScale.Text, out double outputScale) || outputScale <= 0)
            return Fail(showErrors, "'출력 축척' 값은 0보다 커야 합니다.");
        if (!TryParseNumber(_margin.Text, out double margin) || margin < 0)
            return Fail(showErrors, "'여백' 값을 읽을 수 없습니다.");

        var substitutes = new List<FontSubstitute>();
        foreach (DataGridViewRow row in _fontGrid.Rows)
        {
            if (row.IsNewRow)
                continue;

            string shx = (row.Cells["shx"].Value as string ?? string.Empty).Trim();
            string font = (row.Cells["font"].Value as string ?? string.Empty).Trim();

            if (shx.Length == 0 && font.Length == 0)
                continue;
            if (shx.Length == 0 || font.Length == 0)
                return Fail(showErrors, "SHX 글꼴 대체는 두 칸을 모두 채워야 합니다.");

            substitutes.Add(new FontSubstitute { Shx = shx, Font = font });
        }

        string defaultShx = _defaultShxFont.Text.Trim();
        if (defaultShx.Length == 0)
            return Fail(showErrors, "'규칙 없는 SHX 기본 글꼴' 을 비워 둘 수 없습니다.");

        target.ShxSubstitutes = substitutes;
        target.DefaultShxFont = defaultShx;
        target.Widths = rules;
        target.DefaultWidthMm = defaultWidth;
        target.MinWidthMm = minWidth;
        target.MmPerDrawingUnit = mmPerUnit;
        target.OutputScale = outputScale;
        target.MarginMm = margin;
        target.WhiteToBlack = _whiteToBlack.Checked;
        target.ForceBlack = _forceBlack.Checked;
        return true;
    }

    private bool Fail(bool showErrors, string message)
    {
        if (showErrors)
            MessageBox.Show(this, message, "clipdwg", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    /// <summary>사용자가 소수점을 로캘대로 찍든 '.' 로 찍든 받아 준다.</summary>
    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(double value) => value.ToString("0.####", CultureInfo.CurrentCulture);

    // ---- 표 꾸미기 ------------------------------------------------------

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            return;

        string color = (_grid.Rows[e.RowIndex].Cells["color"].Value as string ?? string.Empty).Trim();

        if (_grid.Columns[e.ColumnIndex].Name == "color")
        {
            StyleColorCell(e.CellStyle, color);
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "note")
        {
            e.Value = int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aci)
                ? AciPalette.DescribeAci(aci)
                : string.Empty;
        }
    }

    /// <summary>
    /// 색 칸에 견본색을 입힌다. 알아볼 수 없는 색이면 손대지 않고 기본 모양으로 둔다.
    /// <para>
    /// 표가 <see cref="DataGridViewSelectionMode.FullRowSelect"/>라 행을 고르면 선택색이 셀
    /// 배경을 통째로 덮어 견본이 가려진다. 견본은 이 표의 요점이므로 선택색도 같은 색으로
    /// 지정해 선택한 행에서도 그대로 보이게 한다. 행이 선택됐다는 표시는 나머지 칸의
    /// 반전으로 충분하다.
    /// </para>
    /// </summary>
    internal static void StyleColorCell(DataGridViewCellStyle style, string color)
    {
        if (!TryGetSwatch(color, out Color swatch))
            return;

        Color ink = Contrast(swatch);
        style.BackColor = swatch;
        style.ForeColor = ink;
        style.SelectionBackColor = swatch;
        style.SelectionForeColor = ink;
    }

    private static bool TryGetSwatch(string color, out Color swatch)
    {
        if (ColorWeightMap.TryParseRgb(color, out int rgb))
        {
            swatch = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        if (int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aci)
            && AciPalette.TryGetColor(aci, out swatch))
            return true;

        swatch = SystemColors.Window;
        return false;
    }

    private static Color Contrast(Color background) =>
        (background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114) > 150
            ? Color.Black
            : Color.White;

    // ---- 간단 입력 상자 --------------------------------------------------

    private string? Prompt(string caption, string initial)
    {
        using var dialog = new Form
        {
            Text = caption,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 100),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = SystemFonts.MessageBoxFont,
        };

        var input = new TextBox { Text = initial, Left = 12, Top = 16, Width = 296 };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Left = 152, Top = 56, Width = 75 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Left = 233, Top = 56, Width = 75 };

        dialog.Controls.AddRange(new Control[] { input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }
}
