using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 大丝球管理器 - 负责创建和管理天女散花大招的大丝球预制体
    /// 从场景中复制 silk_cocoon_core 对象作为模板
    /// </summary>
    internal class BigSilkBallManager : MonoBehaviour
    {
        #region Fields
        private GameObject? _bigSilkBallPrefab;
        public GameObject? BigSilkBallPrefab => _bigSilkBallPrefab;

        private bool _initialized = false;
        
        // 缓存的 Animator 组件引用（用于播放爆炸动画）
        private Animator? _cachedAnimator;
        
        // BOSS 场景名称
        private const string BossSceneName = "Cradle_03";
        #endregion

        private void OnEnable()
        {
            // 监听场景加载事件
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            // 取消监听场景加载事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 场景加载回调
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 只在 BOSS 场景时处理
            if (scene.name != BossSceneName)
            {
                return;
            }

            // 首次初始化
            if (!_initialized)
            {
                Log.Info($"检测到 BOSS 场景 {scene.name}，开始初始化 BigSilkBallManager...");
                StartCoroutine(Initialize());
            }
        }


        #region Initialization
        /// <summary>
        /// 初始化大丝球管理器
        /// </summary>
        private IEnumerator Initialize()
        {
            if (_initialized)
            {
                Log.Info("BigSilkBallManager 已初始化");
                yield break;
            }

            Log.Info("开始初始化 BigSilkBallManager...");

            // 等待场景加载完成
            yield return new WaitForSeconds(0.5f);

            // 创建大丝球预制体
            yield return CreateBigSilkBallPrefab();

            _initialized = true;
            Log.Info("BigSilkBallManager 初始化完成");
        }

        /// <summary>
        /// 从场景中复制 silk_cocoon_core 创建大丝球预制体
        /// </summary>
        private IEnumerator CreateBigSilkBallPrefab()
        {
            Log.Info("=== 开始创建大丝球预制体 ===");

            // 查找场景中的 silk_cocoon_core 对象
            GameObject? originalCore = FindSilkCocoonCore();
            if (originalCore == null)
            {
                Log.Error("未找到 silk_cocoon_core 对象");
                yield break;
            }

            Log.Info($"成功找到 silk_cocoon_core: {originalCore.name}");
            // AnalyzeOriginalStructure(originalCore);

            // 复制整个对象
            _bigSilkBallPrefab = Object.Instantiate(originalCore);
            _bigSilkBallPrefab.name = "Big Silk Ball Prefab";

            // 永久保存，不随场景销毁
            DontDestroyOnLoad(_bigSilkBallPrefab);

            Log.Info("大丝球对象复制完成，开始处理组件...");

            // 处理组件
            ProcessComponents();

            // 处理子物体
            ProcessChildObjects();

            // 立即禁用（作为预制体模板）
            _bigSilkBallPrefab.SetActive(false);

            Log.Info($"=== 大丝球预制体创建完成: {_bigSilkBallPrefab.name} ===");
            yield return null;
        }

        /// <summary>
        /// 查找场景中的 silk_cocoon_core 对象
        /// </summary>
        private GameObject? FindSilkCocoonCore()
        {
            // 尝试通过路径查找
            GameObject? bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                Transform? coreTransform = bossScene.transform.Find("silk_cocoon_core");
                if (coreTransform != null)
                {
                    return coreTransform.gameObject;
                }
            }

            // 备用方案：直接查找所有对象
            var allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                if (obj.name == "silk_cocoon_core")
                {
                    return obj;
                }
            }

            return null;
        }

        /// <summary>
        /// 处理根物体组件
        /// </summary>
        private void ProcessComponents()
        {
            if (_bigSilkBallPrefab == null) return;

            Log.Info("--- 处理根物体组件 ---");

            // 保留 Animator 组件（用于播放爆炸动画）
            _cachedAnimator = _bigSilkBallPrefab.GetComponent<Animator>();
            if (_cachedAnimator != null)
            {
                Log.Info("保留 Animator 组件（用于播放 Silk_Cocoon_Intro_Burst 动画）");
            }
            else
            {
                Log.Warn("未找到 Animator 组件");
            }

            // 删除原版的 PlayMakerFSM（Loop Beat Anim - 只是循环小动画，无用）
            var oldFSMs = _bigSilkBallPrefab.GetComponents<PlayMakerFSM>();
            foreach (var fsm in oldFSMs)
            {
                if (fsm.FsmName == "Loop Beat Anim")
                {
                    Log.Info($"删除无用的 PlayMakerFSM: {fsm.FsmName}");
                    Object.Destroy(fsm);
                }
            }

            // 保留 CameraControlAnimationEvents（如果有）
            var camControlEvents = _bigSilkBallPrefab.GetComponent("CameraControlAnimationEvents");
            if (camControlEvents != null)
            {
                Log.Info("保留 CameraControlAnimationEvents 组件");
            }

            // 保留 CaptureAnimationEvent（如果有）
            var captureAnimEvent = _bigSilkBallPrefab.GetComponent("CaptureAnimationEvent");
            if (captureAnimEvent != null)
            {
                Log.Info("保留 CaptureAnimationEvent 组件");
            }
        }

        /// <summary>
        /// 处理子物体
        /// </summary>
        private void ProcessChildObjects()
        {
            if (_bigSilkBallPrefab == null) return;

            Log.Info("--- 处理子物体 ---");
            
            // 删除不需要的子物体
            foreach (Transform child in _bigSilkBallPrefab.transform)
            {
                if (child.name == "scene_dust")
                {
                    Log.Info($"  删除子物体: {child.name}");
                    Object.Destroy(child.gameObject);
                }
                else if (child.name == "tendrils")
                {
                    Log.Info($"  删除子物体: {child.name}");
                    Object.Destroy(child.gameObject);
                }
                else if (child.name == "loom_threads")
                {
                    Log.Info($"  删除子物体: {child.name}");
                    Object.Destroy(child.gameObject);
                }
            }
            
            // 删除heart下的heart_shadow
            Transform heart = _bigSilkBallPrefab.transform.Find("heart");
            if (heart != null)
            {
                Transform heartShadow = heart.Find("hanging_silk");
                if (heartShadow != null)
                {
                    Log.Info($"  删除 heart/hanging_silk");
                    Object.Destroy(heartShadow.gameObject);
                }
            }
            
            // scene_dust - 场景灰尘效果（已删除）
            // over_glow - 发光效果（保留）
            // heart(9层) - 核心心脏（保留，但删除其中的heart_shadow）
            // burst(13层) - 爆炸效果（保留）
            // tendrils(5层) - 触须（已删除）
            // loom_threads(2层) - 织网线（删除）
            // Audio Heartbeat - 心跳音效（保留）

            // 记录所有保留的子物体
            Log.Info("保留的子物体:");
            foreach (Transform child in _bigSilkBallPrefab.transform)
            {
                Log.Info($"  - {child.name}");
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 生成一个大丝球实例
        /// </summary>
        public GameObject? SpawnBigSilkBall(Vector3 position, GameObject bossObject)
        {
            if (_bigSilkBallPrefab == null)
            {
                Log.Error("大丝球预制体未初始化");
                return null;
            }

            var bigSilkBall = Object.Instantiate(_bigSilkBallPrefab);
            
            // 根物品保持原版的Z轴（57.4491）和原版Scale，只调整XY位置
            Vector3 rootPosition = position;
            rootPosition.z = 57.4491f;  // 保持原版Z轴
            bigSilkBall.transform.position = rootPosition;
            
            // 确保根物品Scale为原版（应该默认就是，但记录一下）
            Log.Info($"根物品Scale: {bigSilkBall.transform.localScale}（应保持原版）");
            
            // 保持heart的原版相对位置（不做任何调整）
            // 后续通过PhaseControl调整BOSS的Z轴，让BOSS和大丝球在同一深度
            Transform heart = bigSilkBall.transform.Find("heart");
            if (heart != null)
            {
                Vector3 heartLocalPos = heart.localPosition;
                Log.Info($"heart保持原版相对位置: ({heartLocalPos.x:F4}, {heartLocalPos.y:F4}, {heartLocalPos.z:F4})");
            }
            else
            {
                Log.Warn("未找到heart子物品");
            }
            
            bigSilkBall.SetActive(true);
            Log.Info($"生成大丝球 - 根物品位置: {rootPosition}，heart将显示在Z≈0的位置");

    
            // 添加 BigSilkBallBehavior 组件
            var behavior = bigSilkBall.GetComponent<BigSilkBallBehavior>();
            if (behavior == null)
            {
                behavior = bigSilkBall.AddComponent<BigSilkBallBehavior>();
            }

            // 初始化行为（传入根物品获取组件，同时传入heart用于位置和缩放）
            behavior.Initialize(bigSilkBall, bossObject, heart);

            return bigSilkBall;
        }
        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized()
        {
            return _initialized;
        }
        
        /// <summary>
        /// 清理所有活跃的大丝球实例
        /// </summary>
        public void DestroyAllActiveBigSilkBalls()
        {
            if (_bigSilkBallPrefab == null)
            {
                Log.Warn("大丝球预制体未初始化，无法清理");
                return;
            }

            // 查找所有活跃的大丝球实例
            var allBigSilkBalls = FindObjectsByType<BigSilkBallBehavior>(FindObjectsSortMode.None);
            int destroyedCount = 0;

            foreach (var behavior in allBigSilkBalls)
            {
                if (behavior != null && behavior.gameObject != null)
                {
                    // 确保不是预制体本身
                    if (behavior.gameObject != _bigSilkBallPrefab)
                    {
                        Object.Destroy(behavior.gameObject);
                        destroyedCount++;
                    }
                }
            }

            Log.Info($"已清理场景中的大丝球实例，共销毁 {destroyedCount} 个");
        }
         /// <summary>
        /// 分析物体的完整层级结构和位置信息（用于调试）
        /// </summary>
        public void AnalyzeHierarchy(GameObject obj)
        {
            if (obj == null)
            {
                Log.Warn("分析对象为 null");
                return;
            }

            Log.Info("===== BigSilkBall 层级结构和位置分析 =====");
            Log.Info($"根物体: {obj.name}");
            Log.Info($"世界坐标: {obj.transform.position}");
            Log.Info($"本地坐标: {obj.transform.localPosition}");
            Log.Info($"旋转: {obj.transform.rotation.eulerAngles}");
            Log.Info($"缩放: {obj.transform.localScale}");
            Log.Info("");

            AnalyzeHierarchyRecursive(obj.transform, 0);
            Log.Info("===== 分析完成 =====");
        }

        /// <summary>
        /// 递归分析层级结构
        /// </summary>
        private void AnalyzeHierarchyRecursive(Transform parent, int depth)
        {
            string indent = new string(' ', depth * 2);
            
            foreach (Transform child in parent)
            {
                Log.Info($"{indent}[{depth}] {child.name}");
                Log.Info($"{indent}    世界坐标: ({child.position.x:F4}, {child.position.y:F4}, {child.position.z:F4})");
                Log.Info($"{indent}    本地坐标: ({child.localPosition.x:F4}, {child.localPosition.y:F4}, {child.localPosition.z:F4})");
                Log.Info($"{indent}    本地旋转: ({child.localRotation.eulerAngles.x:F2}, {child.localRotation.eulerAngles.y:F2}, {child.localRotation.eulerAngles.z:F2})");
                Log.Info($"{indent}    本地缩放: ({child.localScale.x:F4}, {child.localScale.y:F4}, {child.localScale.z:F4})");
                
                // 列出组件
                var components = child.GetComponents<Component>();
                if (components.Length > 1) // Transform总是存在，所以>1才有其他组件
                {
                    Log.Info($"{indent}    组件:");
                    foreach (var comp in components)
                    {
                        if (comp is Transform) continue; // 跳过Transform
                        Log.Info($"{indent}      - {comp.GetType().Name}");
                        
                        // 如果是SpriteRenderer，显示额外信息
                        if (comp is SpriteRenderer sr)
                        {
                            Log.Info($"{indent}        Sprite: {sr.sprite?.name ?? "null"}");
                            Log.Info($"{indent}        Color: {sr.color}");
                            Log.Info($"{indent}        SortingLayer: {sr.sortingLayerName} (Order: {sr.sortingOrder})");
                        }
                    }
                }
                
                Log.Info("");
                
                // 递归处理子物品
                if (child.childCount > 0)
                {
                    AnalyzeHierarchyRecursive(child, depth + 1);
                }
            }
        }

        /// <summary>
        /// 分析原版silk_cocoon_core的位置信息（在创建预制体时调用）
        /// </summary>
        private void AnalyzeOriginalStructure(GameObject obj)
        {
            Log.Info("===== 原版 silk_cocoon_core 完整分析 =====");
            AnalyzeHierarchy(obj);
        }
        #endregion
    }
}

