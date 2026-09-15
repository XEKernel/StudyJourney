# local-nupkg — 本机离线 NuGet 包源

## 为什么存在这个目录

本机（开发机）的 **`api.nuget.org` 被网络拦截**：请求会 302 跳到 `nuget.azure.cn`，
然后返回 **空 body**，导致 `dotnet restore` 取不到包。

但 **`www.nuget.org` 本身可达**（HTTP 200），且它的**旧版 v2 打包端点**可以直接下载 `.nupkg`：

```
https://www.nuget.org/api/v2/package/<包ID>/<版本>
```

所以做法是：把需要的包手动下载到这个目录，在 `nuget.config` 里把它注册成一个额外的包源。
`nuget.config` **刻意不写 `<clear/>`**，保留 nuget.org —— CI（GitHub Actions）网络正常，
直接从 nuget.org 还原，不需要这个目录里有东西。

## 当前需要的包

| 包 | 版本 | 用途 | 大小 |
|---|---|---|---|
| `Docnet.Core` | 2.6.0 | PDF 渲染（PDFium 的 .NET 封装，**自带各平台原生库**） | ~18 MB |

`Docnet.Core` 自带原生库，**不需要**额外的 native 包：

- `runtimes/win-x64/native/pdfium.dll`（约 4.7 MB）← 本项目发布用这个
- 另有 linux-x64 / linux-arm / linux-arm64 / osx-x64 / osx-arm64 / win-x86

## 补齐命令（换机器 / 清缓存后执行）

```bash
cd <仓库根>
mkdir -p local-nupkg
curl -sL -o local-nupkg/Docnet.Core.2.6.0.nupkg \
  "https://www.nuget.org/api/v2/package/Docnet.Core/2.6.0"
```

校验（应为 `HTTP 200`、大小约 18429655 字节）：

```bash
curl -sL -o /dev/null -w "HTTP %{http_code} size=%{size_download}\n" \
  "https://www.nuget.org/api/v2/package/Docnet.Core/2.6.0"
```

## 注意

- `*.nupkg` 已在 `.gitignore` 里排除，**不会进仓库**（CI 不需要它）。
- 若某天 `api.nuget.org` 恢复正常，可以删掉本目录与 `nuget.config` 里的 `local-nupkg` 源。
- 本目录必须**存在**（仓库里有这个 README 占位），否则本地源指向空路径会有还原告警。

## 许可

`Docnet.Core` 采用 MIT 许可，其捆绑的 PDFium 原生库采用 BSD-3-Clause（Chromium 项目）。
两者均与本项目 MIT 兼容。
