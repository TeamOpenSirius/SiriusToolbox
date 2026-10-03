# SiriusData rolling Release packages

Ordinary `dotnet build` / `dotnet restore` downloads the two public assets from
`https://github.com/TeamOpenSirius/SiriusData/releases/download/latest/` using the
MSBuild DownloadFile task. GitHub Packages credentials, curl, PowerShell and a
SiriusData source checkout are not required. This uses the mutable **latest tag**,
not GitHub's latest stable-release API.

PackageReferences use `*-*` so `1.0.2-ci.xx` prereleases are eligible. Toolbox keeps
these versions in Directory.Packages.props (central package management).

Directory.Build.props enables rolling restore for the repository's projects.
Directory.Build.targets evaluates configuration after the project body, so setting
UseSiriusDataRollingPackages in a csproj works. The preparation target runs before
NuGet's _GetRestoreSettings for every project, including transitive consumers and
tests; merely hooking BeforeTargets=Restore does not cover that restore graph.

Each project owns these generated directories:

- `obj/siriusdata-download`: the two fresh Release downloads.
- `obj/siriusdata-feed`: ID/version-validated packages renamed to NuGet's required
  `<id>.<version>.nupkg` filenames, plus `resolved-version.txt`.
- `obj/siriusdata-packages`: an isolated package cache. Restore removes only its
  sirius.protocol and sirius.masterdata entries; other dependencies are retained.

Only after both downloads pass identity/version-pair validation does preparation
replace the local feed and clear Sirius caches. Download failures fail the restore;
there is no silent fallback to an earlier package. A release updated halfway through
the two downloads produces a version mismatch and requires another restore.

The three repositories use the same targets. Nuget.config removes the retired
GitHub Packages/local-source mappings, keeps nuget.org, and the additional project
source supplies the temporary feed. Other private feeds can still be configured as
additional sources. Each project's isolated cache also receives third-party packages,
so first restore uses more disk/network than the former shared global cache.

Run `dotnet restore`, then `dotnet build --no-restore` / `dotnet publish --no-restore`
for a fixed release. No download or cache cleanup occurs during --no-restore builds.
Record the resolved versions from project.assets.json and the release handoff. The
next restore intentionally fetches again. Do not run two independent restore/build
commands against the same project concurrently: they share that project's obj.
If rolling publication occurs during a multi-project restore, compare all resolved
Sirius versions before packaging and repeat restore if they differ.

CI no longer needs a read:packages token or a protocol submodule. Network access to
GitHub Release assets and nuget.org is required during restore. To reuse a verified
restore offline, use --no-restore; this is not an offline first-build mechanism.
The opt-out property disables only automatic fetching; a caller opting out must
supply compatible sources and packages themselves.

## 中文

普通 dotnet build/restore 会自动下载 SiriusData 的 latest Release 两个包，使用
`*-*` 接受 ci 预发布版本。无需 GitHub Packages 令牌，也无需子模块或同级源码。
每个项目使用自己的 obj 临时源和包缓存；每次还原重新下载、校验包 ID 和两包版本，
并清理本项目缓存中的 Sirius 包，避免全局旧稳定版抢先命中。失败会明确报错。

下载后必须改名为包 ID 加实际版本号，NuGet 才能从本地源解析版本。还原准备挂接到
每个依赖项目的还原图阶段，配置在项目正文之后求值，覆盖解决方案与传递依赖。

验证后用 --no-restore 构建/发布，不再下载。生成发布包前记录并核对各项目解析的版本。
首次还原需要联网，且每项目隔离缓存会增加磁盘用量；同一项目不要同时运行多个构建或还原。
