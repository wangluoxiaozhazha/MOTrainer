# MOTrainer

[English](README.md) | [简体中文](README.zh-CN.md)

MOTrainer 是一个面向《Mental Omega》的非官方开源离线修改器。项目独立实现，
用于单机环境中的单位、资源、生产、战场状态和超级武器调整。

> 本项目仅供离线游戏使用，不提供反作弊绕过，也不建议在任何联机环境中运行。

## 本仓库的增强版

本仓库基于原开源项目
[cmdrmo/MOTrainer](https://github.com/cmdrmo/MOTrainer)
进行增强，原作者与原项目仍是上游来源。

本版本新增或改进的功能：

- 修改器打开后即可配置金钱、选中单位属性和全部功能开关，不必先启动游戏或进入地图。
- 游戏未启动时可以先把功能排入队列；检测到经过校验的游戏模块后自动应用。
- 金钱按配置值在每一局开始时自动应用一次，不持续覆盖局内的正常花费。
- 选中单位修改和功能策略在换地图、返回主菜单、重新开局后继续生效。
- 所有状态保持到关闭修改器为止；退出时自动还原修改器拥有的补丁和策略。
- EXE 内置管理员权限清单，可直接访问与游戏相同权限级别的进程。
- 新增启动前排队、逐局重新应用和退出还原的自动化测试。

![MOTrainer 英文界面](docs/images/motrainer-english.png)

## 项目状态

本增强版在同一个程序中支持自动、英文和简体中文界面，并支持下文记录的经典版与
Steam 版 YR 1.001 已验证游戏配置。

- [原项目地址](https://github.com/cmdrmo/MOTrainer)
- [兼容性与故障排查](docs/COMPATIBILITY.zh-CN.md)
- [提交增强版 Issue](https://github.com/wangluoxiaozhazha/MOTrainer/issues)

## 主要功能

### 选中单位

- 修改当前玩家已选中对象的等级和生命。
- 事件模式：选择形成时应用一次，适合常规自动升星。
- 轮询模式：持续应用当前设置，适合锁定生命和兼容特殊选择路径。
- 占有选中对象。
- 为全部选中对象加血或将生命扣至 1，不区分归属。

### 全局状态

- 锁定经济、锁定电量。
- 仅当前玩家快速生产。
- 随处建造、自动维修。
- 解锁建造数量、解锁队列上限。
- 操纵全部、心灵探测、无视隐形、无视黑雾。
- 科技全开：阵营模式与扩展模式。

全部功能开关都可以在修改器打开后预先配置。游戏未启动时显示等待状态；检测到
经过校验的游戏模块后自动应用，并在换地图后继续保持，直到关闭修改器。

### 自动保持

- 金钱可在进图前设置，每局开始时自动应用一次。
- 选中单位等级和生命可在进图前启用自动修改。
- 自动策略会保持到关闭修改器，并在退出时释放。

### 一次性功能

- 修改当前玩家经济。
- 将首个选中对象所属阵营的经济归零。
- 令首个选中对象所属阵营投降。
- 批量发放已勾选的八种超级武器。

所有热键均可在程序内修改、保存和恢复默认。热键与鼠标操作调用相同的功能路径。

## 兼容性

| 项目 | 当前范围 |
| --- | --- |
| 操作系统 | Windows |
| 目标进程 | `gamemd.exe`，32 位（x86） |
| 默认运行环境 | .NET Framework 4.8 |
| 界面语言 | 自动、英文、简体中文 |
| 使用场景 | Mental Omega 离线战局 |
| 已验证游戏版本 | 经典 YR 1.001；Steam YR 1.001（2024） |

程序会校验目标模块的 SHA-256。模块版本未知、战局结构未就绪、补丁字节不一致或
写入回读失败时，受影响的操作会被拒绝，不会尝试猜测地址。

准确的模块指纹、运行状态说明、权限排查和版本反馈方法见
[兼容性与故障排查](docs/COMPATIBILITY.zh-CN.md)。

## 下载与使用

请从本项目的
[GitHub Releases](https://github.com/wangluoxiaozhazha/MOTrainer/releases)
下载发布包，不要使用不明来源重新打包的版本。Release 提供 `.sha256` 文件时，
建议在解压前核对 ZIP。

发布包中的以下三个文件必须放在同一目录：

```text
MOTrainer.exe
MOTrainer.Core.dll
MOTrainer.exe.config
```

双语构建还包含一个 `zh-Hans` 目录，其中有两个卫星资源 DLL。该目录必须与
`MOTrainer.exe` 一起保留；删除后简体中文资源将不可用。

1. 启动 `MOTrainer.exe`，在 Windows UAC 提示中选择“是”。
2. 先配置金钱、单位属性和需要启用的功能开关。
3. 启动 Mental Omega；游戏版本校验完成后，排队功能会自动应用。
4. 进入离线战局，使用已配置的功能。

增强版 EXE 已内置管理员权限清单，不再需要单独的 VBS 启动器。

`MOTrainer.exe.config` 保存界面语言、编辑数值、超级武器选择、功能模式和自定义
热键。更新前建议备份该文件；如果旧配置被新版本判定为无效，请恢复新发布包附带的
默认配置。

如果系统缺少运行环境，请安装官方
[.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)。

## 安全边界

MOTrainer 不复制或修改 Mental Omega 的游戏文件。功能通过读取和写入当前游戏进程
实现；修改器退出时会尝试恢复由本次运行启用的持续补丁。

已经发生的一次性结果无法自动撤销，部分游戏自身持久化的状态也可能继续影响当前
存档。测试候选版本前建议先保存游戏进度。

由于进程内存读写、远程代码页和全局热键等行为，安全软件可能将发布包标记为风险
程序。请只使用项目正式发布的文件并核对 SHA-256；如果仍有疑虑，可以审查源码并
自行构建。项目不会要求关闭安全软件，也不会要求将整个游戏目录加入排除列表。

## 从源码构建

需要安装能够构建 .NET 8 和 .NET Framework 4.8 目标的 .NET SDK。

```powershell
dotnet build .\app\MOTrainer.sln --configuration Release
dotnet run --project .\app\tests\MOTrainer.Core.SmokeTests `
  --configuration Release
```

当前自动化测试包含 41 项烟雾测试，覆盖核心状态转换、安全拒绝、回滚路径、启动前
排队、跨战局策略重新应用、本地化资源和设置兼容性，但不代替特定 Mental Omega
版本中的实机验证。

默认的紧凑型 .NET Framework 4.8 发布命令：

```powershell
dotnet publish .\app\src\MOTrainer.WinForms\MOTrainer.WinForms.csproj `
  --configuration Release `
  --framework net48 `
  -p:PlatformTarget=x86 `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  --no-self-contained `
  --output .\dist\win-x86-net48
```

## 项目结构

```text
app/src/MOTrainer.Core/              进程、内存、版本、功能与安全检查
app/src/MOTrainer.WinForms/          Windows 图形界面
app/tests/MOTrainer.Core.SmokeTests/ 无第三方测试框架的轻量烟雾测试
docs/                                兼容性与维护说明
```

后续代码维护采用随功能改动逐步偿还结构债的方式，详见
[渐进维护与结构偿债计划](docs/MAINTENANCE_PLAN.md)。

## 项目关系与致谢

本增强版基于原开源项目
[cmdrmo/MOTrainer](https://github.com/cmdrmo/MOTrainer)，
并保留上游的 GPL-3.0-only 授权。MOTrainer 不是 Mental Omega、Electronic
Arts 或任何既有修改器的官方项目，也不与这些项目存在隶属关系。具体说明见
[致谢与项目边界](ACKNOWLEDGEMENTS.zh-CN.md)。

本仓库不包含或分发游戏本体、Mental Omega 文件、第三方修改器文件或它们的资源。

## 许可证

MOTrainer 以 [GNU General Public License v3.0 only](LICENSE)
（`GPL-3.0-only`）发布。许可证不授予任何第三方游戏、模组、名称或资源的权利。
