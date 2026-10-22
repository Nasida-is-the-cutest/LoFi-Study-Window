using System;
using UnityEngine;
using PotatoOptimization.Core;

namespace PotatoOptimization.Features
{
    /// <summary>
    /// 音频管理器 - 负责音频声道交换等功能
    /// </summary>
    public class AudioManager
    {
        private AudioChannelSwapper _swapper;

        /// <summary>
        /// 启用音频声道交换 (用于镜像模式)
        /// </summary>
        public void EnableChannelSwap()
        {
            try
            {
                // 查找 AudioListener
                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                if (listener == null)
                {
                    PotatoPlugin.Log.LogWarning("未找到 AudioListener，跳过音频镜像");
                    return;
                }

                // 检查是否已存在组件
                _swapper = listener.gameObject.GetComponent<AudioChannelSwapper>();
                if (_swapper == null)
                {
                    _swapper = listener.gameObject.AddComponent<AudioChannelSwapper>();
                    PotatoPlugin.Log.LogInfo("已创建音频声道交换组件");
                }
                else if (_swapper.enabled)
                {
                    PotatoPlugin.Log.LogWarning("音频声道交换组件已启用，跳过重复添加");
                    return;
                }

                _swapper.enabled = true;
                PotatoPlugin.Log.LogInfo("已启用音频声道交换 (左右互换)");
            }
            catch (Exception e)
            {
                PotatoPlugin.Log.LogError($"启用音频镜像失败: {e.Message}");
            }
        }

        /// <summary>
        /// 禁用音频声道交换
        /// </summary>
        public void DisableChannelSwap()
        {
            if (_swapper != null)
            {
                // 关键修复：销毁组件而非仅禁用，避免OnAudioFilterRead继续被调用导致爆音
                UnityEngine.Object.Destroy(_swapper);
                _swapper = null;
                PotatoPlugin.Log.LogInfo("已销毁音频声道交换组件");
            }
        }
    }

    /// <summary>
    /// 音频滤镜：交换左右声道，用于镜像模式
    /// </summary>
    public class AudioChannelSwapper : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels)
        {
            // 防御性检查：确保数据有效
            if (data == null || data.Length == 0)
                return;

            // 仅处理立体声 (2 声道)
            if (channels != 2)
                return;

            // 验证数据长度是否匹配声道数
            if (data.Length % 2 != 0)
            {
                PotatoPlugin.Log.LogWarning($"音频数据长度异常: {data.Length} (应为偶数)");
                return;
            }

            // 交换左右声道
            // data 格式: [L0, R0, L1, R1, L2, R2, ...]
            for (int i = 0; i < data.Length; i += 2)
            {
                float temp = data[i];       // 保存左声道
                data[i] = data[i + 1];      // 左 = 右
                data[i + 1] = temp;         // 右 = 左
            }
        }
    }
}
