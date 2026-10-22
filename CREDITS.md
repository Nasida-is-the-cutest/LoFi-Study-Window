# 致谢与来源

## iGPU Savior

**Lo-Fi 学习小窗基于并借鉴开源项目 [iGPU Savior](https://github.com/Small-tailqwq/iGPUSaviorMod)，原作者为 [Small-tailqwq](https://github.com/Small-tailqwq)。**

本项目是独立维护的衍生改进版。原项目提供了 BepInEx 插件架构、性能策略、窗口／PiP、镜像、竖屏、MOD 设置、笔记导出等基础功能。本衍生版在此基础上增加置顶学习面板、原番茄钟适配、静态背景策略及简化按钮界面，并修复线程调度、资源清理、窗口校验和异常恢复等问题。

这不是从零实现，也不是原作者或游戏官方发布的新版本。仓库保留原项目提交历史，以便查看来源和后续改动。

原项目采用 MIT 许可证。仓库与发布包保留 [LICENSE.txt](LICENSE.txt) 中的原版权和许可文本：

```text
Copyright (c) 2025 Small-tailqwq
```

感谢原作者公开代码，提供可供学习和继续改进的基础。原项目的第一人称介绍、开发经历、测试截图和性能百分比，不作为本衍生版作者经历或实测结果；本版验证记录见 [docs/VALIDATION.md](docs/VALIDATION.md)。

## BepInEx 与其他依赖

- [BepInEx](https://github.com/BepInEx/BepInEx)：Unity 插件加载框架，本项目使用 BepInEx 5；加载器由用户另行安装。
- [Harmony](https://github.com/pardeike/Harmony)：运行时方法补丁机制，使用 BepInEx 环境提供的程序集。
- Unity 与《Chill with You: Lo-Fi Story》：游戏运行环境和本地构建所需 API，相关游戏文件、程序集、音乐及美术资源不随本插件分发。

各依赖保留各自的许可和权利；本项目的 MIT 许可证不替代它们的许可证。
