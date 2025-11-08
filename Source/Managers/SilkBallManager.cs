using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 丝球管理器 - 负责创建和管理自定义丝球预制体
    /// 作为普通组件挂载到 AnySilkBossManager 上
    /// </summary>
    internal class SilkBallManager : MonoBehaviour
    {
        #region Fields
        private GameObject? _customSilkBallPrefab;
        public GameObject? CustomSilkBallPrefab => _customSilkBallPrefab;

        private AssetManager? _assetManager;
        private bool _initialized = false;

        // 从 Boss 的 DashSlash Effect 缓存的 DamageHero 组件
        private DamageHero? _originalDamageHero;
        public DamageHero? OriginalDamageHero => _originalDamageHero;

        // 音效参数缓存
        private FsmObject? _initAudioTable;
        private FsmObject? _initAudioPlayerPrefab;
        private FsmObject? _getSilkAudioTable;
        private FsmObject? _getSilkAudioPlayerPrefab;

        public FsmObject? InitAudioTable => _initAudioTable;
        public FsmObject? InitAudioPlayerPrefab => _initAudioPlayerPrefab;
        public FsmObject? GetSilkAudioTable => _getSilkAudioTable;
        public FsmObject? GetSilkAudioPlayerPrefab => _getSilkAudioPlayerPrefab;
        #endregion

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        #region Initialization
        /// <summary>
        /// 初始化丝球管理器
        /// </summary>
        private IEnumerator Initialize()
        {
            if (_initialized)
            {
                Log.Info("SilkBallManager already initialized.");
                yield break;
            }

            Log.Info("Starting SilkBallManager initialization...");

            // 获取同一 GameObject 上的 AssetManager 组件
            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("无法找到 AssetManager 组件");
                yield break;
            }

            // 等待 AssetManager 初始化完成
            while (!_assetManager.IsInitialized())
            {
                yield return new WaitForSeconds(0.1f);
            }

            // 创建自定义丝球预制体
            yield return CreateCustomSilkBallPrefab();

            _initialized = true;
            Log.Info("SilkBallManager initialization completed.");
        }

        /// <summary>
        /// 由 BossBehavior 调用，设置 DamageHero 组件
        /// </summary>
        public void SetDamageHeroComponent(DamageHero damageHero)
        {
            if (_originalDamageHero != null)
            {
                Log.Info("DamageHero 组件已存在，跳过设置");
                return;
            }

            _originalDamageHero = damageHero;
            Log.Info($"成功设置 DamageHero 组件，监听器数量: {damageHero?.OnDamagedHero?.GetPersistentEventCount() ?? 0}");
        }

        /// <summary>
        /// 检查 DamageHero 组件是否已设置
        /// </summary>
        public bool HasDamageHeroEvent()
        {
            return _originalDamageHero != null;
        }

        /// <summary>
        /// 创建自定义丝球预制体
        /// </summary>
        private IEnumerator CreateCustomSilkBallPrefab()
        {
            Log.Info("=== 开始创建自定义丝球预制体 ===");

            // 获取原版丝球预制体
            var originalPrefab = _assetManager?.Get<GameObject>("Reaper Silk Bundle");
            if (originalPrefab == null)
            {
                Log.Error("无法获取原版丝球预制体 'Reaper Silk Bundle'");
                yield break;
            }

            Log.Info($"成功获取原版丝球预制体: {originalPrefab.name}");
            LogOriginalPrefabStructure(originalPrefab);

            // 复制一份预制体
            _customSilkBallPrefab = Object.Instantiate(originalPrefab);
            _customSilkBallPrefab.name = "Custom Silk Ball Prefab";

            // 永久保存，不随场景销毁
            DontDestroyOnLoad(_customSilkBallPrefab);

            // 禁用该对象（作为预制体模板）
            _customSilkBallPrefab.SetActive(false);

            Log.Info("丝球预制体复制完成，开始处理组件...");

            // 处理根物体组件
            ProcessRootComponents();

            // 处理子物体
            ProcessChildObjects();

            // 提取音效动作（必须在删除原版 FSM 之前）
            ExtractAudioActions();

            // 删除原版 PlayMakerFSM 组件
            RemoveOriginalFSM();

            // 添加 SilkBallBehavior 组件
            var silkBallBehavior = _customSilkBallPrefab.AddComponent<SilkBallBehavior>();
            if (silkBallBehavior != null)
            {
                Log.Info("成功添加 SilkBallBehavior 组件");
            }

            Log.Info($"=== 自定义丝球预制体创建完成: {_customSilkBallPrefab.name} ===");
            yield return null;
        }

        /// <summary>
        /// 记录原版预制体结构
        /// </summary>
        private void LogOriginalPrefabStructure(GameObject prefab)
        {
            Log.Info("--- 原版预制体结构分析 ---");

            // 根物体组件
            Log.Info($"根物体: {prefab.name}");
            var rootComponents = prefab.GetComponents<Component>();
            foreach (var comp in rootComponents)
            {
                Log.Info($"  组件: {comp.GetType().Name}");
            }

            // 子物体
            Log.Info("子物体列表:");
            foreach (Transform child in prefab.transform)
            {
                Log.Info($"  子物体: {child.name}");
                var childComponents = child.GetComponents<Component>();
                foreach (var comp in childComponents)
                {
                    Log.Info($"    组件: {comp.GetType().Name}");
                }
            }
        }

        /// <summary>
        /// 处理根物体组件
        /// </summary>
        private void ProcessRootComponents()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 处理根物体组件 ---");

            // 保留 Rigidbody2D（会在 SilkBallBehavior 中重新配置）
            var rb2d = _customSilkBallPrefab.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                Log.Info($"保留 Rigidbody2D: gravityScale={rb2d.gravityScale}, linearDamping={rb2d.linearDamping}");
            }

            // 移除不需要的组件
            // ObjectBounce - 原版的弹跳逻辑，我们不需要
            var objectBounce = _customSilkBallPrefab.GetComponent<ObjectBounce>();
            if (objectBounce != null)
            {
                Object.Destroy(objectBounce);
                Log.Info("移除 ObjectBounce 组件");
            }

            // SetZ - 保持Z轴位置，可以保留
            var setZ = _customSilkBallPrefab.GetComponent<SetZ>();
            if (setZ != null)
            {
                Log.Info("保留 SetZ 组件");
            }

            // AutoRecycleSelf - 自动回收，我们用自己的销毁逻辑
            var autoRecycle = _customSilkBallPrefab.GetComponent<AutoRecycleSelf>();
            if (autoRecycle != null)
            {
                Object.Destroy(autoRecycle);
                Log.Info("移除 AutoRecycleSelf 组件");
            }

            // EventRegister - 事件注册，可以移除
            var eventRegisters = _customSilkBallPrefab.GetComponents<EventRegister>();
            foreach (var er in eventRegisters)
            {
                Object.Destroy(er);
                Log.Info($"移除 EventRegister 组件: {er.subscribedEvent}");
            }

            // bounceOnWater - 水面弹跳，可以移除
            var bounceOnWater = _customSilkBallPrefab.GetComponent("bounceOnWater");
            if (bounceOnWater != null)
            {
                Object.Destroy(bounceOnWater);
                Log.Info("移除 bounceOnWater 组件");
            }
        }

        /// <summary>
        /// 处理子物体
        /// </summary>
        private void ProcessChildObjects()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 处理子物体 ---");

            // 重要子物体：Sprite Silk（主要的可视化部分）
            Transform spriteSilk = _customSilkBallPrefab.transform.Find("Sprite Silk");
            if (spriteSilk != null)
            {
                Log.Info("找到 Sprite Silk 子物体，保留所有组件");
                // 保留所有组件：MeshFilter, MeshRenderer, tk2dSprite, tk2dSpriteAnimator, 
                // RandomRotation, RandomScale, CircleCollider2D, AmbientSway, NonBouncer

                // 获取 CircleCollider2D 用于伤害检测
                var circleCollider = spriteSilk.GetComponent<CircleCollider2D>();
                if (circleCollider != null)
                {
                    Log.Info($"找到 CircleCollider2D: radius={circleCollider.radius}, isTrigger={circleCollider.isTrigger}");
                    // 确保是触发器
                    circleCollider.isTrigger = true;
                }
            }

            // 粒子特效子物体（用于消散/消失）
            Transform ptCollect = _customSilkBallPrefab.transform.Find("Pt Collect");
            if (ptCollect != null)
            {
                Log.Info("找到 Pt Collect 子物体（快速消散特效），保留");
            }

            Transform ptDisappear = _customSilkBallPrefab.transform.Find("Pt Disappear");
            if (ptDisappear != null)
            {
                Log.Info("找到 Pt Disappear 子物体（缓慢消失特效），保留");
            }

            Transform ptBreak = _customSilkBallPrefab.transform.Find("Pt Break");
            if (ptBreak != null)
            {
                Log.Info("找到 Pt Break 子物体（破碎特效），保留");
            }

            // 其他视觉效果
            Transform spriteCollect = _customSilkBallPrefab.transform.Find("Sprite Collect");
            if (spriteCollect != null)
            {
                Log.Info("找到 Sprite Collect 子物体，保留");
            }

            Transform slashEffect = _customSilkBallPrefab.transform.Find("Slash Effect");
            if (slashEffect != null)
            {
                Log.Info("找到 Slash Effect 子物体，保留");
            }

            Transform glow = _customSilkBallPrefab.transform.Find("Glow");
            if (glow != null)
            {
                Log.Info("找到 Glow 子物体，保留");
            }

            // Terrain Collider - 地形碰撞，可以移除或禁用
            Transform terrainCollider = _customSilkBallPrefab.transform.Find("Terrain Collider");
            if (terrainCollider != null)
            {
                terrainCollider.gameObject.SetActive(false);
                Log.Info("禁用 Terrain Collider 子物体（使用 Sprite Silk 的碰撞器）");
            }
        }

        /// <summary>
        /// 提取音效参数
        /// </summary>
        private void ExtractAudioActions()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 提取音效参数 ---");

            // 获取原版 Control FSM
            var controlFsm = _customSilkBallPrefab.GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");

            if (controlFsm == null)
            {
                Log.Warn("未找到原版 Control FSM，跳过音效提取");
                return;
            }

            // 提取 Init 状态的 PlayRandomAudioClipTable 参数
            ExtractPlayRandomAudioClipTableParams(controlFsm, "Init", 
                out _initAudioTable, out _initAudioPlayerPrefab);

            // 提取 Get Silk 状态的 PlayRandomAudioClipTable 参数
            ExtractPlayRandomAudioClipTableParams(controlFsm, "Get Silk", 
                out _getSilkAudioTable, out _getSilkAudioPlayerPrefab);
        }

        /// <summary>
        /// 从 PlayRandomAudioClipTable 动作中提取参数
        /// </summary>
        private void ExtractPlayRandomAudioClipTableParams(PlayMakerFSM fsm, string stateName, 
            out FsmObject? table, out FsmObject? audioPlayerPrefab)
        {
            table = null;
            audioPlayerPrefab = null;

            var state = fsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
            if (state == null)
            {
                Log.Warn($"未找到状态: {stateName}");
                return;
            }

            // 查找 PlayRandomAudioClipTable 动作
            var audioAction = state.Actions.FirstOrDefault(a => a.GetType().Name == "PlayRandomAudioClipTable");
            if (audioAction == null)
            {
                Log.Warn($"在状态 {stateName} 中未找到 PlayRandomAudioClipTable 动作");
                return;
            }

            // 通过反射获取字段
            var type = audioAction.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var tableField = type.GetField("Table", flags);
            var audioPrefabField = type.GetField("AudioPlayerPrefab", flags);

            if (tableField != null)
            {
                table = tableField.GetValue(audioAction) as FsmObject;
                Log.Info($"成功提取 {stateName} 的 Table 参数");
            }

            if (audioPrefabField != null)
            {
                audioPlayerPrefab = audioPrefabField.GetValue(audioAction) as FsmObject;
                Log.Info($"成功提取 {stateName} 的 AudioPlayerPrefab 参数");
            }
        }


        /// <summary>
        /// 移除原版 FSM
        /// </summary>
        private void RemoveOriginalFSM()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 移除原版 FSM ---");

            var oldFSMs = _customSilkBallPrefab.GetComponents<PlayMakerFSM>();
            foreach (var fsm in oldFSMs)
            {
                Log.Info($"移除原版 FSM: {fsm.FsmName}");
                Object.Destroy(fsm);
            }
        }
        #endregion

        /// <summary>
        /// 实例化一个丝球（基础版本）
        /// </summary>
        public GameObject? SpawnSilkBall(Vector3 position)
        {
            return SpawnSilkBall(position, 30f, 20f, 6f, 1f);
        }

        /// <summary>
        /// 实例化一个丝球（完整参数版本）
        /// </summary>
        public GameObject? SpawnSilkBall(Vector3 position, float acceleration, float maxSpeed, float chaseTime, float scale)
        {
            if (_customSilkBallPrefab == null)
            {
                Log.Error("自定义丝球预制体未初始化");
                return null;
            }

            var silkBall = Object.Instantiate(_customSilkBallPrefab);
            silkBall.transform.position = position;
            silkBall.SetActive(true);

            Log.Info($"实例化丝球到位置: {position}, 参数: acc={acceleration}, maxSpd={maxSpeed}, chase={chaseTime}, scale={scale}");
            return silkBall;
        }

        /// <summary>
        /// 清理场景中所有活跃的丝球（不删除预制体）
        /// </summary>
        public void DestroyAllActiveSilkBalls()
        {
            if (_customSilkBallPrefab == null)
            {
                Log.Warn("自定义丝球预制体未初始化，无法清理");
                return;
            }

            // 查找所有活跃的丝球实例（通过SilkBallBehavior组件识别）
            var allSilkBalls = FindObjectsByType<SilkBallBehavior>(FindObjectsSortMode.None);
            int destroyedCount = 0;

            foreach (var silkBallBehavior in allSilkBalls)
            {
                if (silkBallBehavior != null && silkBallBehavior.gameObject != null)
                {
                    // 确保不是预制体本身
                    if (silkBallBehavior.gameObject != _customSilkBallPrefab)
                    {
                        Object.Destroy(silkBallBehavior.gameObject);
                        destroyedCount++;
                    }
                }
            }

            Log.Info($"已清理场景中的丝球实例，共销毁 {destroyedCount} 个");
        }
    }
}
