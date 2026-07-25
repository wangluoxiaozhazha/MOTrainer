# MOTrainer

MOTrainer 是一个面向《Mental Omega》的非官方离线修改器。项目独立实现，
用于单机环境中的单位、资源、生产、战场状态和超级武器调整。

项目目前处于首次公开发布前的整理阶段。程序主体已经完成，公开文档、发布包和
最终版本信息仍在准备中。

> 本项目仅供离线游戏使用，不提供反作弊绕过，也不建议在任何联机环境中运行。

当前源码版本：`0.1.0-rc1`

## 主要功能

### 选中单位

- 修改当前玩家已选中对象的等级和生命
- 事件模式：选择形成时应用一次，适合常规自动升星
- 轮询模式：持续应用当前设置，适合锁定生命和兼容特殊选择路径
- 占有选中对象
- 为全部选中对象加血或将生命扣至 1，不区分归属

### 全局状态

- 锁定经济、锁定电量
- 仅当前玩家快速生产
- 随处建造、自动维修
- 解锁建造数量、解锁队列上限
- 操纵全部、心灵探测、无视隐形、无视黑雾
- 科技全开：阵营模式与扩展模式

`解锁建造数量` 和 `解锁队列上限` 可以在进入战局前预设；程序会在对应结构
就绪后应用。

### 一次性功能

- 修改当前玩家经济
- 将首个选中对象所属阵营的经济归零
- 令首个选中对象所属阵营投降
- 批量发放已勾选的八种超级武器

所有热键均可在程序内修改、保存和恢复默认。热键与鼠标操作调用相同的功能路径。

## 兼容性

- Windows
- 32 位（x86）游戏进程
- 默认发布包使用 .NET Framework 4.8
- 当前仅支持一组经过实机验证的 `gamemd.exe` 与 `Ares.dll`
- 当前界面和公开文档以简体中文为主

程序会校验目标模块的 SHA-256。模块版本未知、战局结构未就绪或读取结果异常时，
受影响的操作会被拒绝，不会尝试猜测地址。

准确的模块指纹、运行状态说明和版本反馈方法见
[兼容性说明](docs/COMPATIBILITY.md)。

## 使用

首次公开发布完成后，请从本项目的 GitHub Releases 页面下载发布包。不要从
不明来源下载重新打包的版本。

发布包中的以下三个文件必须放在同一目录：

```text
MOTrainer.exe
MOTrainer.Core.dll
MOTrainer.exe.config
```

运行步骤：

1. 启动 Mental Omega。
2. 启动 `MOTrainer.exe`。
3. 等待顶部状态栏显示版本已确认；进入战局后使用对应功能。

`MOTrainer.exe.config` 同时保存运行时要求和用户设置。更新版本前建议备份该
文件；需要保留热键和界面选项时，可继续使用原配置。如果新版本提示配置无效，
请换回随新版本提供的默认配置。

如果系统缺少运行环境，请安装
[Microsoft .NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)。

## 安全边界

MOTrainer 不复制或修改 Mental Omega 的游戏文件。功能通过读取和写入当前
游戏进程实现，修改器退出时会尝试恢复由本次运行启用的持续补丁。

由于进程内存读写、远程代码页和全局热键等行为，安全软件可能将发布包标记为
风险程序。请只使用项目正式发布的文件并核对发布页提供的 SHA-256；如果仍有
疑虑，可以自行审查源码并构建。

## 从源码构建

需要安装能够构建 .NET 8 和 .NET Framework 4.8 目标的 .NET SDK。

```powershell
dotnet build .\app\MOTrainer.sln --configuration Release
dotnet run --project .\app\tests\MOTrainer.Core.SmokeTests `
  --configuration Release
```

当前自动化测试包含 30 项 Core 烟雾测试。它们覆盖核心状态转换和安全拒绝路径，
但不代替特定 Mental Omega 版本中的实机验证。

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
app/src/MOTrainer.Core/             进程、内存、版本、功能与安全检查
app/src/MOTrainer.WinForms/         Windows 图形界面
app/tests/MOTrainer.Core.SmokeTests/无第三方测试框架的 Core 烟雾测试
docs/                               公开说明
```

## 项目关系与致谢

MOTrainer 不是 Mental Omega、Electronic Arts 或任何既有修改器的官方项目，
也不与这些项目存在隶属关系。设计和兼容性研究参考了社区公开资料、既有工具的
可观察行为和长期游戏经验，具体说明见[致谢](ACKNOWLEDGEMENTS.md)。

本仓库不包含或分发游戏本体、Mental Omega 文件、第三方修改器文件或它们的资源。

## 许可证

MOTrainer 以 [GNU General Public License v3.0 only](LICENSE)
（`GPL-3.0-only`）发布。
