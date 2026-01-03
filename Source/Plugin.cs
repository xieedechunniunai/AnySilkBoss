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
using AnySilkBoss.Source.Handlers;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>
    /// Boulder 贴图（从嵌入资源加载）
    /// </summary>
    public static Texture2D? BoulderTexture { get; private set; }

    private void Awake()
    {
        Log.Init(Logger);
        Plugin.Instance = this;
        LoadEmbeddedTextures();

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

            // Boss 行为补丁
            _harmony.PatchAll(typeof(BossPatches));

            // 本地化补丁
            _harmony.PatchAll(typeof(I18nPatches));

            // 减伤系统补丁
            _harmony.PatchAll(typeof(DamageReductionPatches));

            // 梦境模式补丁
            _harmony.PatchAll(typeof(MemorySceneTransitionPatch));
            _harmony.PatchAll(typeof(MemoryClashTinkPatch));
            _harmony.PatchAll(typeof(MemoryParryDownspikePatch));
            _harmony.PatchAll(typeof(HeroDamageStackPatch));

            // 死亡相关补丁
            _harmony.PatchAll(typeof(HeroControllerDeathPatches));
            _harmony.PatchAll(typeof(GameManagerRespawnPatches));

            // 护符/道具补丁
            _harmony.PatchAll(typeof(ToolItemPatches));

            // 初始化音频替换系统
            AudioHandler.Initialize();
            AudioHandler.ApplyPatches(_harmony);

            Log.Info("Harmony patches applied successfully");
        }
        catch (System.Exception ex)
        {
            Log.Error($"Failed to apply Harmony patches: {ex.Message}");
            Log.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    private void OnSceneChange(Scene oldScene, Scene newScene)
    {
        // 更新是否在Boss房间的状态
        Plugin.IsInBossRoom = newScene.name == "Cradle_03";

        // 当切换到主菜单时，清空所有丝球池和缓存
        if (newScene.name == "Menu_Title")
        {
            Log.Info("[Plugin] 切换到主菜单，清空丝球池和缓存...");
            var silkBallManager = AnySilkBossManager?.GetComponent<SilkBallManager>();
            if (silkBallManager != null)
            {
                silkBallManager.ResetOnReturnToMenu();
            }
            return;
        }

        if (newScene.name == "Cradle_03")
        {
            // 重置所有平台状态
            StartCoroutine(ResetBossPlatformsDelay());
            DamageStackManager.Reset();
        }

        // 当从主菜单加载存档时创建管理器
        if (oldScene.name == "Menu_Title")
        {
            CreateManager();
            
            // 重新初始化丝球管理器（获取玩家引用和重建池子）
            StartCoroutine(ReinitializeSilkBallManagerDelayed());
            return;
        }
    }

    /// <summary>
    /// 延迟重新初始化丝球管理器（等待玩家加载完成）
    /// </summary>
    private IEnumerator ReinitializeSilkBallManagerDelayed()
    {
        // 等待一小段时间，确保玩家已经加载
        yield return new WaitForSeconds(0.5f);

        var silkBallManager = AnySilkBossManager?.GetComponent<SilkBallManager>();
        if (silkBallManager != null)
        {
            silkBallManager.ReinitializeOnEnterGame();
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
            var assetManager = AnySilkBossManager.AddComponent<AssetManager>();

            // 添加 DamageHero 事件管理器组件
            AnySilkBossManager.AddComponent<DamageHeroEventManager>();

            // 添加丝球管理器组件
            AnySilkBossManager.AddComponent<SilkBallManager>();

            // 添加大丝球管理器组件
            AnySilkBossManager.AddComponent<BigSilkBallManager>();

            // 添加 Memory 模式管理器组件
            AnySilkBossManager.AddComponent<MemoryManager>();
            // 添加死亡管理器组件
            AnySilkBossManager.AddComponent<DeathManager>();

            // 添加单根丝线管理器组件
            AnySilkBossManager.AddComponent<SingleWebManager>();

            // 添加 First Weaver Pin 管理器组件
            AnySilkBossManager.AddComponent<FWPinManager>();

            // 添加 First Weaver Blast 管理器组件
            AnySilkBossManager.AddComponent<FWBlastManager>();

            // 添加 LaceCircleSlash 管理器组件
            AnySilkBossManager.AddComponent<LaceCircleSlashManager>();

            Log.Info("创建持久化管理器和所有组件");

            // 启动预加载所有外部资源的协程
            StartCoroutine(assetManager.PreloadAllExternalAssets());
        }
        else
        {
            Log.Info("找到已存在的持久化管理器");
        }
    }

    private IEnumerator ResetBossPlatformsDelay()
    {
        yield return new WaitForSeconds(2.5f);
        ResetBossPlatforms();
    }
    /// <summary>
    /// 重置 Boss 房间的所有平台状态（将倒塌的平台恢复）
    /// </summary>
    private void ResetBossPlatforms()
    {
        try
        {
            if (IsInBossRoom)
            {
                // 查找所有包含 cradle_plat 的物品
                var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                var cradlePlats = allObjects.Where(x => x.name.Contains("cradle_plat") && !x.name.Contains("spike")).ToList();
                var cradleSpikePlats = allObjects.Where(x => x.name.Contains("cradle_spike_plat")).ToList();
                // 处理 cradle_plat：禁用子物品 crank_hit 并重置位置
                foreach (var plat in cradlePlats)
                {
                    Transform crankHit = plat.transform.Find("crank_hit");
                    if (crankHit != null)
                    {
                        crankHit.gameObject.SetActive(false);
                    }
                    // 重置特定平台的位置
                    switch (plat.name)
                    {
                        case "cradle_plat (6)":
                            plat.transform.position = new Vector3(39.81f, 58.35f, -0.2602f);
                            Log.Info($"重置 {plat.name} 的位置");
                            break;
                        case "cradle_plat (1)":
                            plat.transform.position = new Vector3(49.01f, 64.94f, -0.2602f);
                            Log.Info($"重置 {plat.name} 的位置");
                            break;
                        case "cradle_plat (7)":
                            plat.transform.position = new Vector3(31.81f, 80.87f, -0.2602f);
                            Log.Info($"重置 {plat.name} 的位置");
                            break;
                        case "cradle_plat (8)":
                            plat.transform.position = new Vector3(48.9f, 93.74f, -0.2602f);
                            Log.Info($"重置 {plat.name} 的位置");
                            break;
                        case "cradle_plat":
                            plat.transform.position = new Vector3(31.27f, 108.25f, -0.2602f);
                            Log.Info($"重置 {plat.name} 的位置");
                            break;
                    }
                }

                // 处理 cradle_spike_plat：修改 spikes hit 的 DamageHero 组件
                foreach (var spikePlat in cradleSpikePlats)
                {
                    Transform spikesHit = spikePlat.transform.Find("spikes hit");
                    if (spikesHit != null)
                    {
                        DamageHero damageHero = spikesHit.GetComponent<DamageHero>();
                        if (damageHero != null)
                        {
                            damageHero.damageDealt = 2;
                            damageHero.hazardType = (int)GlobalEnums.HazardType.NON_HAZARD;
                        }
                    }
                    else
                    {
                        Log.Warn($"{spikePlat.name} 没有找到子物品 spikes hit");
                    }
                }
            }
            // 检查 GameManager
            if (GameManager.instance == null)
            {
                Log.Warn("GameManager.instance 为 null，无法重置平台");
                return;
            }

            // 检查 sceneData
            var sceneData = GameManager.instance.sceneData;
            if (sceneData == null)
            {
                Log.Warn("sceneData 为 null，无法重置平台");
                return;
            }

            // 检查 persistentBools
            if (sceneData.persistentBools == null)
            {
                Log.Warn("persistentBools 为 null，无法重置平台");
                return;
            }

            // 检查 serializedList
            if (sceneData.persistentBools.serializedList == null)
            {
                Log.Warn("serializedList 为 null，无法重置平台");
                return;
            }

            var cradleItems = sceneData.persistentBools.serializedList.FindAll(x => x.SceneName == "Cradle_03" && x.ID.Contains("plat"));
            if (cradleItems.Count == 0)
            {
                return;
            }
            int resetCount = 0;
            foreach (var platform in cradleItems)
            {
                bool oldValue = platform.Value;
                platform.Value = false;
                resetCount++;
            }

        }
        catch (System.Exception ex)
        {
            Log.Error($"重置平台失败: {ex.Message}");
            Log.Error($"堆栈跟踪: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 加载嵌入在程序集中的贴图资源
    /// </summary>
    private void LoadEmbeddedTextures()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            if (resourceName.Contains("SLIK"))
            {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                BoulderTexture = new Texture2D(2, 2);
                BoulderTexture.LoadImage(buffer);
                BoulderTexture.filterMode = FilterMode.Point;
                BoulderTexture.wrapMode = TextureWrapMode.Clamp;
                Log.Info($"成功加载 Boulder 贴图: {resourceName}");
            }
        }
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }
}