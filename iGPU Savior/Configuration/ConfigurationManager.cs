using BepInEx.Configuration;
using UnityEngine;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Configuration
{
    /// <summary>
    /// 统一管理所有 BepInEx 配置项,提供类型安全的访问接口
    /// </summary>
    public class ConfigurationManager
    {
        private readonly ConfigFile _config;
        public ConfigFile File => _config;

        // ==================== 快捷键配置 ====================
        public ConfigEntry<KeyCode> KeyPotatoMode { get; private set; }
        public ConfigEntry<KeyCode> KeyPiPMode { get; private set; }
        public ConfigEntry<KeyCode> KeyCameraMirror { get; private set; }
        public ConfigEntry<KeyCode> KeyPortraitMode { get; private set; }

        // ==================== 相机配置 ====================
        public ConfigEntry<bool> CfgEnableMirror { get; private set; }
        public ConfigEntry<bool> CfgEnablePortraitMode { get; private set; }
        
        // ==================== 性能配置 ====================
        public ConfigEntry<bool> CfgEnableBackgroundOptimization { get; private set; }

        // ==================== 交互配置 ====================
        public ConfigEntry<bool> CfgEnableDeleteConfirm { get; private set; }

        // ==================== 窗口配置 ====================
        public ConfigEntry<WindowScaleRatio> CfgWindowScale { get; private set; }
        public ConfigEntry<DragMode> CfgDragMode { get; private set; }
        public ConfigEntry<bool> CfgAutoHideGuiInPiP { get; private set; }

        // ==================== 服装配置 ====================
        public ConfigEntry<bool> CfgDisableCostumeRotation { get; private set; }
        public ConfigEntry<string> CfgSuggestedCostumeSkin { get; private set; }

        public ConfigEntry<bool> CfgLearningAutoStart { get; private set; }
        public ConfigEntry<LearningView> CfgLearningView { get; private set; }
        public ConfigEntry<int> CfgPiPX { get; private set; }
        public ConfigEntry<int> CfgPiPY { get; private set; }
        public ConfigEntry<int> CfgPiPWidth { get; private set; }
        public ConfigEntry<int> CfgPiPHeight { get; private set; }
        public ConfigEntry<int> CfgPiPDpi { get; private set; }

        public ConfigurationManager(ConfigFile config)
        {
            _config = config;
            InitializeConfigurations();
        }

        private void InitializeConfigurations()
        {
            CfgLearningAutoStart = _config.Bind("Learning", "AutoStart", true, "房间准备好后自动进入省电学习小窗。");
            CfgLearningView = _config.Bind("Learning", "View", LearningView.Static, "Static 静态背景；Animated 低帧率动画。");
            CfgPiPX = _config.Bind("LearningWindow", "X", int.MinValue, "记忆的小窗位置。");
            CfgPiPY = _config.Bind("LearningWindow", "Y", int.MinValue, "记忆的小窗位置。");
            CfgPiPDpi = _config.Bind("LearningWindow", "Dpi", 96, "保存尺寸时的屏幕 DPI，用于换屏时保持可读大小。");
            CfgPiPWidth = _config.Bind("LearningWindow", "Width", 0, "记忆的客户区宽度；0 使用 480 DIP。");
            CfgPiPHeight = _config.Bind("LearningWindow", "Height", 0, "记忆的客户区高度；0 使用 270 DIP。");
            // 快捷键配置
            KeyPotatoMode = _config.Bind("Hotkeys", "PotatoModeKey", KeyCode.F2, 
                "切换土豆模式的按键");
            
            KeyPiPMode = _config.Bind("Hotkeys", "PiPModeKey", KeyCode.F3, 
                "切换画中画小窗的按键");
            
            KeyCameraMirror = _config.Bind("Hotkeys", "CameraMirrorKey", KeyCode.F4, 
                "切换摄像机镜像的按键(左右翻转画面)");
            
            KeyPortraitMode = _config.Bind("Hotkeys", "PortraitModeKey", KeyCode.F5, 
                "切换竖屏优化的按键(方便调试参数)");

            // 相机配置
            CfgEnableMirror = _config.Bind("Camera", "EnableMirrorOnStart", false, 
                "启动时是否自动启用摄像机镜像(默认关闭,建议先用UE Explorer测试)");
            
            CfgEnablePortraitMode = _config.Bind("Camera", "EnablePortraitMode", false, 
                "启动时是否自动启用竖屏优化(默认关闭,如启用会在游戏初始化后自动激活)");
            
            CfgEnableDeleteConfirm = _config.Bind("General", "EnableDeleteConfirm", true,
                "是否启用笔记/待办删除二次确认(默认开启)");

            // 性能配置
            CfgEnableBackgroundOptimization = _config.Bind("Performance", "EnableBackgroundOptimization", true,
                "游戏窗口不可见时自动省电(最小化/虚拟桌面切换/拖出屏幕/同显示器有全屏程序时触发:限帧10fps+渲染分辨率0.1+关闭阴影)");

            // 窗口配置
            CfgWindowScale = _config.Bind("Window", "ScaleRatio", WindowScaleRatio.OneThird, 
                "在设置页选择此比例可按屏幕工作区调整小窗；首次默认 480×270 DIP。");
            
            CfgDragMode = _config.Bind("Window", "DragMethod", DragMode.Ctrl_LeftClick, 
                "拖动方式");

            CfgAutoHideGuiInPiP = _config.Bind("Window", "AutoHideGuiInPiP", false,
                "进入小窗模式时自动隐藏游戏GUI，退出小窗时自动恢复");

            // 服装配置
            CfgDisableCostumeRotation = _config.Bind("Costume", "DisableRotation", false,
                "屏蔽服装轮换(开启后始终使用默认服装,需重启游戏生效)");
            
            CfgSuggestedCostumeSkin = _config.Bind("Costume", "SuggestedCostumeSkin", "",
                "悄悄话-建议明日服装(填入服装名称如 Polo_1,使用一次后自动清空,需重启游戏生效)");

        }

        /// <summary>
        /// 保存所有配置
        /// </summary>
        public void Save()
        {
            _config.Save();
        }

        /// <summary>
        /// 重新加载配置
        /// </summary>
        public void Reload()
        {
            _config.Reload();
        }
    }
}
