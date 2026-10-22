# 安装、升级与回退

这是《放松时光：与你共享 Lo-Fi 故事》的 Windows x64 插件，基于 [Small-tailqwq 的 iGPU Savior](https://github.com/Small-tailqwq/iGPUSaviorMod) 改进。请从本仓库的 [Releases](../../../releases/latest) 下载已编译的插件 ZIP；GitHub 的 “Source code” 压缩包是源码，不等同于可直接安装的插件包。

## 先找到游戏目录

在 Steam 中右键游戏，选择“管理 → 浏览本地文件”。本文的“游戏目录”是 **`Chill With You.exe` 所在的文件夹**，不是它的上级文件夹。

安装前通过游戏菜单退出，并确认游戏已结束运行。配置和旧插件先做备份；安装脚本也会在覆盖插件前创建备份。安装和卸载脚本只处理本插件，不修改游戏存档。

## 首次使用 MOD：先安装 BepInEx 5

已有可正常加载插件的 BepInEx 5 时，跳过这一节。

1. 前往 [BepInEx 官方发布页](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)，下载 **Windows x64 版 BepInEx 5**。本项目已验证 5.4.23.5，不使用 BepInEx 6。
2. 将加载器压缩包的内容解压到游戏目录，让 `winhttp.dll`、`doorstop_config.ini` 等加载器文件与 `Chill With You.exe` 同级，而不是多套一层文件夹。
3. 通过 Steam 启动游戏一次，再正常退出。确认游戏目录出现 `BepInEx/plugins` 和 `BepInEx/config`。

插件发布包不附带 BepInEx、游戏本体或游戏程序集。如果加载器尚未正常工作，先解决加载器安装，再安装本插件。

## 安装或升级插件

下载 [最新插件 ZIP](../../../releases/latest) 并解压到一个独立文件夹。选择下面一种方法即可。

### 方法一：复制 DLL

1. 完全退出游戏。
2. 若已安装 iGPU Savior 或本衍生版，先把旧 DLL 复制到游戏目录之外的备份文件夹；也可备份 `BepInEx/config/chillwithyou.potatomode.cfg`。
3. 将解压包里的 `BepInEx/plugins/iGPU Savior.dll` 复制到游戏目录的相同位置，替换旧文件。
4. 检查整个 `BepInEx/plugins`，包括子文件夹：**本插件只能保留一份 DLL**。原版 iGPU Savior 与本衍生版使用相同的插件标识，不能并存。不要把备份 DLL 留在 `plugins` 里，即使改了文件名也可能被加载。
5. 通过 Steam 启动游戏。

正确目录示例：

```text
游戏目录/
├─ Chill With You.exe
├─ winhttp.dll
└─ BepInEx/
   ├─ core/
   ├─ config/
   └─ plugins/
      └─ iGPU Savior.dll
```

### 方法二：PowerShell 安装脚本

解压后可直接双击 **`安装.cmd`**。脚本会自动查找常见 Steam 路径及二级库；找不到游戏时，用下面的命令指定目录。若 Windows 提示需要写入游戏目录的权限，可在管理员 PowerShell 中运行同一命令。

在解压后的发布包文件夹打开 PowerShell。将示例路径替换成自己的游戏目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story'
```

脚本源文件见 [install.ps1](../install.ps1)。它会检查游戏目录与必要前置、确认游戏已退出、备份现有插件及配置，再复制 DLL；请保留脚本输出的备份路径。此命令的执行策略设置仅作用于这一次 PowerShell 进程。

脚本不会自动安装 BepInEx。出现校验失败时，先按输出修正目录、加载器或重复插件问题，再重试；不要在游戏运行中强行替换 DLL。

## 第一次使用

1. 启动游戏、完成教程并进入普通房间。登录和加载过程中不会自动隐藏游戏界面。
2. 番茄钟服务准备好后，默认自动进入学习小窗；也可以按 **F3** 手动切换。
3. 若尚未开始番茄钟，点击 **原窗口**，设置时长并开始计时，再按 F3 返回。
4. 使用 **暂停／继续** 控制计时；**动画／静态** 切换画面；拖动顶部空白区域移动小窗，拖动边缘调整尺寸。

默认使用静态背景以减少持续绘制房间。三个按钮配色相同，文字表示点击后的操作。小窗的位置和大小会自动记住，完整说明见 [LEARNING_WINDOW.md](LEARNING_WINDOW.md)。

## 配置和故障排查

配置文件为 `BepInEx/config/chillwithyou.potatomode.cfg`，首次成功加载后生成。大部分选项可在游戏内 MOD 设置页调整。希望关闭自动进入小窗时，退出游戏后修改：

```ini
[Learning]
AutoStart = false
```

| 现象 | 检查方法 |
|---|---|
| 没有小窗，F3 无效 | 确认已进入普通房间、BepInEx 5 正常加载、DLL 放在正确目录；查看 `BepInEx/LogOutput.log` |
| 提示重复插件／行为异常 | 搜索 `plugins` 及子目录，只保留一份本插件 DLL，移走原版或旧版备份 |
| 计时尚未开始 | 返回原窗口，在游戏中设置并启动番茄钟；小窗不另建独立计时器 |
| 想操作故事、音乐或笔记 | 点击“原窗口”或按 F3 |
| 窗口位置不合适 | 拖动顶部调整；位置重置配置见 [完整说明](LEARNING_WINDOW.md#配置与恢复) |
| 更新游戏后无法使用 | 保留日志、记录游戏版本，按下节回退并提交可复现的问题 |

公开提交日志前，请移除个人路径、账号和其他隐私信息。已验证的游戏和加载器版本、功耗未实测等边界见 [VALIDATION.md](VALIDATION.md)。

## 回退或卸载

**先完全退出游戏。**

回退到旧版时，将当前 `iGPU Savior.dll` 移出 `BepInEx/plugins`，再从备份取回旧 DLL；需要恢复原设置时，一并取回备份的配置文件。新增的学习配置节也可保留，原版会忽略它们。

只想卸载本插件时，可使用发布包内的脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story'
```

也可双击发布包里的 **`卸载.cmd`**。脚本源文件见 [uninstall.ps1](../uninstall.ps1)：通过安装脚本安装后，它会将当前 DLL 备份移出 `plugins`，并恢复安装前的旧 DLL；原来没有插件时则移走本插件。配置与游戏存档保留，不卸载 BepInEx 或其他 MOD。若采用手动复制安装，请按本节开头的手动方法回退。重新通过 Steam 启动游戏后生效。

不需要删除游戏存档，也不需要重新安装游戏。
