using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Handlers;

/// <summary>
/// 音频替换处理器
/// 从 DLL 所在目录的 Audio 文件夹加载并替换游戏内音频
/// </summary>
[HarmonyPatch]
public static class AudioHandler
{
    /// <summary>
    /// 已加载的替换音频缓存
    /// Key: 原始音频名称, Value: 替换后的 AudioClip
    /// </summary>
    private static readonly Dictionary<string, AudioClip> LoadedClips = new();

    /// <summary>
    /// Audio 文件夹路径（DLL 所在目录下的 Audio 文件夹）
    /// </summary>
    public static string AudioFolder => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
        "Audio"
    );

    /// <summary>
    /// 应用 Harmony 补丁
    /// </summary>
    public static void ApplyPatches(Harmony harmony)
    {
        // Patch AudioSource.PlayHelper
        harmony.Patch(
            AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayHelper),
                new[] { typeof(AudioSource), typeof(ulong) }),
            prefix: new HarmonyMethod(typeof(AudioHandler), nameof(PlayHelperPatch))
        );

        // Patch AudioSource.PlayOneShotHelper
        harmony.Patch(
            AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShotHelper),
                new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) }),
            prefix: new HarmonyMethod(typeof(AudioHandler), nameof(PlayOneShotHelperPatch))
        );

        // Patch AudioSource.clip setter
        harmony.Patch(
            AccessTools.PropertySetter(typeof(AudioSource), nameof(AudioSource.clip)),
            postfix: new HarmonyMethod(typeof(AudioHandler), nameof(ClipSetterPatch))
        );

        Log.Info("[AudioHandler] Harmony 补丁已应用");
    }

    /// <summary>
    /// 初始化：确保 Audio 文件夹存在
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // 确保 Audio 目录存在
            if (!Directory.Exists(AudioFolder))
            {
                Directory.CreateDirectory(AudioFolder);
                Log.Info($"[AudioHandler] 创建 Audio 文件夹: {AudioFolder}");
            }
            else
            {
                // 列出已有的音频文件
                var audioFiles = Directory.GetFiles(AudioFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsAudioFile(f))
                    .ToList();

                Log.Info($"[AudioHandler] Audio 文件夹: {AudioFolder}");
                Log.Info($"[AudioHandler] 发现 {audioFiles.Count} 个音频文件:");
                foreach (var file in audioFiles)
                {
                    Log.Info($"  - {Path.GetFileName(file)}");
                }
            }

            Log.Info("[AudioHandler] 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error($"[AudioHandler] 初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查是否是支持的音频文件
    /// </summary>
    private static bool IsAudioFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".flac" || ext == ".wav" || ext == ".mp3" || ext == ".ogg";
    }

    /// <summary>
    /// PlayHelper 前缀补丁
    /// </summary>
    public static void PlayHelperPatch(AudioSource source, ulong delay)
    {
        if (source?.clip != null)
        {
            TryReplaceClip(source);
        }
    }

    /// <summary>
    /// PlayOneShotHelper 前缀补丁
    /// </summary>
    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            TryReplaceClip(ref clip);
        }
    }

    /// <summary>
    /// clip setter 后缀补丁
    /// </summary>
    public static void ClipSetterPatch(AudioSource __instance, AudioClip value)
    {
        if (value != null && !value.name.StartsWith("ANYSILKBOSS_"))
        {
            TryReplaceClip(__instance);
        }
    }

    /// <summary>
    /// 尝试替换 AudioSource 的 clip
    /// </summary>
    private static void TryReplaceClip(AudioSource source)
    {
        if (source?.clip == null) return;

        string clipName = source.clip.name.Replace("ANYSILKBOSS_", "");

        // 先检查缓存
        if (LoadedClips.TryGetValue(clipName, out var cached))
        {
            source.clip = cached;
            return;
        }

        // 尝试从文件加载
        var loadedClip = LoadAudioClip(clipName);
        if (loadedClip != null)
        {
            LoadedClips[clipName] = loadedClip;
            source.clip = loadedClip;
        }
    }

    /// <summary>
    /// 尝试替换 AudioClip 引用
    /// </summary>
    private static void TryReplaceClip(ref AudioClip clip)
    {
        if (clip == null) return;

        string clipName = clip.name.Replace("ANYSILKBOSS_", "");

        // 先检查缓存
        if (LoadedClips.TryGetValue(clipName, out var cached))
        {
            clip = cached;
            return;
        }

        // 尝试从文件加载
        var loadedClip = LoadAudioClip(clipName);
        if (loadedClip != null)
        {
            LoadedClips[clipName] = loadedClip;
            clip = loadedClip;
        }
    }

    /// <summary>
    /// 根据音频名称从 Audio 文件夹加载音频
    /// </summary>
    private static AudioClip LoadAudioClip(string clipName)
    {
        string filePath = GetAudioFilePath(clipName);
        if (string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            string url = "file:///" + Uri.EscapeUriString(filePath.Replace("\\", "/"));
            var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN);
            var operation = request.SendWebRequest();

            // 同步等待
            while (!operation.isDone) { }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.Error($"[AudioHandler] 加载音频失败 {filePath}: {request.error}");
                return null;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            clip.name = "ANYSILKBOSS_" + clipName;
            Log.Info($"[AudioHandler] 已加载并替换音频: {clipName}");
            return clip;
        }
        catch (Exception ex)
        {
            Log.Error($"[AudioHandler] 加载音频异常 {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 查找匹配的音频文件路径
    /// </summary>
    private static string GetAudioFilePath(string clipName)
    {
        if (!Directory.Exists(AudioFolder))
            return null;

        // 在 Audio 文件夹中查找同名文件（支持任意扩展名）
        var files = Directory.GetFiles(AudioFolder, $"{clipName}.*", SearchOption.AllDirectories);
        if (files.Any())
            return files.First();

        return null;
    }
}
