using System;
using System.ComponentModel;
using Avalonia.Controls;
using StudyJourney.Avalonia.Services.GradeAnalysis;
using StudyJourney.Avalonia.ViewModels.GradeAnalysis;

// ⚠ 必须写 global:: —— 本文件在 StudyJourney.Avalonia.* 命名空间下，
//    直接写 Avalonia.Platform.Storage 会被解析成 StudyJourney.Avalonia.Platform.Storage（CS0234）。
using FilePickerFileType = global::Avalonia.Platform.Storage.FilePickerFileType;
using FilePickerOpenOptions = global::Avalonia.Platform.Storage.FilePickerOpenOptions;

namespace StudyJourney.Avalonia.Views.GradeAnalysis;

/// <summary>
/// 班级成绩分析主窗口：六个页签（排行榜 / 个人详情 / 单人 PK / 小组 PK / 波动分析 / 数据导入）。
///
/// <para><b>为什么这里还有 code-behind</b>：页面数据的组织全在
/// <see cref="GradeAnalysisViewModel"/>（MVVM），这里只做三件视图专属的事 ——
/// ① 注入文件选择器（VM 不该知道窗口与存储 API）；② 导入失败时切到「手动录入」页；
/// ③ 把「显示列」勾选的变动转成一次视图刷新 + 落盘。这三件事离了视图就不成立。</para>
/// </summary>
public partial class GradeAnalysisWindow : Window
{
    /// <summary>全局唯一实例（与课表编辑器同款约定：开多份会让「全班共用一套视图」的语义分裂）。</summary>
    private static GradeAnalysisWindow? _current;

    private readonly GradeAnalysisViewModel _vm;

    /// <summary>供 XAML 设计器/框架实例化用。</summary>
    public GradeAnalysisWindow() : this(new GradeDatabase()) { }

    public GradeAnalysisWindow(GradeDatabase db)
    {
        InitializeComponent();

        try
        {
            db.Initialize();
        }
        catch (Exception ex)
        {
            // 建库失败不能把窗口打不开 —— 先把窗口显示出来，把原因写在错误条上
            _vm = new GradeAnalysisViewModel(db);
            DataContext = _vm;
            _vm.ErrorText = "初始化成绩库失败：" + ex.Message;
            WireEvents();
            return;
        }

        _vm = new GradeAnalysisViewModel(db);
        DataContext = _vm;
        WireEvents();
    }

    private void WireEvents()
    {
        // 导入失败 → 自动切到「手动录入」子页（规格要求）
        _vm.ImportFailed += (_, _) => ImportTabs.SelectedIndex = 1;

        // 任意「显示列」开关变动 → 刷新榜面并保存视图。
        // VM 内部用 _suspendRefresh 挡住了装载期的批量赋值，所以这里不会连环触发。
        _vm.PropertyChanged += OnVmPropertyChanged;

        // 破坏性操作（删除学生 / 移出小组成员 / 删除小组）的确认框由视图注入。
        // 不用 App.ConfirmAsync 那个不带 owner 的老入口 —— 那个的弹窗不跟随本窗口居中。
        _vm.ConfirmAsync = (title, message) => Helpers.DialogHelper.ShowConfirmAsync(this, title, message);

        // 小组配色：VM → 色板方向由 XAML 绑定完成；色板 → VM 方向必须挂事件 ——
        // ColorSwatch.ValueChanged **只在用户点色板时触发**（赋 Value 不触发，这是它的既定语义）。
        GroupColorSwatch.ValueChanged += (_, _) => _vm.SelectedGroupColorHex = GroupColorSwatch.Value;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name is null) return;
        if (name.StartsWith("Show", StringComparison.Ordinal)) _vm.OnViewColumnChanged();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 文件选择器由视图注入（VM 保持对窗口 API 的无知）
        _vm.PickExcelFileAsync = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择成绩表",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel 工作簿")
                    {
                        Patterns = new[] { "*.xlsx" },
                    },
                },
            });
            // 用 IStorageItem.Path（Uri）取本地路径，而不是 TryGetLocalPath() 扩展方法 ——
            // 后者在 Avalonia.Platform.Storage 命名空间下，而本文件所处的
            // StudyJourney.Avalonia.* 会让该命名空间名解析歧义（CS0234 / CS1061）。
            // 非 file:// （云盘虚拟位置）直接返回 null，交给调用方提示。
            if (files.Count == 0) return null;
            var uri = files[0].Path;
            return uri.IsFile ? uri.LocalPath : null;
        };
    }

    /// <summary>全局入口：已开则激活并刷新，未开则新建。</summary>
    public static void ShowOrActivate()
    {
        if (_current is not null)
        {
            try
            {
                _current._vm.RefreshFromExternalChange();
                _current.Show();
                _current.Activate();
                return;
            }
            catch
            {
                _current = null; // 实例已失效，落到下面重建
            }
        }

        var w = new GradeAnalysisWindow();
        _current = w;
        w.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, w)) _current = null;
        };
        w.Show();
    }
}
