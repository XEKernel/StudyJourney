using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

/// <summary>
/// 设置页接口：Load 从设置读入控件，Apply 将控件写回设置。
/// IsDirty（#8）：本页是否有未保存修改 —— 设置窗口切页/关窗前据此提示，避免静默丢失。
/// 默认 false（无编辑状态的页面无需覆盖）；有直改列表/控件的页按需覆盖。
///
/// <para><b>⚠ 隐含契约：Load 与 Apply 必须作用于【同一个】AppSettings 实例</b>
/// （2026-10-06 由自检实测暴露）。有些字段的 Apply 不是"纯写入"，而是"基于现值计算"：
/// 倒计时页的目标时刻要<b>保留秒</b>（老师改字号时不该把 09:00:37 悄悄变成 09:00:00），
/// 靠的是读传入对象里的现值再写回。换个对象 Apply，那份现值就是空的 → 秒丢失。
/// 页面实现者与调用方都别破坏这条。</para>
///
/// <para>2026-10-06 起逐页 MVVM 化：实现类只负责把 Load/Apply/IsDirty <b>转发给视图模型</b>
/// （见 <c>ViewModels/Settings/</c>），不再自己逐个控件赋值。倒计时页是第一个样板。</para>
/// </summary>
public interface ISettingsPage
{
    void Load(AppSettings s);
    void Apply(AppSettings s);
    bool IsDirty => false;
}
