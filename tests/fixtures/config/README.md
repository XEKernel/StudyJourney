# 配置分发夹具（S8 / A0-3 真源样本）

这两个文件是**真实的 v3.3.0 `settings.json`**，不是手搓的干净小 JSON。
手搓文件测不出格式保真问题（CRLF / 2 空格缩进 / 中文 \uXXXX 转义 / 无 BOM / 无结尾换行）。

| 文件 | 来源（交接方机器） | 字节 | 顶层键 | 行数 |
|---|---|---:|---:|---:|
| `v330-root-settings.json` | 仓库根 `settings.json`（v3.3.0 同期配置） | 3,067 | 93 | 94 |
| `v330-teacher-settings.json` | `StudyJourney.Avalonia/bin/Release/net10.0/settings.json`（老师机运行期数据） | 4,901 | 96 | 147 |

## 为什么 `v330-teacher-settings.json` 是"脱敏但等长"的

原文件里 `Teachers[]` 有 7 个 `PasswordHash`（PBKDF2 哈希）。**哈希也是凭据**，不应进仓库。
处理方式：只把 `PasswordHash` 的值替换成**等长的占位串**（保留 `PBKDF2$100000$` 与 `$` 分隔符、
每段长度不变，其余字符换成 `A`）。**其它字符一个都没动**，字节长度仍为 4,901，
因此格式保真（A0-3）的验证强度不受影响。

原文件 SHA-256 前 16 位：`2eb161c7e0e90e85`（脱敏后不再匹配，这是预期的）。

## 两份文件共同的格式特征（正是 A0-3 要守的）

- LF 全部是 `\r\n`（无裸 LF），**无结尾换行**；
- **无 BOM**；
- 2 空格缩进，`": "` 与 `",\r\n"` 分隔；
- 非 ASCII 字符**全部** \uXXXX 转义（文件里 0 个非 ASCII 字节）；
- 数组/嵌套对象（`SubjectPresentations` / `CustomCountdowns` / `Teachers`）按 STJ 默认缩进排布。
