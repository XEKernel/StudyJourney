using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 可点击的颜色色板（2026-09-25，规划 2.7 ③）。
///
/// 起因：设置页里每个颜色原来是「hex 输入框 + 预览方块 + 选择…按钮」三件套，共 20 处
/// （考试页 14 + 天气 4 + 倒计时 2）。老师看到的是 `#88FFFFFF` 这种字符串，
/// 既看不懂又能手打错（打错了 Apply 照样落盘，只是主窗口悄悄走兜底色）。
///
/// 现在：**一行 = 一个色板**，整行可点 → 打开颜色选择器（色环 + 明度 + 透明度 + 手动输入 hex）。
/// · 色块直接显示当前颜色，右侧显示 hex（要抄给别人时还能看）；
/// · 值非法时不再"假装正常"：色块画成空心并标注"无法识别"，老师一眼能发现配置坏了；
/// · 落盘仍是原来的 `#AARRGGBB` 字符串 → settings.json 完全兼容，读取方一行都不用改。
/// </summary>
public class ColorSwatch : Button
{
    private readonly TextBlock _caption = new()
    {
        FontSize = 12.5,
        VerticalAlignment = VerticalAlignment.Center,
        Width = 150,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly Border _chip = new()
    {
        Width = 26,
        Height = 26,
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _hexText = new()
    {
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
        Foreground = new SolidColorBrush(Color.Parse("#FF9AA0AA"))
    };

    /// <summary>无参构造（供 XAML 加载器实例化）</summary>
    public ColorSwatch()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,Auto,*") };
        Grid.SetColumn(_caption, 0);
        Grid.SetColumn(_chip, 1);
        Grid.SetColumn(_hexText, 2);
        row.Children.Add(_caption);
        row.Children.Add(_chip);
        row.Children.Add(_hexText);

        Content = row;
        Padding = new Thickness(4, 3);
        MinWidth = 300;
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Click += OnClick;
        ApplyVisual();
    }

    /// <summary>左侧说明文字（如"科目名颜色"）</summary>
    public string Caption
    {
        get => _caption.Text ?? "";
        set => _caption.Text = value;
    }

    /// <summary>当前颜色（`#AARRGGBB` / `#RRGGBB`）。设置即刷新显示，不落盘（落盘由设置页 Apply 负责）</summary>
    public string Value
    {
        get => _value;
        set
        {
            var v = value ?? "";
            if (_value == v) return;      // Load 回填时不触发 ValueChanged，避免误判成"用户改过"
            _value = v;
            ApplyVisual();
        }
    }
    private string _value = "";

    /// <summary>用户**通过色板**改了颜色（Load 回填不触发）</summary>
    public event EventHandler? ValueChanged;

    /// <summary>供设置页在校验前判断色值是否可用</summary>
    public bool IsValid => TryParse(_value, out _);

    /// <summary>解析 hex 颜色；失败返回 false（不做兜底替换 —— 让调用方决定怎么办）</summary>
    public static bool TryParse(string? hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = Color.Parse(hex.Trim()); return true; }
        catch { return false; }
    }

    private void ApplyVisual()
    {
        if (TryParse(_value, out var c))
        {
            _chip.Background = new SolidColorBrush(c);
            _hexText.Text = _value.Trim().ToUpperInvariant();
            ToolTip.SetTip(this, $"当前颜色：{_hexText.Text}（点一下改色）");
        }
        else
        {
            // 不装作正常：画成空心 + 明确文字，老师才能发现"这里配错了"
            _chip.Background = Brushes.Transparent;
            _hexText.Text = string.IsNullOrWhiteSpace(_value)
                ? "（未设置，点击选色）"
                : $"{_value}（无法识别，点击重选）";
            ToolTip.SetTip(this, _hexText.Text);
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new ColorPickerDialog(string.IsNullOrWhiteSpace(_value) ? "#FFFFFFFF" : _value);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) dlg.ShowDialog(owner); else dlg.Show();
        dlg.Closed += (_, _) =>
        {
            if (dlg.SelectedHex == null) return;      // 取消
            Value = dlg.SelectedHex;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
    }
}
