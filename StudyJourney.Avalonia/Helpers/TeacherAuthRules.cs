using System;
using System.Collections.Generic;
using System.Linq;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 教师账号登录判定（v2.29.0 安全加固）——**全是纯函数**，便于自检钉住。
///
/// 修的漏洞（2026-10-01 用户决定"方案 A"）：
///   内置默认账号（`Teacher01/Study@2026`、`teacher01~06/123456`）**写死在源码里**，而本仓库是开源的
///   → 密码等于公开；更糟的是 `FindTeacherForLogin` 在账号表查不到时会**无条件**兜底到这份内置表，
///   于是老师在设置页**删光账号也删不掉它** —— 同一局域网里任何人仍可用 `teacher01/123456`
///   登录远程控制台（改课表、传文件、关屏关机）。
///
/// 改法：配置上带一次性标记 <see cref="AppSettings.AccountsInitialized"/>，据此决定要不要兜底：
///   · **false**（老配置 / settings.json 损坏后重建）→ 仍允许兜底，保证**老用户升级后不会突然登不进**；
///   · **true**（首次运行、以及 <see cref="AppSettings.Load"/> 的一次性迁移都会置上）→ **彻底不兜底**，
///     删掉就是删掉。
///
/// ⚠ 不能拿"账号表是否为空"当判据：老师**故意删光**账号时它就是空的，而那种情况恰恰**必须**不兜底。
/// </summary>
/// <summary>一次性迁移要做什么（v2.29.0 安全加固）</summary>
public enum AccountMigrationAction
{
    /// <summary>已初始化过 → 什么都不做</summary>
    None,
    /// <summary>只补标记（账号表里已经有老师自己的账号）</summary>
    MarkOnly,
    /// <summary>补标记 + 把内置账号写进账号表（否则老用户升级后会突然登不进）</summary>
    MarkAndSeed,
}

public static class TeacherAuthRules
{
    /// <summary>历史内置账号用的公开密码（只用于"是否仍在使用默认密码"的风险提示，不参与登录）</summary>
    public static readonly string[] KnownDefaultPasswords = { "123456", "Study@2026" };

    /// <summary>
    /// 决定一次性迁移动作（**纯函数**）。⚠ 关键：**已初始化就什么都不做** ——
    /// 老师把账号删光是有意为之，不能借迁移把他删掉的账号"复活"回来。
    /// </summary>
    public static AccountMigrationAction DecideMigration(bool accountsInitialized, int teacherCount)
    {
        if (accountsInitialized) return AccountMigrationAction.None;
        return teacherCount == 0 ? AccountMigrationAction.MarkAndSeed : AccountMigrationAction.MarkOnly;
    }

    /// <summary>
    /// 登录查找：先查配置里的账号（用户名 或 显示名 均可）；查不到时**仅在未初始化**的情况才看内置表。
    /// 显示名理论可重复 → 取第一个匹配（班级规模下不会冲突；冲突可在设置页改显示名）。
    /// </summary>
    public static TeacherAccount? FindForLogin(
        IReadOnlyList<TeacherAccount>? configured,
        IReadOnlyList<TeacherAccount>? fallback,
        bool accountsInitialized,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var hit = configured?.FirstOrDefault(a => Matches(a, name));
        if (hit != null) return hit;

        // ★ 本次加固的核心：初始化之后**到此为止**
        if (accountsInitialized) return null;

        return fallback?.FirstOrDefault(a => Matches(a, name));
    }

    private static bool Matches(TeacherAccount a, string name)
        => string.Equals(a.Username, name, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>这个账号是否仍在使用**公开的默认密码**（设置页风险提示用）</summary>
    public static bool UsesDefaultPassword(TeacherAccount? account)
    {
        if (account == null) return false;
        foreach (var p in KnownDefaultPasswords)
        {
            try
            {
                if (account.VerifyPassword(p)) return true;
            }
            catch
            {
                // 哈希字段异常 → 当作"不是默认密码"，宁可漏报也不误报
            }
        }
        return false;
    }

    /// <summary>账号列表里仍在使用默认密码的那些（风险提示要逐个列出来）</summary>
    public static List<TeacherAccount> WithDefaultPassword(IEnumerable<TeacherAccount>? accounts)
        => (accounts ?? Enumerable.Empty<TeacherAccount>()).Where(UsesDefaultPassword).ToList();
}
