using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 资源管理器，负责加载和管理 AssetBundle 资源和场景对象
    /// 
    /// 资源来源类型：
    /// - GlobalBundle：从游戏全局已加载的 bundle 精确查找
    /// - ExternalBundle：从外部 bundle 文件加载（精确匹配资源名）
    /// - Scene：从游戏场景加载（按路径查找）
    /// 
    /// 所有外部资源（ExternalBundle 和 Scene）都会持久化存储在 AssetPool 下，不随场景切换销毁
    /// </summary>
    internal sealed class AssetManager : MonoBehaviour
    {
        #region 资源类型定义
        /// <summary>
        /// 资源配置
        /// 
        /// 加载方式由配置决定：
        /// - BundleName 为 null：从游戏全局已加载 bundle 精确查找
        /// - BundleName 不为 null 且 ObjectPath 为 null：从指定 bundle 精确匹配资源名
        /// - BundleName 不为 null 且 ObjectPath 不为 null：加载场景 bundle 后按路径查找对象
        /// </summary>
        private class AssetConfig
        {
            /// <summary>
            /// Bundle 文件路径（相对于 aa/平台/ 目录）
            /// - null：从全局已加载 bundle 查找
            /// - 普通 bundle：如 "localpoolprefabs_assets_laceboss.bundle"
            /// - 场景 bundle：如 "scenes_scenes_scenes/slab_10b.bundle"
            /// </summary>
            public string? BundleName { get; set; }

            /// <summary>
            /// 场景内对象路径（可选）
            /// - null：从 bundle 精确匹配资源名
            /// - 非 null：加载场景后按路径查找对象，格式如 "Boss Scene/Pin Projectiles/FW Pin Projectile (3)"
            /// </summary>
            public string? ObjectPath { get; set; }
        }

        /// <summary>
        /// 统一资源配置：必须在此显式声明的资源才能获取
        /// </summary>
        private static readonly Dictionary<string, AssetConfig> _assetConfig = new()
        {
            // ========== 全局 Bundle 资源：从游戏全局已加载 bundle 精确查找 ==========
            { "Reaper Silk Bundle", new AssetConfig { BundleName = null } },
            { "Abyss Bullet", new AssetConfig { BundleName = null } },

            // ========== 外部 Bundle 资源：从指定 bundle 文件精确匹配资源名 ==========
            { "lace_circle_slash", new AssetConfig { BundleName = "localpoolprefabs_assets_laceboss.bundle" } },
            { "First Weaver Anim", new AssetConfig { BundleName = "tk2danimations_assets_areaslab.bundle" } },
            { "First Weaver Cln", new AssetConfig { BundleName = "tk2dcollections_assets_areaslab.bundle" } },
            { "First Weaver Bomb Blast", new AssetConfig { BundleName = "localpoolprefabs_assets_areaslab.bundle" } },
            { "focus_blast_first_weaver", new AssetConfig { BundleName = "animations_assets_areaslab.bundle" } },

            // ========== 场景对象：从场景 bundle 加载后按路径查找 ==========
            { "FW Pin Projectile", new AssetConfig { BundleName = "scenes_scenes_scenes/slab_10b.bundle", ObjectPath = "Boss Scene/Pin Projectiles/FW Pin Projectile (3)" } },
            // { "First Weaver Blast", new AssetConfig { BundleName = "scenes_scenes_scenes/slab_10b.bundle", ObjectPath = "Boss Scene/Blasts/First Weaver Blast" } }
        };

        /// <summary>Bundle 根目录路径</summary>
        private static readonly string BundleRootFolder = Path.Combine(
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
        /// <summary>全局 Bundle 资源缓存（以 (类型, 名称) 为键，需要跟踪类型）</summary>
        private readonly Dictionary<(Type, string), UnityEngine.Object> _globalBundleCache = new();

        /// <summary>外部资源缓存（统一存储 ExternalBundle 和 Scene 类型的资源，全部持久化）</summary>
        private readonly Dictionary<string, UnityEngine.Object> _externalAssetCache = new();

        /// <summary>AssetPool 容器（存放所有外部资源）</summary>
        private GameObject? _assetPool;

        private bool _initialized = false;

        /// <summary>正在加载的资源（防止重复加载）</summary>
        private readonly HashSet<string> _loadingAssets = new();

        /// <summary>外部资源是否已全部预加载</summary>
        private bool _externalAssetsPreloaded = false;

        /// <summary>正在预加载外部资源</summary>
        private bool _preloadingExternalAssets = false;
        #endregion

        #region 公开属性
        /// <summary>外部资源是否已全部预加载完成</summary>
        public bool IsExternalAssetsPreloaded => _externalAssetsPreloaded;
        #endregion

        #region 初始化与生命周期
        private void Awake()
        {
            CreateAssetPool();
            _initialized = true;
            Log.Info("[AssetManager] 已初始化（统一持久化缓存架构）");
        }

        /// <summary>
        /// 预加载所有配置的外部资源
        /// 应在游戏初始化时调用，之后外部 Manager 可直接使用 Get 获取资源
        /// </summary>
        public IEnumerator PreloadAllExternalAssets()
        {
            if (_externalAssetsPreloaded || _preloadingExternalAssets)
            {
                Log.Debug("[AssetManager] 外部资源已预加载或正在预加载，跳过");
                // 等待预加载完成
                while (_preloadingExternalAssets)
                {
                    yield return null;
                }
                yield break;
            }

            _preloadingExternalAssets = true;
            Log.Info("[AssetManager] 开始预加载所有外部资源...");

            // 收集所有需要预加载的外部资源（BundleName 不为 null 的资源）
            var externalAssets = new List<string>();
            foreach (var kvp in _assetConfig)
            {
                if (kvp.Value.BundleName != null)
                {
                    externalAssets.Add(kvp.Key);
                }
            }

            if (externalAssets.Count == 0)
            {
                Log.Info("[AssetManager] 没有需要预加载的外部资源");
                _externalAssetsPreloaded = true;
                _preloadingExternalAssets = false;
                yield break;
            }

            Log.Info($"[AssetManager] 需要预加载 {externalAssets.Count} 个外部资源: {string.Join(", ", externalAssets)}");

            // 批量加载所有外部资源
            yield return LoadAssetsAsync(externalAssets.ToArray());

            // 验证加载结果
            int loadedCount = 0;
            int failedCount = 0;
            foreach (var assetName in externalAssets)
            {
                if (IsAssetLoaded(assetName))
                {
                    loadedCount++;
                }
                else
                {
                    failedCount++;
                    Log.Warn($"[AssetManager] 预加载失败: {assetName}");
                }
            }

            _externalAssetsPreloaded = true;
            _preloadingExternalAssets = false;
            Log.Info($"[AssetManager] 外部资源预加载完成: 成功 {loadedCount}，失败 {failedCount}");
        }

        /// <summary>
        /// 等待外部资源预加载完成
        /// </summary>
        public IEnumerator WaitForPreload()
        {
            while (!_externalAssetsPreloaded)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 创建 AssetPool 容器用于存放所有外部资源
        /// </summary>
        private void CreateAssetPool()
        {
            if (_assetPool == null)
            {
                _assetPool = new GameObject("AssetPool");
                _assetPool.transform.SetParent(transform);
                _assetPool.SetActive(false); // 保持禁用状态，作为资源容器
                Log.Info("[AssetManager] AssetPool 容器已创建");
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 场景切换时清理失效引用（不清理持久化缓存）
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 预加载期间跳过缓存清理（附加场景加载会触发此事件）
            if (_preloadingExternalAssets)
            {
                Log.Debug($"[AssetManager] 场景切换: {scene.name}，预加载中，跳过缓存清理");
                return;
            }

            // 只在主场景切换时清理（非 Additive 模式）
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive)
            {
                Log.Debug($"[AssetManager] 场景切换: {scene.name}（Additive），跳过缓存清理");
                return;
            }

            Log.Debug($"[AssetManager] 场景切换: {scene.name}，检查缓存有效性");
            CleanupInvalidCaches();
        }

        /// <summary>
        /// 清理所有缓存中的失效引用
        /// </summary>
        private void CleanupInvalidCaches()
        {
            // 清理全局 Bundle 缓存中的失效引用
            var globalKeysToRemove = new List<(Type, string)>();
            foreach (var kvp in _globalBundleCache)
            {
                if (kvp.Value == null)
                {
                    globalKeysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in globalKeysToRemove)
            {
                _globalBundleCache.Remove(key);
                Log.Debug($"[AssetManager] 移除失效的全局缓存: {key.Item2}");
            }

            // 清理外部资源缓存中的失效引用
            var externalKeysToRemove = new List<string>();
            foreach (var kvp in _externalAssetCache)
            {
                if (kvp.Value == null)
                {
                    externalKeysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in externalKeysToRemove)
            {
                _externalAssetCache.Remove(key);
                Log.Debug($"[AssetManager] 移除失效的外部资源缓存: {key}");
            }

            int totalRemoved = globalKeysToRemove.Count + externalKeysToRemove.Count;
            if (totalRemoved > 0)
            {
                Log.Info($"[AssetManager] 清理了 {totalRemoved} 个失效的缓存引用");
            }
        }
        #endregion

        #region 资源获取（统一接口）
        /// <summary>
        /// 获取资源（同步）
        /// 必须在 _assetConfig 中显式声明的资源才能获取
        /// 对于外部资源（BundleName 不为 null），如果尚未加载会返回 null
        /// </summary>
        public T? Get<T>(string assetName) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("[AssetManager] 资源名称不能为空");
                return null;
            }

            // 检查资源是否已配置
            if (!_assetConfig.TryGetValue(assetName, out var config))
            {
                Log.Error($"[AssetManager] 未配置的资源: '{assetName}'。请在 AssetManager._assetConfig 中显式声明。");
                return null;
            }

            // 根据配置分发
            if (config.BundleName == null)
            {
                // 全局 Bundle 查找
                return GetFromGlobalBundle<T>(assetName);
            }
            else
            {
                // 从外部缓存获取
                return GetFromExternalCache<T>(assetName);
            }
        }

        /// <summary>
        /// 获取场景对象（同步，从缓存获取）
        /// 这是 Get&lt;GameObject&gt; 的便捷方法
        /// </summary>
        public GameObject? GetSceneObject(string assetName)
        {
            return Get<GameObject>(assetName);
        }

        /// <summary>
        /// 检查资源是否已加载
        /// </summary>
        public bool IsAssetLoaded(string assetName)
        {
            if (!_assetConfig.TryGetValue(assetName, out var config))
            {
                return false;
            }

            if (config.BundleName == null)
            {
                // 全局资源检查缓存
                return _globalBundleCache.Keys.Any(k => k.Item2 == assetName);
            }
            else
            {
                // 外部资源检查缓存
                return _externalAssetCache.TryGetValue(assetName, out var cached) && cached != null;
            }
        }

        /// <summary>
        /// 检查场景对象是否已加载（兼容旧 API）
        /// </summary>
        public bool IsSceneObjectLoaded(string assetName)
        {
            return IsAssetLoaded(assetName);
        }

        /// <summary>
        /// 检查资源是否正在加载
        /// </summary>
        public bool IsAssetLoading(string assetName)
        {
            return _loadingAssets.Contains(assetName);
        }

        /// <summary>
        /// 检查场景对象是否正在加载（兼容旧 API）
        /// </summary>
        public bool IsSceneObjectLoading(string assetName)
        {
            return IsAssetLoading(assetName);
        }
        #endregion

        #region 资源加载（协程方式）
        /// <summary>
        /// 加载外部资源（异步，协程方式）
        /// 适用于 BundleName 不为 null 的资源
        /// 加载后的资源会持久化存储在 AssetPool 下
        /// </summary>
        public IEnumerator LoadAssetAsync(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("[AssetManager] 资源名称不能为空");
                yield break;
            }

            if (!_assetConfig.TryGetValue(assetName, out var config))
            {
                Log.Error($"[AssetManager] 未配置的资源: '{assetName}'。请在 AssetManager._assetConfig 中显式声明。");
                yield break;
            }

            // 全局 Bundle 资源直接同步获取，不需要异步加载
            if (config.BundleName == null)
            {
                Log.Debug($"[AssetManager] '{assetName}' 是全局资源，直接同步获取");
                yield break;
            }

            // 检查是否已加载
            if (_externalAssetCache.TryGetValue(assetName, out var cached) && cached != null)
            {
                Log.Debug($"[AssetManager] 资源 '{assetName}' 已在缓存中");
                yield break;
            }

            // 检查是否正在加载
            if (_loadingAssets.Contains(assetName))
            {
                Log.Debug($"[AssetManager] 资源 '{assetName}' 正在加载中，等待完成...");
                while (_loadingAssets.Contains(assetName))
                {
                    yield return null;
                }
                yield break;
            }

            // 标记为正在加载
            _loadingAssets.Add(assetName);

            // 根据是否有 ObjectPath 分发加载方式
            if (config.ObjectPath == null)
            {
                // 从 bundle 精确匹配资源名
                yield return LoadFromExternalBundle(assetName, config.BundleName);
            }
            else
            {
                // 加载场景后按路径查找对象
                yield return LoadFromSceneBundle(assetName, config.BundleName, config.ObjectPath);
            }

            // 加载完成，移除标记
            _loadingAssets.Remove(assetName);
        }

        /// <summary>
        /// 加载场景对象（兼容旧 API）
        /// </summary>
        public IEnumerator LoadSceneObjectAsync(string assetName)
        {
            yield return LoadAssetAsync(assetName);
        }

        /// <summary>
        /// 批量加载外部资源（异步，协程方式）
        /// 自动按 bundle 分组，减少加载/卸载次数
        /// </summary>
        public IEnumerator LoadAssetsAsync(params string[] assetNames)
        {
            // 按 BundleName 分组
            var groupedByBundle = new Dictionary<string, List<string>>();

            foreach (var assetName in assetNames)
            {
                if (!_assetConfig.TryGetValue(assetName, out var config)) continue;
                if (config.BundleName == null) continue; // 跳过全局资源
                if (IsAssetLoaded(assetName) || IsAssetLoading(assetName)) continue;

                if (!groupedByBundle.ContainsKey(config.BundleName))
                    groupedByBundle[config.BundleName] = new List<string>();
                groupedByBundle[config.BundleName].Add(assetName);
            }

            // 批量加载每个 bundle 的资源
            foreach (var group in groupedByBundle)
            {
                yield return LoadFromBundleBatch(group.Key, group.Value);
            }
        }

        /// <summary>
        /// 批量加载场景对象（兼容旧 API）
        /// </summary>
        public IEnumerator LoadSceneObjectsAsync(params string[] assetNames)
        {
            yield return LoadAssetsAsync(assetNames);
        }
        #endregion

        #region GlobalBundle 资源加载
        /// <summary>
        /// 从全局已加载 Bundle 获取资源（精确匹配）
        /// </summary>
        private T? GetFromGlobalBundle<T>(string assetName) where T : UnityEngine.Object
        {
            var cacheKey = (typeof(T), assetName);

            // 1. 检查缓存
            if (_globalBundleCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached != null)
                {
                    Log.Debug($"[AssetManager] 从全局缓存中找到资源: {assetName}");
                    return cached as T;
                }
                else
                {
                    _globalBundleCache.Remove(cacheKey);
                }
            }

            // 2. 从全局已加载 bundle 查找
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null) continue;

                try
                {
                    var assetPaths = bundle.GetAllAssetNames();
                    foreach (var assetPath in assetPaths)
                    {
                        string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);
                        if (currentAssetName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var asset = bundle.LoadAsset<T>(assetPath);
                            if (asset != null)
                            {
                                _globalBundleCache[cacheKey] = asset;
                                Log.Info($"[AssetManager] 从全局 Bundle '{bundle.name}' 加载资源: {assetName}");
                                return asset;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[AssetManager] 从 Bundle '{bundle.name}' 查找资源时出错: {e.Message}");
                }
            }

            Log.Error($"[AssetManager] 未找到全局资源: '{assetName}' ({typeof(T).Name})");
            return null;
        }
        #endregion

        #region 外部资源加载
        /// <summary>
        /// 从外部缓存获取资源
        /// </summary>
        private T? GetFromExternalCache<T>(string assetName) where T : UnityEngine.Object
        {
            if (_externalAssetCache.TryGetValue(assetName, out var cached) && cached != null)
            {
                if (cached is T typedAsset)
                {
                    return typedAsset;
                }
                Log.Warn($"[AssetManager] 资源 '{assetName}' 类型不匹配: 期望 {typeof(T).Name}，实际 {cached.GetType().Name}");
                return null;
            }

            Log.Warn($"[AssetManager] 资源 '{assetName}' 尚未加载，请先调用 LoadAssetAsync");
            return null;
        }

        /// <summary>
        /// 从外部 Bundle 加载单个资源（精确匹配资源名）
        /// </summary>
        private IEnumerator LoadFromExternalBundle(string assetName, string bundleName)
        {
            Log.Info($"[AssetManager] 从 Bundle '{bundleName}' 加载资源: {assetName}");

            string bundlePath = Path.Combine(BundleRootFolder, bundleName);
            if (!File.Exists(bundlePath))
            {
                Log.Error($"[AssetManager] Bundle 文件不存在: {bundlePath}");
                yield break;
            }

            AssetBundle? bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Log.Error($"[AssetManager] 无法加载 Bundle: {bundlePath}");
                yield break;
            }

            try
            {
                var assetPaths = bundle.GetAllAssetNames();
                foreach (var assetPath in assetPaths)
                {
                    string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);
                    if (currentAssetName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        var asset = bundle.LoadAsset<UnityEngine.Object>(assetPath);
                        if (asset != null)
                        {
                            // 如果是 GameObject，放到 AssetPool 下
                            if (asset is GameObject go && _assetPool != null)
                            {
                                // 先禁用原始 prefab，避免实例化时触发 OnEnable
                                bool wasActive = go.activeSelf;
                                go.SetActive(false);
                                
                                var copy = Instantiate(go);
                                copy.name = assetName + " (Prefab)";
                                
                                // 恢复原始 prefab 状态
                                go.SetActive(wasActive);
                                
                                // 禁用/销毁可能导致自毁的组件
                                DisableAutoDestructComponents(copy);
                                
                                // copy 已经是禁用状态，直接设置父对象
                                copy.transform.SetParent(_assetPool.transform);
                                _externalAssetCache[assetName] = copy;
                            }
                            else
                            {
                                _externalAssetCache[assetName] = asset;
                            }
                            Log.Info($"[AssetManager] 资源 '{assetName}' 加载成功并已持久化");
                            break;
                        }
                    }
                }
            }
            finally
            {
                bundle.Unload(false);
            }

            yield return null;
        }

        /// <summary>
        /// 从场景 Bundle 加载单个对象（按路径查找）
        /// </summary>
        private IEnumerator LoadFromSceneBundle(string assetName, string bundleName, string objectPath)
        {
            Log.Info($"[AssetManager] 从场景 Bundle '{bundleName}' 加载对象: {assetName} (路径: {objectPath})");

            string bundlePath = Path.Combine(BundleRootFolder, bundleName);
            if (!File.Exists(bundlePath))
            {
                Log.Error($"[AssetManager] 场景 Bundle 文件不存在: {bundlePath}");
                yield break;
            }

            AssetBundle? bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Log.Error($"[AssetManager] 无法加载场景 Bundle: {bundlePath}");
                yield break;
            }

            // 从 bundle 路径提取场景名（如 "scenes_scenes_scenes/slab_10b.bundle" -> "slab_10b"）
            string sceneName = Path.GetFileNameWithoutExtension(bundleName);

            // 获取 bundle 中的所有场景路径，使用第一个场景路径
            string[] scenePaths = bundle.GetAllScenePaths();
            string sceneToLoad = sceneName;
            if (scenePaths.Length > 0)
            {
                sceneToLoad = scenePaths[0];
            }

            var tempCamera = CreateTempCamera();

            var loadOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
            yield return loadOp;

            // 使用场景路径获取场景
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(sceneToLoad);
            if (!scene.IsValid())
            {
                // 尝试用名称获取
                scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(sceneToLoad));
            }

            if (!scene.isLoaded)
            {
                Log.Error($"[AssetManager] 场景加载失败: {sceneToLoad}");
                bundle.Unload(true);
                if (tempCamera != null) Destroy(tempCamera);
                yield break;
            }

            DisableProblematicComponents(scene);

            var sourceObject = FindObjectInScene(scene, objectPath);
            if (sourceObject != null)
            {
                var copy = Instantiate(sourceObject);
                copy.name = assetName + " (Prefab)";
                copy.SetActive(false);

                if (_assetPool != null)
                {
                    copy.transform.SetParent(_assetPool.transform);
                }

                _externalAssetCache[assetName] = copy;
                Log.Info($"[AssetManager] 场景对象 '{assetName}' 加载成功并已持久化");
            }
            else
            {
                Log.Error($"[AssetManager] 在场景 '{sceneName}' 中未找到对象 '{objectPath}'");
            }

            var unloadOp = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene.name);
            yield return unloadOp;

            bundle.Unload(false);
            if (tempCamera != null) Destroy(tempCamera);
        }

        /// <summary>
        /// 从同一 Bundle 批量加载资源
        /// 自动区分精确匹配和场景路径查找
        /// </summary>
        private IEnumerator LoadFromBundleBatch(string bundleName, List<string> assetNames)
        {
            // 标记为正在加载
            foreach (var name in assetNames)
            {
                _loadingAssets.Add(name);
            }

            // 分类：精确匹配 vs 场景路径查找
            var exactMatchAssets = new List<string>();
            var scenePathAssets = new List<string>();

            foreach (var assetName in assetNames)
            {
                var config = _assetConfig[assetName];
                if (config.ObjectPath == null)
                {
                    exactMatchAssets.Add(assetName);
                }
                else
                {
                    scenePathAssets.Add(assetName);
                }
            }

            string bundlePath = Path.Combine(BundleRootFolder, bundleName);
            if (!File.Exists(bundlePath))
            {
                Log.Error($"[AssetManager] Bundle 文件不存在: {bundlePath}");
                foreach (var name in assetNames) _loadingAssets.Remove(name);
                yield break;
            }

            AssetBundle? bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Log.Error($"[AssetManager] 无法加载 Bundle: {bundlePath}");
                foreach (var name in assetNames) _loadingAssets.Remove(name);
                yield break;
            }

            // 处理精确匹配的资源
            if (exactMatchAssets.Count > 0)
            {
                Log.Info($"[AssetManager] 从 Bundle '{bundleName}' 批量加载 {exactMatchAssets.Count} 个精确匹配资源...");

                var bundleAssetPaths = bundle.GetAllAssetNames();
                foreach (var assetName in exactMatchAssets)
                {
                    foreach (var assetPath in bundleAssetPaths)
                    {
                        string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);
                        if (currentAssetName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var asset = bundle.LoadAsset<UnityEngine.Object>(assetPath);
                            if (asset != null)
                            {
                                if (asset is GameObject go && _assetPool != null)
                                {
                                    // 先禁用原始 prefab，避免实例化时触发 OnEnable
                                    bool wasActive = go.activeSelf;
                                    go.SetActive(false);
                                    
                                    var copy = Instantiate(go);
                                    copy.name = assetName + " (Prefab)";
                                    
                                    // 恢复原始 prefab 状态
                                    go.SetActive(wasActive);
                                    
                                    // 禁用/销毁可能导致自毁的组件
                                    DisableAutoDestructComponents(copy);
                                    
                                    // copy 已经是禁用状态，直接设置父对象
                                    copy.transform.SetParent(_assetPool.transform);
                                    _externalAssetCache[assetName] = copy;
                                }
                                else
                                {
                                    _externalAssetCache[assetName] = asset;
                                }
                                Log.Info($"[AssetManager] 资源 '{assetName}' 加载成功");
                            }
                            break;
                        }
                    }
                }
            }

            // 处理场景路径查找的资源
            if (scenePathAssets.Count > 0)
            {
                Log.Info($"[AssetManager] 从场景 Bundle '{bundleName}' 批量加载 {scenePathAssets.Count} 个场景对象...");

                // 从 bundle 路径提取场景名
                string sceneName = Path.GetFileNameWithoutExtension(bundleName);

                // 获取 bundle 中的所有场景路径，使用第一个场景路径
                string[] scenePaths = bundle.GetAllScenePaths();
                string sceneToLoad = sceneName;
                if (scenePaths.Length > 0)
                {
                    sceneToLoad = scenePaths[0];
                }

                var tempCamera = CreateTempCamera();

                var loadOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
                yield return loadOp;

                // 使用场景路径获取场景
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(sceneToLoad);
                if (!scene.IsValid())
                {
                    // 尝试用名称获取
                    scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(sceneToLoad));
                }

                if (!scene.isLoaded)
                {
                    Log.Error($"[AssetManager] 场景加载失败: {sceneToLoad}");
                    if (tempCamera != null) Destroy(tempCamera);
                }
                else
                {
                    DisableProblematicComponents(scene);

                    foreach (var assetName in scenePathAssets)
                    {
                        var config = _assetConfig[assetName];
                        var sourceObject = FindObjectInScene(scene, config.ObjectPath!);

                        if (sourceObject == null)
                        {
                            Log.Error($"[AssetManager] 在场景 '{sceneName}' 中未找到对象 '{config.ObjectPath}'");
                            continue;
                        }

                        var copy = Instantiate(sourceObject);
                        copy.name = assetName + " (Prefab)";
                        copy.SetActive(false);

                        if (_assetPool != null)
                        {
                            copy.transform.SetParent(_assetPool.transform);
                        }

                        _externalAssetCache[assetName] = copy;
                        Log.Info($"[AssetManager] 场景对象 '{assetName}' 加载成功");
                    }

                    var unloadOp = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene.name);
                    yield return unloadOp;

                    if (tempCamera != null) Destroy(tempCamera);
                }
            }

            bundle.Unload(false);

            // 移除加载标记
            foreach (var name in assetNames)
            {
                _loadingAssets.Remove(name);
            }

            Log.Info($"[AssetManager] Bundle '{bundleName}' 批量加载完成");
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 创建临时相机，避免附加场景中的渲染组件找不到 Camera.main
        /// </summary>
        private GameObject CreateTempCamera()
        {
            var go = new GameObject("AssetManager_TempCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                tag = "MainCamera"
            };
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            return go;
        }

        /// <summary>
        /// 静默禁用附加场景中可能导致报错的组件
        /// </summary>
        private void DisableProblematicComponents(Scene scene)
        {
            var behaviours = scene.GetRootGameObjects()
                .SelectMany(o => o.GetComponentsInChildren<Behaviour>(true))
                .Where(b => b != null)
                .ToArray();

            foreach (var b in behaviours)
            {
                string typeName = b.GetType().Name;
                if (!b.enabled) continue;

                if (typeName == "TintRendererGroup" || typeName == "CustomSceneManager")
                {
                    b.enabled = false;
                }
            }
        }

        /// <summary>
        /// 禁用可能导致对象自动销毁/回收的组件
        /// 这些组件可能在 Awake/Start 时触发自毁逻辑
        /// </summary>
        private void DisableAutoDestructComponents(GameObject obj)
        {
            if (obj == null) return;

            // 禁用的组件类型名称列表
            var autoDestructTypes = new HashSet<string>
            {
                "AutoRecycleSelf",      // 自动回收
                "ActiveRecycler",       // 活跃回收器
                "ObjectBounce",         // 可能触发回收
                "DropRecycle",          // 掉落回收
                "RecycleResetHandler",  // 回收重置处理器
                "EventRegister",        // 事件注册（可能注册 CLEAR EFFECTS 等）
            };

            // 获取所有组件（包括子对象）
            var allComponents = obj.GetComponentsInChildren<Component>(true);
            int disabledCount = 0;

            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                
                string typeName = comp.GetType().Name;
                
                if (autoDestructTypes.Contains(typeName))
                {
                    if (comp is Behaviour behaviour)
                    {
                        behaviour.enabled = false;
                        disabledCount++;
                    }
                    else
                    {
                        // 对于非 Behaviour 组件，直接销毁
                        Destroy(comp);
                        disabledCount++;
                    }
                }
            }

            if (disabledCount > 0)
            {
                Log.Debug($"[AssetManager] 禁用了 {obj.name} 的 {disabledCount} 个自毁相关组件");
            }
        }
        #endregion

        #region 缓存管理与卸载
        /// <summary>
        /// 清空指定资源的缓存
        /// </summary>
        public void ClearAssetCache(string assetName)
        {
            // 尝试从全局缓存移除
            var globalKeysToRemove = _globalBundleCache.Keys.Where(k => k.Item2 == assetName).ToList();
            foreach (var key in globalKeysToRemove)
            {
                _globalBundleCache.Remove(key);
            }

            // 尝试从外部缓存移除
            if (_externalAssetCache.TryGetValue(assetName, out var obj))
            {
                if (obj != null && obj is GameObject go)
                {
                    Destroy(go);
                }
                _externalAssetCache.Remove(assetName);
            }

            Log.Info($"[AssetManager] 已清空资源缓存: {assetName}");
        }

        /// <summary>
        /// 清空场景对象缓存（兼容旧 API）
        /// </summary>
        public void ClearSceneObjectCache(string assetName)
        {
            ClearAssetCache(assetName);
        }

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public void ClearAllCache()
        {
            _globalBundleCache.Clear();

            // 销毁外部资源缓存中的 GameObject
            foreach (var kvp in _externalAssetCache)
            {
                if (kvp.Value != null && kvp.Value is GameObject go)
                {
                    Destroy(go);
                }
            }
            _externalAssetCache.Clear();
            _loadingAssets.Clear();

            Log.Info("[AssetManager] 已清空所有资源缓存");
        }

        /// <summary>
        /// 卸载所有资源
        /// </summary>
        public void UnloadAll()
        {
            Log.Info("[AssetManager] 开始卸载所有资源...");
            ClearAllCache();

            // 销毁 AssetPool
            if (_assetPool != null)
            {
                Destroy(_assetPool);
                _assetPool = null;
            }

            _initialized = false;
            Log.Info("[AssetManager] 所有资源已卸载");
        }

        private void OnDestroy()
        {
            UnloadAll();
        }
        #endregion

        #region 场景查找工具
        /// <summary>
        /// 在场景中按路径查找对象
        /// </summary>
        private GameObject? FindObjectInScene(Scene scene, string objectPath)
        {
            if (!scene.IsValid())
            {
                Log.Error("[AssetManager] 无效的场景");
                return null;
            }

            string[] pathParts = objectPath.Split('/');
            if (pathParts.Length == 0)
            {
                Log.Error($"[AssetManager] 无效的对象路径: {objectPath}");
                return null;
            }

            // 查找根对象
            GameObject? current = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == pathParts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null)
            {
                Log.Error($"[AssetManager] 未找到根对象: {pathParts[0]}");
                return null;
            }

            // 遍历子对象路径
            for (int i = 1; i < pathParts.Length; i++)
            {
                var childTransform = current.transform.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == pathParts[i]);

                if (childTransform == null)
                {
                    Log.Error($"[AssetManager] 未找到子对象: {pathParts[i]} (路径: {objectPath})");
                    return null;
                }
                current = childTransform.gameObject;
            }

            Log.Debug($"[AssetManager] 找到对象: {current.name}");
            return current;
        }

        /// <summary>
        /// 在当前场景中查找对象
        /// </summary>
        public static GameObject? FindObjectInCurrentScene(string objectPath)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            string[] pathParts = objectPath.Split('/');
            if (pathParts.Length == 0) return null;

            GameObject? current = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == pathParts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null) return null;

            for (int i = 1; i < pathParts.Length; i++)
            {
                var childTransform = current.transform.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == pathParts[i]);
                if (childTransform == null) return null;
                current = childTransform.gameObject;
            }

            return current;
        }

        /// <summary>
        /// 在 GameObject 中查找子对象
        /// </summary>
        public static GameObject? FindChildObject(GameObject parent, string childPath)
        {
            if (parent == null) return null;

            string[] pathParts = childPath.Split('/');
            GameObject current = parent;

            foreach (var partName in pathParts)
            {
                var childTransform = current.transform.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == partName);
                if (childTransform == null) return null;
                current = childTransform.gameObject;
            }

            return current;
        }
        #endregion

       }
}