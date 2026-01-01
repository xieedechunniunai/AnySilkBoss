using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Memory;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// First Weaver Bomb Blast 管理器
    /// 负责管理 Bomb Blast 物品的对象池
    /// 
    /// 生命周期：
    /// - 进入 BOSS 场景 (Cradle_03) 时：通过 AssetManager 获取预制体，创建对象池
    /// - 离开 BOSS 场景时：销毁对象池内的所有物品（预制体由 AssetManager 持久化管理）
    /// 
    /// First Weaver Bomb Blast FSM 结构（名称: Control）：
    /// - Start (FINISHED) → Check Closest Blast
    /// - Check Closest Blast (FINISHED/RETRY) → Appear Pause / Retry Frame
    /// - Retry Frame (FINISHED) → Check Closest Blast
    /// - Appear Pause (FINISHED) → Blast
    /// - Blast (FINISHED) → Wait
    /// - Wait (FINISHED) → End
    /// - End：原版包含 RecycleSelf action（我们需要删除并替换为自定义回收）
    /// 
    /// 使用方式：
    /// - 从池中取出对象后 SetActive(true)，FSM 会自动从 Start 开始运行
    /// - FSM 执行到 End 状态时，会调用我们的回收方法
    /// - 回收方法会禁用对象并放回池中
    /// </summary>
    internal class FWBlastManager : MonoBehaviour
    {
        #region 常量配置
        /// <summary>BOSS 场景名称</summary>
        private const string BOSS_SCENE_NAME = "Cradle_03";

        /// <summary>AssetManager 中的 Bomb Blast 资源名称</summary>
        private const string BOMB_BLAST_ASSET_NAME = "First Weaver Bomb Blast";

        /// <summary>FSM 分析输出路径</summary>
        private const string FSM_OUTPUT_PATH = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\";

        /// <summary>Bomb Blast 对象池大小</summary>
        private const int BOMB_BLAST_POOL_SIZE = 40;
        #endregion

        #region 字段
        /// <summary>Bomb Blast 预制体（从 AssetManager 获取）</summary>
        private GameObject? _bombBlastPrefab;

        /// <summary>Bomb Blast 对象池（可用的实例）</summary>
        private readonly List<GameObject> _bombBlastPool = new();

        /// <summary>对象池容器</summary>
        private GameObject? _poolContainer;

        /// <summary>初始化标志</summary>
        private bool _initialized = false;

        /// <summary>正在初始化标志</summary>
        private bool _initializing = false;

        /// <summary>AssetManager 引用</summary>
        private AssetManager? _assetManager;

        /// <summary>Blast 动画控制器（从 AssetManager 获取）</summary>
        private RuntimeAnimatorController? _blastAnimatorController;
        #endregion

        #region 属性
        /// <summary>Bomb Blast 预制体</summary>
        public GameObject? BombBlastPrefab => _bombBlastPrefab;

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => _initialized;
        #endregion

        #region 生命周期
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnDestroy()
        {
            CleanupPool();
        }
        #endregion

        #region 场景管理
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BOSS_SCENE_NAME)
            {
                return;
            }

            Log.Info($"[FWBlastManager] 检测到 BOSS 场景 {scene.name}，开始初始化...");
            StartCoroutine(Initialize());
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            // 离开 BOSS 场景（到其他场景）时完全清理
            if (oldScene.name == BOSS_SCENE_NAME && newScene.name != BOSS_SCENE_NAME)
            {
                Log.Info($"[FWBlastManager] 离开 BOSS 场景 {oldScene.name}，清理缓存");
                CleanupPool();
            }
            // 同场景重载（如死亡复活）时，回收所有活跃的 Blast 到池中
            else if (oldScene.name == BOSS_SCENE_NAME && newScene.name == BOSS_SCENE_NAME)
            {
                Log.Info($"[FWBlastManager] 检测到场景重载（死亡复活），回收所有活跃的 Blast");
                RecycleAllBombBlasts();
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化管理器
        /// </summary>
        public IEnumerator Initialize()
        {
            if (_initialized || _initializing)
            {
                Log.Info("[FWBlastManager] 已初始化或正在初始化，跳过");
                yield break;
            }

            _initializing = true;
            Log.Info("[FWBlastManager] 开始初始化...");

            yield return new WaitForSeconds(0.2f);

            // 1. 获取 AssetManager 并加载预制体
            yield return LoadPrefabFromAssetManager();

            // 2. 分析 FSM
            AnalyzePrefabFsm();

            // 3. 创建对象池容器
            CreatePoolContainer();

            // 4. 预生成对象池
            yield return PrewarmPool();

            _initialized = true;
            _initializing = false;
            Log.Info("[FWBlastManager] 初始化完成");
        }

        private IEnumerator LoadPrefabFromAssetManager()
        {
            Log.Info($"[FWBlastManager] 从 AssetManager 获取预制体...");

            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    _assetManager = managerObj.GetComponent<AssetManager>();
                }
            }

            if (_assetManager == null)
            {
                Log.Error("[FWBlastManager] 未找到 AssetManager，无法加载预制体");
                yield break;
            }

            if (!_assetManager.IsExternalAssetsPreloaded)
            {
                Log.Info("[FWBlastManager] 等待 AssetManager 预加载完成...");
                yield return _assetManager.WaitForPreload();
            }

            _bombBlastPrefab = _assetManager.Get<GameObject>(BOMB_BLAST_ASSET_NAME);

            if (_bombBlastPrefab != null)
            {
                Log.Info($"[FWBlastManager] Bomb Blast 获取成功: {_bombBlastPrefab.name}");
            }
            else
            {
                Log.Error("[FWBlastManager] Bomb Blast 获取失败：返回 null");
            }

            // 加载动画控制器
            _blastAnimatorController = _assetManager.Get<RuntimeAnimatorController>("focus_blast_first_weaver");
            if (_blastAnimatorController != null)
            {
                Log.Info($"[FWBlastManager] 动画控制器获取成功: {_blastAnimatorController.name}");
            }
            else
            {
                Log.Error("[FWBlastManager] 动画控制器获取失败");
            }
        }

        private void AnalyzePrefabFsm()
        {
            if (_bombBlastPrefab != null)
            {
                var bombBlastFsm = _bombBlastPrefab.LocateMyFSM("Control");
                if (bombBlastFsm != null)
                {
                    string outputPath = FSM_OUTPUT_PATH + "_bombBlastFsm.txt";
                    FsmAnalyzer.WriteFsmReport(bombBlastFsm, outputPath);
                    Log.Info($"[FWBlastManager] Bomb Blast FSM 分析完成: {outputPath}");
                }
                else
                {
                    Log.Warn("[FWBlastManager] Bomb Blast 未找到 Control FSM");
                }
            }
        }

        private void CreatePoolContainer()
        {
            if (_poolContainer == null)
            {
                _poolContainer = new GameObject("FWBombBlastPool");
                _poolContainer.transform.SetParent(transform);
                Log.Info("[FWBlastManager] 对象池容器已创建");
            }
        }

        private IEnumerator PrewarmPool()
        {
            Log.Info("[FWBlastManager] 开始预生成对象池...");

            if (_bombBlastPrefab != null)
            {
                for (int i = 0; i < BOMB_BLAST_POOL_SIZE; i++)
                {
                    var obj = CreateBombBlastInstance();
                    if (obj != null)
                    {
                        _bombBlastPool.Add(obj);
                    }

                    if (i % 2 == 0)
                    {
                        yield return null;
                    }
                }
                Log.Info($"[FWBlastManager] Bomb Blast 池预生成完成: {_bombBlastPool.Count} 个");
            }
        }
        #endregion

        #region 对象池操作
        private void SetBombBlastPooled(GameObject obj)
        {
            if (obj == null) return;

            var fsm = obj.LocateMyFSM("Control");
            if (fsm != null)
            {
                fsm.enabled = false;
            }

            var rb = obj.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
            }

            var blastChild = obj.transform.Find("Blast");
            if (blastChild != null)
            {
                blastChild.gameObject.SetActive(false);
            }

            obj.tag = "Untagged";
        }

        private void ActivateBombBlast(GameObject obj)
        {
            if (obj == null) return;

            var rb = obj.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.simulated = true;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            var fsm = obj.LocateMyFSM("Control");
            if (fsm != null)
            {
                fsm.enabled = true;
                fsm.Fsm.SetState("Start");
            }

            if (!obj.activeSelf)
            {
                obj.SetActive(true);
            }
        }

        /// <summary>
        /// 创建 Bomb Blast 实例
        /// 实例化后需要补丁 FSM，删除 RecycleSelf 并添加自定义回收逻辑
        /// </summary>
        private GameObject? CreateBombBlastInstance()
        {
            if (_bombBlastPrefab == null || _poolContainer == null)
            {
                return null;
            }

            var obj = Instantiate(_bombBlastPrefab, _poolContainer.transform);
            obj.name = $"First Weaver Bomb Blast (Pool)";

            // 先禁用对象，防止 FSM 自动运行
            obj.SetActive(false);

            // 修复 Blast 子物品的 Animator 控制器引用
            FixBlastAnimator(obj);

            // 添加 BombBlastBehavior 组件（FSM补丁逻辑已迁移到该组件）
            var behavior = obj.AddComponent<BombBlastBehavior>();

            behavior.EnsureFsmPatched();

            SetBombBlastPooled(obj);

            if (!obj.activeSelf)
            {
                obj.SetActive(true);
            }

            return obj;
        }

        /// <summary>
        /// 修复 Blast 子物品的 Animator 控制器引用
        /// 由于 bundle 卸载后 RuntimeAnimatorController 引用丢失，需要重新赋值
        /// </summary>
        private void FixBlastAnimator(GameObject bombBlastObj)
        {
            if (_blastAnimatorController == null)
            {
                Log.Warn("[FWBlastManager] 动画控制器未加载，无法修复 Animator");
                return;
            }

            // 查找子物品 Blast
            var blastChild = bombBlastObj.transform.Find("Blast");
            if (blastChild == null)
            {
                Log.Warn("[FWBlastManager] 未找到 Blast 子物品");
                return;
            }

            var animator = blastChild.GetComponent<Animator>();
            if (animator == null)
            {
                Log.Warn("[FWBlastManager] Blast 子物品未找到 Animator 组件");
                return;
            }

            animator.runtimeAnimatorController = _blastAnimatorController;
            Log.Debug("[FWBlastManager] 已修复 Blast 的 Animator 控制器引用");
        }

        /// <summary>
        /// 从池中获取可用的 Bomb Blast
        /// </summary>
        public GameObject? GetAvailableBombBlast()
        {
            if (!_initialized)
            {
                Log.Warn("[FWBlastManager] 尚未初始化，无法获取 Bomb Blast");
                return null;
            }

            GameObject? obj = null;

            if (_bombBlastPool.Count > 0)
            {
                obj = _bombBlastPool[_bombBlastPool.Count - 1];
                _bombBlastPool.RemoveAt(_bombBlastPool.Count - 1);
            }
            else
            {
                obj = CreateBombBlastInstance();
                Log.Debug("[FWBlastManager] Bomb Blast 池为空，创建新实例");
            }

            return obj;
        }

        /// <summary>
        /// 在指定位置生成并触发 Bomb Blast 攻击
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="parent">父对象（可选）</param>
        /// <param name="size">爆炸大小（0=随机0.6-0.8，>0直接使用该值）</param>
        /// <param name="spawnSilkBallRing">是否生成丝球环</param>
        /// <param name="silkBallCount">丝球数量</param>
        /// <param name="initialOutwardSpeed">丝球初始向外速度</param>
        /// <param name="reverseAcceleration">反向加速度值</param>
        /// <param name="maxInwardSpeed">最大向内速度</param>
        /// <param name="reverseAccelDuration">反向加速度持续时间</param>
        /// <param name="useReverseAccelMode">是否使用反向加速度模式（false=径向爆发模式）</param>
        /// <param name="radialBurstSpeed">径向爆发速度</param>
        /// <param name="releaseDelay">释放前等待时间</param>
        /// <returns>Bomb Blast 实例</returns>
        public GameObject? SpawnBombBlast(Vector3 position, Transform? parent = null,
            float size = 0f, bool spawnSilkBallRing = false,
            int silkBallCount = 8, float initialOutwardSpeed = 12f,
            float reverseAcceleration = 25f, float maxInwardSpeed = 30f, float reverseAccelDuration = 5f,
            bool useReverseAccelMode = true, float radialBurstSpeed = 18f, float releaseDelay = 0.3f)
        {
            if (!_initialized)
            {
                Log.Warn("[FWBlastManager] 尚未初始化，无法生成 Bomb Blast");
                return null;
            }

            var obj = GetAvailableBombBlast();
            if (obj == null) return null;

            obj.transform.SetParent(parent);
            obj.transform.position = position;

            // 配置 BombBlastBehavior
            var behavior = obj.GetComponent<BombBlastBehavior>();
            if (behavior != null)
            {
                behavior.customSize = size;
                behavior.Configure(spawnSilkBallRing, silkBallCount,
                    initialOutwardSpeed, reverseAcceleration, maxInwardSpeed, reverseAccelDuration,
                    useReverseAccelMode, radialBurstSpeed, releaseDelay);
            }

            ActivateBombBlast(obj);

            // 下一帧发送 START_NORMAL 事件（等待 FSM 进入 Mode Select 状态）
            StartCoroutine(SendStartEventNextFrame(obj, "START_NORMAL"));

            Log.Debug($"[FWBlastManager] Bomb Blast 已生成 at {position}, size={size}, ring={spawnSilkBallRing}, reverseAccel={useReverseAccelMode}");
            return obj;
        }

        /// <summary>
        /// 在指定位置生成移动模式的 Bomb Blast（用于 BlastBurst3 汇聚爆炸）
        /// 爆炸会追踪目标移动，到达后触发爆炸
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="moveTarget">移动目标 Transform</param>
        /// <param name="moveAcceleration">移动加速度</param>
        /// <param name="moveMaxSpeed">移动最大速度</param>
        /// <param name="reachDistance">到达距离阈值</param>
        /// <param name="moveTimeout">移动超时时间</param>
        /// <param name="size">爆炸大小（0=随机，>0直接使用）</param>
        /// <returns>Bomb Blast 实例</returns>
        public GameObject? SpawnMovingBombBlast(
            Vector3 position,
            Transform moveTarget,
            float moveAcceleration = 30f,
            float moveMaxSpeed = 7f,
            float reachDistance = 2f,
            float moveTimeout = 5f,
            float size = 1f)
        {
            if (!_initialized)
            {
                Log.Warn("[FWBlastManager] 尚未初始化，无法生成移动模式 Bomb Blast");
                return null;
            }

            var obj = GetAvailableBombBlast();
            if (obj == null) return null;

            obj.transform.SetParent(null);
            obj.transform.position = position;

            // 配置 BombBlastBehavior
            var behavior = obj.GetComponent<BombBlastBehavior>();
            if (behavior != null)
            {
                // 设置大小和基础参数
                behavior.customSize = size;
                behavior.Configure(false);
                // 配置移动模式
                behavior.ConfigureMoveMode(moveTarget, moveAcceleration, moveMaxSpeed, reachDistance, moveTimeout);
            }

            ActivateBombBlast(obj);

            // 下一帧发送 START_MOVE 事件（等待 FSM 进入 Mode Select 状态）
            StartCoroutine(SendStartEventNextFrame(obj, "START_MOVE"));

            Log.Debug($"[FWBlastManager] 移动模式 Bomb Blast 已生成 at {position}, 目标={moveTarget?.name}, 速度={moveMaxSpeed}");
            return obj;
        }

        /// <summary>
        /// 等待 FSM 进入 Mode Select 状态后发送启动事件
        /// </summary>
        private IEnumerator SendStartEventNextFrame(GameObject obj, string eventName)
        {
            if (obj == null) yield break;

            var fsm = obj.LocateMyFSM("Control");
            if (fsm == null) yield break;

            // 等待 FSM 进入 Mode Select 状态（最多等待 10 帧）
            int maxWaitFrames = 10;
            int waitedFrames = 0;
            while (waitedFrames < maxWaitFrames)
            {
                yield return null;
                waitedFrames++;

                if (obj == null || !obj.activeInHierarchy) yield break;

                // 检查是否已进入 Mode Select 状态
                if (fsm.ActiveStateName == "Mode Select")
                {
                    fsm.SendEvent(eventName);
                    Log.Debug($"[FWBlastManager] FSM 已进入 Mode Select，发送事件: {eventName}");
                    yield break;
                }
            }

            // 超时后仍然尝试发送事件
            if (obj != null && obj.activeInHierarchy)
            {
                fsm.SendEvent(eventName);
                Log.Warn($"[FWBlastManager] 等待超时，强制发送事件: {eventName}, 当前状态: {fsm.ActiveStateName}");
            }
        }

        /// <summary>
        /// 回收 Bomb Blast 到池
        /// </summary>
        public void RecycleBombBlast(GameObject? obj)
        {
            if (obj == null || _poolContainer == null)
            {
                return;
            }

            var fsm = obj.LocateMyFSM("Control");
            if (fsm != null)
            {
                fsm.enabled = false;
            }

            // 重置 BombBlastBehavior 配置
            var behavior = obj.GetComponent<BombBlastBehavior>();
            if (behavior != null)
            {
                behavior.StopAllCoroutines();
                behavior.ResetConfig();
            }

            SetBombBlastPooled(obj);

            if (!obj.activeSelf)
            {
                obj.SetActive(true);
            }

            // 移回对象池容器
            obj.transform.SetParent(_poolContainer.transform);

            if (fsm != null)
            {
                fsm.Fsm.SetState("Start");
            }

            // 添加回池
            if (!_bombBlastPool.Contains(obj))
            {
                _bombBlastPool.Add(obj);
            }
            Log.Debug($"[FWBlastManager] Bomb Blast 已回收，池中数量: {_bombBlastPool.Count}");
        }

        /// <summary>
        /// 回收所有活跃的 Bomb Blast
        /// </summary>
        public void RecycleAllBombBlasts()
        {
            var activeObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in activeObjects)
            {
                if (obj.name.Contains("First Weaver Bomb Blast") && 
                    obj.activeInHierarchy && 
                    obj.transform.parent != _poolContainer?.transform)
                {
                    RecycleBombBlast(obj);
                }
            }
        }
        #endregion

        #region 清理
        public void CleanupPool()
        {
            Log.Info("[FWBlastManager] 开始清理对象池...");

            StopAllCoroutines();

            foreach (var obj in _bombBlastPool)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            _bombBlastPool.Clear();

            if (_poolContainer != null)
            {
                Destroy(_poolContainer);
                _poolContainer = null;
            }

            _bombBlastPrefab = null;
            _initialized = false;
            _initializing = false;

            Log.Info("[FWBlastManager] 对象池清理完成");
        }
        #endregion
    }
}
