using Avalonia.Controls;
using Avalonia.Media;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.ViewModels.Settings;

namespace StudyJourney.Avalonia.Views.Settings;

/// <summary>
/// 「倒计时」设置页 —— <b>MVVM 化样板</b>（2026-10-06）。
///
/// <para>改造前：222 行，Load/Apply/IsDirty 全在页面里，10 个控件靠 <c>x:Name</c> 逐个赋值/取值，
/// 倒计时列表还要命令式重建面板 + 一个 <c>Dispatcher.Post</c> workaround。</para>
/// <para>改造后：页面只剩 <see cref="Load"/> / <see cref="Apply"/> / <see cref="IsDirty"/> 三个
/// <see cref="ISettingsPage"/> 契约方法的转发，加上两件**确实只能由视图做**的事：</para>
/// <list type="number">
/// <item>填充系统字体列表（要问 <see cref="FontManager"/>，属 UI 数据源）；
/// ⚠ 必须<b>先填列表再 <c>vm.Load()</c></b>，否则 ComboBox 还没有 items，
/// 绑定过去的 <c>FontFamily</c> 选不中。</item>
/// <item>色板 → VM 的单向回填：<see cref="Views.ColorSwatch.ValueChanged"/>
/// <b>只在用户点色板时触发</b>（赋 <c>Value</c> 不触发），所以可以直接挂 VM 属性，
/// 不需要在 Load/Apply 里手工搬运字符串。VM → 色板方向由 XAML 绑定自动完成。</item>
/// </list>
/// </summary>
public partial class CountdownPage : UserControl, ISettingsPage
{
    private readonly CountdownPageViewModel _vm;

    public CountdownPage()
    {
        InitializeComponent();

        _vm = new CountdownPageViewModel
        {
            // VM 不直接调 App.ShowMessageAsync（静态 UI API）—— 由这里注入，VM 才能在自检里无 UI 跑
            ReportError = (title, msg) => _ = App.ShowMessageAsync(title, msg),
        };
        DataContext = _vm;

        // 色板 → VM：只有用户真的选了色才通知（ColorSwatch 保证赋 Value 不触发）
        TextColorSwatch.ValueChanged += (_, _) => _vm.TextColorHex = TextColorSwatch.Value;
        AccentColorSwatch.ValueChanged += (_, _) => _vm.AccentColorHex = AccentColorSwatch.Value;
    }

    public void Load(AppSettings s)
    {
        // ① 系统字体列表只填一次（FontManager 是 UI 数据源，留在视图层）
        if (FontFamilyBox.Items.Count == 0)
        {
            foreach (var ff in FontManager.Current.SystemFonts)
                FontFamilyBox.Items.Add(ff.Name);
        }

        // ② 再让 VM 回填 —— 顺序不能反，否则 FontFamily 绑不中
        _vm.Load(s);

        // ③ 首次进入把两个色板与 VM 同步一次（此后靠双向：绑定下行 + ValueChanged 上行）
        TextColorSwatch.Value = _vm.TextColorHex;
        AccentColorSwatch.Value = _vm.AccentColorHex;
    }

    public void Apply(AppSettings s) => _vm.Apply(s);

    /// <summary>切页/关窗提示依据 —— 自定义倒计时是否有未保存修改（语义见 VM 的 IsDirty 注释）</summary>
    public bool IsDirty => _vm.IsDirty;
}
