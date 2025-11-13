using System.IO;
using System.Reflection;
using System.Collections;
using BepInEx;
using HarmonyLib;
using AnySilkBoss.Source.Patches;
using AnySilkBoss.Source.Behaviours;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
namespace AnySilkBoss.Source;

/// <summary>
/// The main plugin class.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static Harmony _harmony = null!;
    private GameObject AnySilkBossManager = null!;
    
    // 全局状态
    public static Plugin Instance { get; private set; }
    public static bool IsBossSaveLoaded { get; set; } = false;
    public static string OriginalSaveBackupPath { get; set; } = null;
    public static bool IsInBossRoom { get; set; } = false;
    public static string CurrentSaveFileName { get; set; } = null;

    private void Awake()
    {
        Log.Init(Logger);
        Plugin.Instance = this;
        
        SceneManager.activeSceneChanged += OnSceneChange;
        
        // 延迟应用Harmony补丁，等待游戏完全初始化
        StartCoroutine(DelayedPatchApplication());
        
        Log.Info("AnySilkBoss plugin loaded!");
    }

    private IEnumerator DelayedPatchApplication()
    {
        // 等待几帧，确保游戏完全初始化
        yield return new WaitForSeconds(1f);
        
        try
        {
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(BossPatches));
            Log.Info("BossPatches Harmony patches applied successfully");
        }
        catch (System.Exception ex)
        {
            Log.Error($"Failed to apply BossPatches Harmony patches: {ex.Message}");
            Log.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    private void OnSceneChange(Scene oldScene, Scene newScene)
    {
        // 更新是否在Boss房间的状态
        Plugin.IsInBossRoom = newScene.name == "Cradle_03";

        if (Plugin.IsInBossRoom)
        {
            Log.Info($"切换到Boss场景: {newScene.name}");
        }
        
        // 当从主菜单加载存档时创建管理器
        if (oldScene.name == "Menu_Title")
        {
            CreateManager();
            return;
        }
    }

    private void CreateManager()
    {
        // 查找是否已存在持久化管理器
        AnySilkBossManager = GameObject.Find("AnySilkBossManager");
        if (AnySilkBossManager == null)
        {
            AnySilkBossManager = new GameObject("AnySilkBossManager");
            UnityEngine.Object.DontDestroyOnLoad(AnySilkBossManager);

            // 添加资源管理器组件
            AnySilkBossManager.AddComponent<AssetManager>();
            
            // 添加 DamageHero 事件管理器组件
            AnySilkBossManager.AddComponent<DamageHeroEventManager>();
            
            // 添加丝球管理器组件
            AnySilkBossManager.AddComponent<SilkBallManager>();
            
            // 添加大丝球管理器组件
            AnySilkBossManager.AddComponent<BigSilkBallManager>();
            
            // 添加存档管理器组件
            AnySilkBossManager.AddComponent<SaveSwitchManager>();
            
            // 添加死亡管理器组件
            AnySilkBossManager.AddComponent<DeathManager>();
            
            // 添加工具恢复管理器组件
            AnySilkBossManager.AddComponent<ToolRestoreManager>();
            
            // 添加单根丝线管理器组件
            AnySilkBossManager.AddComponent<SingleWebManager>();
            
            Log.Info("创建持久化管理器和所有组件");
        }
        else
        {
            Log.Info("找到已存在的持久化管理器");
        }
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
        if (AssetManager.Instance != null)
        {
            AssetManager.Instance.UnloadAll();
        }
    }
}