using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 资源管理器，负责加载和管理 AssetBundle 资源
    /// 支持两种资源类型：
    /// - Persistent：持久化资源（游戏内置 bundle，可缓存）
    /// - Transient：临时资源（外部 bundle，每次重新加载）
    /// </summary>
    internal sealed class AssetManager : MonoBehaviour
    {
        #region 资源类型定义
        /// <summary>
        /// 资源类型
        /// </summary>
        private enum AssetType
        {
            /// <summary>持久化资源（游戏内置 bundle，可缓存）</summary>
            Persistent,
            /// <summary>临时资源（外部 bundle，每次重新加载）</summary>
            Transient
        }

        /// <summary>
        /// 资源配置：必须在此显式声明的资源才能获取
        /// </summary>
        private static readonly Dictionary<string, (AssetType type, string? bundleName)> _assetConfig = new()
        {
            // 持久化资源（游戏全局已加载的 bundle）
            { "Reaper Silk Bundle", (AssetType.Persistent, null) },
            { "Abyss Bullet", (AssetType.Persistent, null) },
            
            // 临时资源（需要从文件加载的外部 bundle）
            { "lace_circle_slash", (AssetType.Transient, "localpoolprefabs_assets_laceboss.bundle") }
        };

        private static readonly string SceneFolder = Path.Combine(
            Application.streamingAssetsPath,
            "aa",
            Application.platform switch
            {
                RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
                RuntimePlatform.OSXPlayer => "StandaloneOSX",
                RuntimePlatform.LinuxPlayer => "StandaloneLinux64",
                _ => ""
            }
        );
        #endregion

        #region 缓存
        /// <summary>持久化资源缓存（以 (类型, 名称) 为键）</summary>
        private readonly Dictionary<(Type, string), UnityEngine.Object> _persistentAssets = new();

        /// <summary>场景级临时资源缓存（场景切换时清空）</summary>
        private readonly Dictionary<string, UnityEngine.Object> _sceneTransientCache = new();

        private bool _initialized = false;
        #endregion

        #region 初始化与生命周期
        private void Awake()
        {
            _initialized = true;
            Log.Info("AssetManager 已初始化（采用按需加载策略）");
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 场景加载时清理缓存
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Log.Info($"场景切换: {scene.name}，清理缓存");

            // 清空场景级临时资源缓存
            _sceneTransientCache.Clear();

            // 清理持久化资源中的失效引用
            CleanupInvalidPersistentAssets();
        }

        /// <summary>
        /// 清理持久化资源缓存中的失效引用
        /// </summary>
        private void CleanupInvalidPersistentAssets()
        {
            var keysToRemove = new List<(Type, string)>();

            foreach (var kvp in _persistentAssets)
            {
                if (kvp.Value == null)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _persistentAssets.Remove(key);
                Log.Debug($"移除失效的持久化资源缓存: {key.Item2}");
            }

            if (keysToRemove.Count > 0)
            {
                Log.Info($"清理了 {keysToRemove.Count} 个失效的持久化资源缓存");
            }
        }
        #endregion

        #region 资源获取（新架构）
        /// <summary>
        /// 获取资源（同步）
        /// 必须在 _assetConfig 中显式声明的资源才能获取
        /// </summary>
        public T? Get<T>(string assetName) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("资源名称不能为空");
                return null;
            }

            // 检查资源是否已配置
            if (!_assetConfig.TryGetValue(assetName, out var config))
            {
                Log.Error($"未配置的资源: '{assetName}'。请在 AssetManager._assetConfig 中显式声明该资源。");
                return null;
            }

            // 根据资源类型分发
            return config.type switch
            {
                AssetType.Persistent => GetPersistentAsset<T>(assetName),
                AssetType.Transient => GetTransientAsset<T>(assetName, config.bundleName!),
                _ => null
            };
        }

        /// <summary>
        /// 获取资源（异步）
        /// 必须在 _assetConfig 中显式声明的资源才能获取
        /// </summary>
        public async Task<T?> GetAsync<T>(string assetName) where T : UnityEngine.Object
        {
            // 对于当前实现，异步和同步逻辑相同
            // 保留此方法以兼容现有调用
            await Task.Yield();
            return Get<T>(assetName);
        }
        #endregion

        #region 持久化资源加载
        /// <summary>
        /// 获取持久化资源（游戏内置 bundle 的资源，可缓存）
        /// </summary>
        private T? GetPersistentAsset<T>(string assetName) where T : UnityEngine.Object
        {
            var cacheKey = (typeof(T), assetName);

            // 1. 检查持久缓存
            if (_persistentAssets.TryGetValue(cacheKey, out var cached))
            {
                if (cached != null)
                {
                    Log.Debug($"从持久化缓存中找到资源: {assetName}");
                    return cached as T;
                }
                else
                {
                    // 移除失效引用
                    _persistentAssets.Remove(cacheKey);
                    Log.Debug($"持久化缓存中的资源 {assetName} 已失效，已移除");
                }
            }

            // 2. 从全局已加载 bundle 查找
            var asset = GetFromGlobalBundles<T>(assetName);
            if (asset != null)
            {
                _persistentAssets[cacheKey] = asset;
                Log.Info($"从全局 bundle 加载持久化资源: {assetName}");
                return asset;
            }

            Log.Error($"未找到持久化资源: '{assetName}' ({typeof(T).Name})");
            return null;
        }

        /// <summary>
        /// 从全局已加载 Bundle 中查找资源（精确匹配）
        /// </summary>
        private T? GetFromGlobalBundles<T>(string assetName) where T : UnityEngine.Object
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null) continue;

                try
                {
                    var assetPaths = bundle.GetAllAssetNames();
                    foreach (var assetPath in assetPaths)
                    {
                        string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);

                        // 精确匹配
                        if (currentAssetName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var asset = bundle.LoadAsset<T>(assetPath);
                            if (asset != null)
                            {
                                Log.Debug($"从全局 Bundle '{bundle.name}' 中找到资源: {assetName}");
                                return asset;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"从 Bundle '{bundle.name}' 查找资源时出错: {e.Message}");
                }
            }

            return null;
        }
        #endregion

        #region 临时资源加载
        /// <summary>
        /// 获取临时资源（每次从外部 bundle 重新加载）
        /// 在同一场景内会缓存，场景切换时清空
        /// </summary>
        private T? GetTransientAsset<T>(string assetName, string bundleName) where T : UnityEngine.Object
        {
            // 1. 检查场景级缓存
            if (_sceneTransientCache.TryGetValue(assetName, out var cached))
            {
                if (cached != null && cached is T typedCached)
                {
                    Log.Debug($"从场景级缓存中找到临时资源: {assetName}");
                    return typedCached;
                }
                else
                {
                    // 移除失效引用
                    _sceneTransientCache.Remove(assetName);
                }
            }

            // 2. 从文件加载
            var asset = LoadTransientAssetFromFile<T>(assetName, bundleName);
            if (asset != null)
            {
                // 缓存到场景级缓存
                _sceneTransientCache[assetName] = asset;
                Log.Info($"从文件加载临时资源: {assetName}");
            }

            return asset;
        }

        /// <summary>
        /// 从文件加载临时资源（加载 bundle -> 提取资源 -> 立即卸载 bundle）
        /// </summary>
        private T? LoadTransientAssetFromFile<T>(string assetName, string bundleName) where T : UnityEngine.Object
        {
            string bundlePath = Path.Combine(SceneFolder, bundleName);

            if (!File.Exists(bundlePath))
            {
                Log.Error($"Bundle 文件不存在: {bundlePath}");
                return null;
            }

            // 加载 bundle
            AssetBundle? bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Log.Error($"无法加载 bundle: {bundlePath}");
                return null;
            }

            Log.Debug($"成功加载 bundle: {bundle.name}");

            // 查找资源
            T? asset = null;
            try
            {
                var assetPaths = bundle.GetAllAssetNames();
                foreach (var assetPath in assetPaths)
                {
                    string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);

                    // 精确匹配
                    if (currentAssetName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        asset = bundle.LoadAsset<T>(assetPath);
                        if (asset != null)
                        {
                            Log.Debug($"从 bundle '{bundle.name}' 中找到资源: {assetName}");
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"从 bundle 加载资源时出错: {e.Message}");
            }
            finally
            {
                // 立即卸载 bundle（保留已加载的资源）
                bundle.Unload(false);
                Log.Debug($"Bundle 已卸载（资源保留）: {bundleName}");
            }

            if (asset == null)
            {
                Log.Error($"在 bundle '{bundleName}' 中未找到资源: {assetName}");
            }

            return asset;
        }
        #endregion

        #region 缓存管理与卸载
        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public void ClearCache()
        {
            _persistentAssets.Clear();
            _sceneTransientCache.Clear();
            Log.Info("已清空所有资源缓存");
        }

        /// <summary>
        /// 卸载所有资源（场景切换时调用）
        /// </summary>
        public void UnloadAll()
        {
            Log.Info("开始卸载所有资源...");
            ClearCache();
            _initialized = false;
            Log.Info("所有资源已卸载");
        }

        private void OnDestroy()
        {
            UnloadAll();
        }
        #endregion

        #region 调试方法
        /// <summary>
        /// 列出所有缓存的资源
        /// </summary>
        public void LogCachedAssets()
        {
            Log.Info("=== 缓存的资源 ===");

            Log.Info($"持久化资源缓存 ({_persistentAssets.Count}):");
            foreach (var kvp in _persistentAssets)
            {
                var (type, name) = kvp.Key;
                Log.Info($"  - {name} ({type.Name})");
            }

            Log.Info($"场景级临时资源缓存 ({_sceneTransientCache.Count}):");
            foreach (var kvp in _sceneTransientCache)
            {
                Log.Info($"  - {kvp.Key} ({kvp.Value?.GetType().Name ?? "null"})");
            }
        }

        /// <summary>
        /// 列出所有配置的资源
        /// </summary>
        public void LogConfiguredAssets()
        {
            Log.Info("=== 配置的资源 ===");
            foreach (var kvp in _assetConfig)
            {
                var (type, bundleName) = kvp.Value;
                Log.Info($"  - {kvp.Key}: {type} (bundle: {bundleName ?? "全局"})");
            }
        }

        /// <summary>
        /// 列出指定 Bundle 的所有资源
        /// </summary>
        public void LogBundleContents(string bundleName)
        {
            Log.Info($"=== 查找 Bundle: {bundleName} ===");

            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null) continue;

                if (bundle.name.Contains(bundleName, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"找到 Bundle: {bundle.name}");
                    var assetPaths = bundle.GetAllAssetNames();
                    Log.Info($"  包含 {assetPaths.Length} 个资源:");

                    foreach (var assetPath in assetPaths)
                    {
                        string assetName = Path.GetFileNameWithoutExtension(assetPath);
                        Log.Info($"    - {assetName} (路径: {assetPath})");
                    }
                    return;
                }
            }

            Log.Warn($"未找到包含 '{bundleName}' 的 Bundle");
        }
        #endregion
    }
}