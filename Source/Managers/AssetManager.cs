using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using AnySilkBoss.Source.Tools;
namespace AnySilkBoss.Source.Managers;

/// <summary>
/// Manages all loaded assets in the mod using singleton pattern.
/// </summary>
internal sealed class AssetManager : MonoBehaviour
{
    #region Singleton Implementation
    private static AssetManager _instance;
    private static readonly object _lockObject = new object();
    private static bool _applicationIsQuitting = false;

    /// <summary>
    /// Gets the singleton instance of AssetManager.
    /// </summary>
    public static AssetManager Instance
    {
        get
        {
            if (_applicationIsQuitting)
            {
                Log.Warn("AssetManager instance is already destroyed. Returning null.");
                return null;
            }

            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<AssetManager>();

                    if (_instance == null)
                    {
                        GameObject singletonObject = new GameObject("AssetManager");
                        _instance = singletonObject.AddComponent<AssetManager>();
                        DontDestroyOnLoad(singletonObject);
                        Log.Info("AssetManager singleton instance created.");
                    }
                }
                return _instance;
            }
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        // Register for scene change events
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        // Start initialization
        StartCoroutine(Initialize());
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _applicationIsQuitting = true;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }
    }
    #endregion

    #region Asset Management Fields
    private static readonly string[] _bundleNames = new[] {
        "localpoolprefabs_assets_areasong",
        "localpoolprefabs_assets_laceboss"
    };

    private static readonly string[] _assetNames = new string[] {
        "Reaper Silk Bundle"
    };

    private readonly Dictionary<Type, Dictionary<string, Object>> _assets = new();
    private readonly List<AssetBundle> _manuallyLoadedBundles = new();
    private readonly HashSet<string> _manuallyLoadedBundleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool _initialized = false;
    #endregion
    #region Public Interface
    /// <summary>
    /// Initialize the asset manager.
    /// </summary>
    public IEnumerator Initialize()
    {
        lock (_lockObject)
        {
            if (_initialized) yield break;
            _initialized = true;
        }

        Log.Info("Starting AssetManager initialization...");

        // Clear existing assets
        ClearAllAssets();

        // Process already loaded bundles
        var loadedBundles = AssetBundle.GetAllLoadedAssetBundles().ToList();
        Log.Info($"Found {loadedBundles.Count} loaded AssetBundles");

        foreach (var bundle in loadedBundles)
        {
            if (bundle != null)
            {
                ProcessBundleAssets(bundle);
            }
        }

        // Load required bundles if necessary
        if (!AreRequiredAssetsLoaded())
        {
            Log.Info("Required assets not found, starting manual bundle loading...");
            yield return ManuallyLoadBundles();
        }

        Log.Info($"AssetManager initialization completed. Loaded {_assets.Values.Sum(dict => dict.Count)} assets.");
    }

    /// <summary>
    /// Get an asset by name and type.
    /// </summary>
    public T Get<T>(string assetName) where T : Object
    {
        if (string.IsNullOrEmpty(assetName))
        {
            Log.Error("Asset name cannot be null or empty.");
            return null;
        }

        var asset = GetInternal<T>(assetName);

        if (asset == null)
        {
            // 改为警告级别，避免红色错误吓到用户
            Log.Warn($"Asset '{assetName}' ({typeof(T).Name}) not found, attempting synchronous reload...");

            // Try synchronous reload
            asset = SynchronousReload<T>(assetName);

            if (asset != null)
            {
                Log.Info($"Asset '{assetName}' reloaded successfully");
            }
            else
            {
                Log.Error($"Asset '{assetName}' ({typeof(T).Name}) retrieval failed after reload");
            }
        }

        return asset;
    }

    /// <summary>
    /// Check if the asset manager is initialized.
    /// </summary>
    public bool IsInitialized()
    {
        return _initialized;
    }

    /// <summary>
    /// Get all loaded asset names.
    /// </summary>
    public IEnumerable<string> GetAllAssetNames()
    {
        var allNames = new List<string>();
        foreach (var assetDict in _assets.Values)
        {
            allNames.AddRange(assetDict.Keys);
        }
        return allNames;
    }

    /// <summary>
    /// Unload all assets and bundles.
    /// </summary>
    public void UnloadAll()
    {
        lock (_lockObject)
        {
            foreach (var assetDict in _assets.Values)
            {
                foreach (var asset in assetDict.Values)
                {
                    if (asset != null)
                    {
                        Object.DestroyImmediate(asset);
                    }
                }
            }

            _assets.Clear();

            foreach (var bundle in _manuallyLoadedBundles)
            {
                if (bundle != null)
                {
                    bundle.UnloadAsync(true);
                }
            }

            _manuallyLoadedBundles.Clear();
            _manuallyLoadedBundleNames.Clear();

            GC.Collect();
            Log.Info("All assets and bundles unloaded.");
        }
    }
    #endregion

    #region Private Implementation
    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        Log.Info($"Scene changed: {previousScene.name} -> {newScene.name}");

        // Revalidate resources on scene change
        StartCoroutine(RevalidateResources());
    }

    private IEnumerator RevalidateResources()
    {
        Log.Info("Revalidating resource status...");

        // Clean up null references
        int removedCount = CleanupNullReferences();
        if (removedCount > 0)
        {
            Log.Info($"Cleaned up {removedCount} null references");
        }

        // Check if required assets are available
        if (!AreRequiredAssetsAvailable())
        {
            Log.Warn("Required assets not available, reinitializing...");
            yield return Reinitialize();
        }
        else
        {
            Log.Info("All required assets are available");
        }
    }

    private IEnumerator Reinitialize()
    {
        lock (_lockObject)
        {
            _initialized = false;
        }

        ClearAllAssets();
        yield return Initialize();
    }

    private int CleanupNullReferences()
    {
        int removedCount = 0;
        var typesToRemove = new List<Type>();

        foreach (var typeDict in _assets)
        {
            var keysToRemove = new List<string>();

            foreach (var kvp in typeDict.Value)
            {
                if (kvp.Value == null)
                {
                    keysToRemove.Add(kvp.Key);
                    removedCount++;
                }
            }

            foreach (var key in keysToRemove)
            {
                typeDict.Value.Remove(key);
            }

            if (typeDict.Value.Count == 0)
            {
                typesToRemove.Add(typeDict.Key);
            }
        }

        foreach (var type in typesToRemove)
        {
            _assets.Remove(type);
        }

        return removedCount;
    }

    private bool AreRequiredAssetsAvailable()
    {
        foreach (var requiredAsset in _assetNames)
        {
            var asset = GetInternal<GameObject>(requiredAsset);
            if (asset == null)
            {
                Log.Warn($"Required asset '{requiredAsset}' is not available");
                return false;
            }
        }
        return true;
    }

    private bool AreRequiredAssetsLoaded()
    {
        bool allFound = true;

        foreach (var requiredAsset in _assetNames)
        {
            bool found = false;
            foreach (var assetDict in _assets.Values)
            {
                if (assetDict.Keys.Any(key =>
                    key.IndexOf(requiredAsset, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Log.Warn($"Required asset '{requiredAsset}' not found!");
                allFound = false;
            }
        }

        return allFound;
    }

    // ... existing code ...
    private void ProcessBundleAssets(AssetBundle bundle)
    {
        if (bundle == null) return;

        try
        {
            var assetPaths = bundle.GetAllAssetNames();
            if (assetPaths == null || assetPaths.Length == 0) return;

            // Log.Info($"Processing bundle: {bundle.name} with {assetPaths.Length} assets");

            foreach (var assetPath in assetPaths)
            {
                string assetName = Path.GetFileNameWithoutExtension(assetPath);

                // 记录所有资源路径，用于调试
                Log.Debug($"Bundle {bundle.name} contains asset: {assetPath} (name: {assetName})");

                // 检查是否是我们需要的资源
                bool isRequiredAsset = _assetNames.Any(name =>
                    assetName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

                if (isRequiredAsset)
                {
                    Log.Info($"Loading required asset: {assetName} from {assetPath}");

                    try
                    {
                        // 尝试以GameObject类型加载
                        var loadedAsset = bundle.LoadAsset<GameObject>(assetPath);

                        if (loadedAsset == null)
                        {
                            // 如果失败，则用Object加载并尝试转换
                            var obj = bundle.LoadAsset<Object>(assetPath);
                            if (obj is GameObject gameObject)
                                loadedAsset = gameObject;
                            else
                                Log.Warn($"Loaded asset '{assetPath}' is not a GameObject and will be ignored.");
                        }

                        if (loadedAsset != null)
                        {
                            StoreAsset(loadedAsset);
                            Log.Info($"Successfully loaded asset: {loadedAsset.name} ({loadedAsset.GetType().Name})");
                        }
                        else
                        {
                            Log.Error($"Failed to load asset: {assetPath}");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Failed to load asset {assetPath}: {e}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to process bundle {bundle.name}: {e}");
        }
    }
    private void StoreAsset(Object asset)
    {
        Type assetType = asset.GetType();
        string assetName = asset.name;

        if (!_assets.ContainsKey(assetType))
        {
            _assets[assetType] = new Dictionary<string, Object>();
        }

        var assetDict = _assets[assetType];

        // Remove existing entry if present
        if (assetDict.ContainsKey(assetName))
        {
            assetDict.Remove(assetName);
        }

        // Store with strong reference
        assetDict[assetName] = asset;
        Log.Debug($"Stored asset: {assetName} ({assetType.Name})");
    }

    private IEnumerator ManuallyLoadBundles()
    {
        string platformFolder = GetPlatformFolder();

        foreach (string bundleName in _bundleNames)
        {
            yield return LoadBundle(bundleName, GetStandardBundlePath, platformFolder);
        }
    }

    private IEnumerator LoadBundle(string bundleName, Func<string, string, string> pathBuilder, string platformFolder)
    {
        if (IsBundleAlreadyLoaded(bundleName))
        {
            Log.Info($"AssetBundle '{bundleName}' is already loaded, skipping...");
            yield break;
        }

        string bundlePath = pathBuilder(bundleName, platformFolder);

        if (!File.Exists(bundlePath))
        {
            Log.Error($"AssetBundle file not found: {bundlePath}");
            yield break;
        }

        var bundleLoadRequest = AssetBundle.LoadFromFileAsync(bundlePath);
        yield return bundleLoadRequest;

        AssetBundle bundle = bundleLoadRequest.assetBundle;
        if (bundle == null)
        {
            Log.Error($"Failed to load AssetBundle: {bundlePath}");
            yield break;
        }

        _manuallyLoadedBundles.Add(bundle);
        ProcessBundleAssets(bundle);
        _manuallyLoadedBundleNames.Add(bundleName);
        Log.Info($"Successfully loaded bundle: {bundleName}");
    }

    private bool IsBundleAlreadyLoaded(string bundleName)
    {
        if (_manuallyLoadedBundleNames.Contains(bundleName))
        {
            return true;
        }

        foreach (var loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loadedBundle == null) continue;

            var assetPaths = loadedBundle.GetAllAssetNames();
            if (assetPaths == null || assetPaths.Length == 0)
                continue;

            foreach (var assetPath in assetPaths)
            {
                string assetName = Path.GetFileNameWithoutExtension(assetPath);
                if (_assetNames.Any(name =>
                    assetName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _manuallyLoadedBundleNames.Add(bundleName);
                    return true;
                }
            }
        }

        return false;
    }

    private T GetInternal<T>(string assetName) where T : Object
    {
        Type assetType = typeof(T);

        if (!_assets.ContainsKey(assetType))
        {
            return null;
        }

        var assetDict = _assets[assetType];

        if (assetDict.ContainsKey(assetName))
        {
            var asset = assetDict[assetName] as T;
            if (asset != null)
            {
                return asset;
            }
            else
            {
                // Asset exists but is null or type mismatch, remove it
                assetDict.Remove(assetName);
                Log.Warn($"Asset {assetName} was invalid, removed from cache");
            }
        }

        return null;
    }

    private T SynchronousReload<T>(string assetName) where T : Object
    {
        Log.Warn($"Synchronously reloading asset: {assetName}");

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
                        var loadedAsset = bundle.LoadAsset<T>(assetPath);
                        if (loadedAsset != null)
                        {
                            StoreAsset(loadedAsset);
                            Log.Info($"Asset {assetName} reloaded successfully");
                            return loadedAsset;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Failed to reload asset {assetName}: {e}");
            }
        }

        Log.Error($"Unable to reload asset {assetName}");
        return null;
    }

    private void ClearAllAssets()
    {
        _assets.Clear();
        _manuallyLoadedBundleNames.Clear();
        _manuallyLoadedBundles.Clear();
        Log.Info("Cleared all asset caches");
    }

    private static string GetPlatformFolder()
    {
        return Application.platform switch
        {
            RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
            RuntimePlatform.OSXPlayer => "StandaloneOSX",
            RuntimePlatform.LinuxPlayer => "StandaloneLinux64",
            _ => ""
        };
    }

    private static string GetStandardBundlePath(string bundleName, string platformFolder)
    {
        return Path.Combine(Addressables.RuntimePath, platformFolder, $"{bundleName}.bundle");
    }
    #endregion
}