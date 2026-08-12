using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

/// <summary>设置页接口：Load 从设置读入控件，Apply 将控件写回设置</summary>
public interface ISettingsPage
{
    void Load(AppSettings s);
    void Apply(AppSettings s);
}
