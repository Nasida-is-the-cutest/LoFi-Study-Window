# 省电学习小窗使用说明

这是基于开源项目 [iGPU Savior](https://github.com/Small-tailqwq/iGPUSaviorMod) 1.9.4 的衍生改进版。目标是在学习时常驻显示原游戏的番茄钟，并减少持续绘制房间画面的开销。首次安装和升级见 [INSTALL.md](INSTALL.md)，当前验证结果见 [VALIDATION.md](VALIDATION.md)；20% 功耗降低目标仍需电池实测确认。

## 开始使用

1. 退出游戏，保留现有插件及配置的备份。
2. 游戏需要 Windows x64 版 BepInEx 5。本次构建使用 5.4.23.5。首次安装时，从 [BepInEx 官方发布页](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) 下载 Windows x64 包，解压到 `Chill With You.exe` 所在目录，通过 Steam 启动一次再退出；已有 BepInEx 5 的安装可直接替换插件。
3. 将安装包里的 `BepInEx/plugins/iGPU Savior.dll` 放入游戏目录的同名位置。整个 `BepInEx/plugins` 目录（包括子文件夹）只保留一个本插件 DLL，避免重复加载。
4. 通过 Steam 启动游戏，完成教程并进入普通房间。房间空闲、番茄钟服务准备好后会自动进入学习小窗。登录、加载和教程阶段不会自动隐藏界面。
5. 若番茄钟尚未开始，点击“原窗口”设置并启动番茄钟，然后按 F3 返回小窗。

安装包包含本插件、许可证、说明和功耗测量脚本；BepInEx 加载器需单独安装。测试探针、游戏程序集、存档及构建用 SDK 不包含在安装包内。

## 日常操作

| 操作 | 效果 |
|---|---|
| 拖动顶部空白区域 | 移动小窗 |
| 拖动窗口边缘 | 自由调整尺寸 |
| 暂停／继续 | 调用原游戏番茄钟服务 |
| 动画／静态 | 切换到按钮所示的画面模式 |
| 原窗口或 F3 | 退出学习界面，恢复进入前的分辨率、全屏状态和窗口样式 |
| F2 | 原有土豆模式；学习模式的省电策略优先 |
| F4／F5 | 原有镜像／竖屏功能；静态背景随后更新 |
| MOD 设置页 | 调整自动进入、画面模式、窗口比例、拖动方式和快捷键 |

首次小窗为 **480×270 DIP**，距离所在屏幕工作区右下角 24 DIP；实际像素随 Windows 缩放比例变化。窗口记录位置、大小和保存时的 DPI，恢复到对应屏幕；屏幕移除后回到可用工作区。常驻置顶遵循 Windows 普通桌面的置顶规则，不占用键盘焦点。

小窗顶部不再显示操作说明，但顶部 30 DIP 区域仍可拖动。底部三个按钮使用同色的半透明深蓝圆角背景，普通状态不透明度为 42%（透明度 58%），点击后不会持续突出中间按钮。按钮固定在窗口底部，缩放时保持统一间距；此次外观改动没有添加持续动画或图片纹理。

## 画面和省电行为

| 状态 | 游戏逻辑更新目标 | 画面更新目标 | 房间画面 |
|---|---:|---:|---|
| 默认静态模式 | 10 次/秒 | 1 次/秒 | 保存一张背景后暂停相机绘制 |
| 动画模式 | 15 次/秒 | 5 次/秒 | 持续低帧率绘制 |
| 操作后 3 秒 | 15 次/秒 | 15 次/秒 | 静态模式仍保持静态背景 |
| 不可见／最小化 | 10 次/秒 | 1 次/秒 | 暂停房间绘制，保留计时逻辑和音频 |

番茄钟数字来自原服务的剩余时间。进入学习模式后，原计时器使用高精度系统时钟推进，减少低帧率和窗口切换造成的误差；同一轮计时在返回原窗口后继续使用这个时钟，重置时重新初始化。暂停、继续、跳过及结束仍由原游戏服务处理。本插件不另外启动倒计时、不自行播放完成音，也不另写学习统计。

原游戏界面的输入和按钮会在退出小窗后恢复。故事、笔记、音乐等完整操作可在原窗口中进行。当前小窗显示的是番茄钟，正向计时器暂不显示。Windows 休眠／唤醒时的计时行为尚未实测，当前验收覆盖电脑保持唤醒的学习使用。

绘制房间时使用 0.4 的 URP 渲染比例，并关闭阴影、HDR、MSAA 和后处理权重；文字以小窗输出分辨率绘制。背景只在进入静态模式、调整尺寸或改变镜像／竖屏时重新捕获。以上是代码中的目标策略，不能直接换算为电池续航提升。

镜像输出纹理保留窗口输出尺寸，URP 在内部应用渲染比例，避免再次缩放。实现依据：[Unity 2022.3 URP 的纹理描述符逻辑](https://github.com/Unity-Technologies/Graphics/blob/2022.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRenderPipelineCore.cs)。

## 配置与恢复

配置文件为 `BepInEx/config/chillwithyou.potatomode.cfg`。

- `[Learning] AutoStart=false`：关闭自动进入；仍可按 F3 手动打开。
- `[Learning] View=Static` 或 `Animated`：默认画面模式。
- `[LearningWindow] X/Y/Width/Height/Dpi`：自动记录的窗口位置和尺寸。退出游戏后可把 X、Y 改回 `-2147483648`，Width、Height 改回 0，以重置默认位置和大小。

原有配置键仍保留。旧的“PiP 自动隐藏 GUI”键对新的学习面板没有作用，因此新设置页不再显示这个开关。“窗口比例”现在用于在设置页中主动调整小窗，首次大小采用上面的固定默认值。

回退时退出游戏，将备份的原版 DLL 放回插件目录。新配置节可保留，原版会忽略它们。本次改进未改动原游戏安装目录和游戏程序集。

## 开发构建

主项目目标为 .NET Framework 4.7.2，测试项目为 .NET 8。`GameDir` 应包含游戏程序集和 BepInEx/core。

```powershell
dotnet build 'iGPU Savior/iGPU Savior.csproj' -c Release -p:GameDir='你的游戏目录'
dotnet test 'iGPU Savior.Tests/iGPU Savior.Tests.csproj'
dotnet run --project 'scripts/window-validation/WindowTests.csproj' -c Release
pwsh -File scripts/sync-version.ps1 -CheckOnly
```

窗口专项测试只创建测试用的隐藏窗口，其中一个属于单独进程；不会操作用户其他应用。版本仍为 1.9.4，正式发版前需另行确认版本号。
