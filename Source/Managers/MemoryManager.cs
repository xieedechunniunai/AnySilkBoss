using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;
using HutongGames.PlayMaker;
using TeamCherry.SharedUtils;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// Memory 模式管理器（简化版）
    /// 
    /// 简化逻辑：
    /// 1. 在 Cradle_03_Destroyed 弹琴 3 秒 → 保存返回位置，进入 Cradle_03
    /// 2. 在 Cradle_03 中：
    ///    - 死亡 → 正常复活（不拦截）
    ///    - 弹琴退出 → 返回 Cradle_03_Destroyed 指定位置
    ///    - 尝试切换场景 → 强制返回 Cradle_03_Destroyed 指定位置
    /// </summary>
    internal class MemoryManager : MonoBehaviour
    {
        public static MemoryManager? Instance { get; private set; }

        // Memory 模式状态
        public static bool IsInMemoryMode { get; private set; } = false;

        // 场景名称
        private const string TRIGGER_SCENE = "Cradle_03_Destroyed";
        private const string TARGET_SCENE = "Cradle_03";

        // 进入梦境后在 Cradle_03 的出生位置
        private const float SPAWN_POS_X = 40.13f;
        private const float SPAWN_POS_Y = 133.5677f;
        private const float SPAWN_POS_Z = 0.0f;

        // 返回 Cradle_03_Destroyed 的指定位置（弹琴位置附近）
        private const float RETURN_POS_X = 49.7f;
        private const float RETURN_POS_Y = 133.5677f;
        private const float RETURN_POS_Z = 0.0f;

        // 弹琴检测位置和范围
        private static readonly Vector3 TRIGGER_POSITION = new Vector3(49.7935f, 133.5621f, 0.004f);
        private const float TRIGGER_RANGE = 15f;

        // 音频检测相关
        private AudioSource? _needolinAudioSource;
        private float _audioPlayingTimer = 0f;
        private const float REQUIRED_PLAYING_TIME = 3f;
        private bool _isCheckingAudio = false;
        private bool _hasTriggeredThisSession = false;

        // 当前是否在触发场景
        private bool _isInTriggerScene = false;

        // 是否正在从梦境返回
        private bool _isReturningFromMemory = false;

        // 重生标记
        private const string MEMORY_RESPAWN_MARKER_NAME = "MemoryRespawnMarker";
        private GameObject? _memoryRespawnMarker;

        // 缓存被禁用的 TransitionPoint
        private List<TransitionPoint> _disabledTransitionPoints = new List<TransitionPoint>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(this);
                return;
            }

            SceneManager.activeSceneChanged += OnSceneChanged;
            Log.Info("[MemoryManager] 初始化完成（简化版）");
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            Log.Info($"[MemoryManager] 场景切换: {oldScene.name} → {newScene.name}");

            // ========== 进入 Cradle_03 ==========
            if (newScene.name == TARGET_SCENE)
            {
                if (IsInMemoryMode)
                {
                    Log.Info($"[MemoryManager] ====== 进入梦境 {TARGET_SCENE} ======");

                    // 禁用 TransitionPoint，防止自动切换场景
                    DisableAllTransitionPoints();

                    // 设置玩家位置并播放醒来动画
                    StartCoroutine(EnterMemorySceneRoutine());

                    // 启动弹琴检测（用于退出梦境）
                    _hasTriggeredThisSession = false;
                    StartCoroutine(SetupAudioDetection());
                }

                _isInTriggerScene = false;
                _audioPlayingTimer = 0f;
            }
            // ========== 进入 Cradle_03_Destroyed ==========
            else if (newScene.name == TRIGGER_SCENE)
            {
                Log.Info($"[MemoryManager] 进入触发场景: {TRIGGER_SCENE}");

                bool isReturning = _isReturningFromMemory || (oldScene.name == TARGET_SCENE && IsInMemoryMode);

                _isInTriggerScene = true;
                _hasTriggeredThisSession = false;
                _isCheckingAudio = false;
                _audioPlayingTimer = 0f;

                // 如果是从梦境返回
                if (isReturning)
                {
                    Log.Info("[MemoryManager] 从梦境返回，设置返回位置");
                    _isReturningFromMemory = false;
                    IsInMemoryMode = false;

                    if (GameManager._instance != null)
                    {
                        GameManager._instance.ForceCurrentSceneIsMemory(false);
                    }

                    // 禁用 TransitionPoint
                    DisableAllTransitionPoints();

                    // 设置返回位置
                    StartCoroutine(ReturnFromMemoryRoutine());
                }

                // 检查是否为第三章，启动弹琴检测
                if (CheckIsAct3())
                {
                    Log.Info("[MemoryManager] 确认为第三章，启动弹琴检测");
                    StartCoroutine(SetupAudioDetection());
                }
            }
            // ========== 从 Cradle_03 离开到其他场景 ==========
            else if (oldScene.name == TARGET_SCENE && IsInMemoryMode)
            {
                Log.Info($"[MemoryManager] 梦境中尝试离开到 {newScene.name}，强制返回 {TRIGGER_SCENE}");

                // 标记正在返回
                _isReturningFromMemory = true;

                // 强制返回
                StartCoroutine(ForceReturnToTriggerScene());
            }
            else
            {
                _isInTriggerScene = false;
                _isCheckingAudio = false;
                _audioPlayingTimer = 0f;
            }
        }

        private void Update()
        {
            // 在触发场景且非梦境模式：检测弹琴进入梦境
            if (_isInTriggerScene && !IsInMemoryMode && _isCheckingAudio && !_hasTriggeredThisSession)
            {
                if (IsPlayerInTriggerRange())
                {
                    CheckAudioForEnterMemory();
                }
                else
                {
                    _audioPlayingTimer = 0f;
                }
            }

            // 在梦境中：检测弹琴退出梦境
            if (IsInMemoryMode && _isCheckingAudio && !_hasTriggeredThisSession)
            {
                CheckAudioForExitMemory();
            }
        }

        #region 进入梦境

        /// <summary>
        /// 检测弹琴进入梦境
        /// </summary>
        private void CheckAudioForEnterMemory()
        {
            if (_needolinAudioSource == null)
            {
                StartCoroutine(SetupAudioDetection());
                return;
            }

            if (_needolinAudioSource.isPlaying)
            {
                _audioPlayingTimer += Time.deltaTime;

                if (_audioPlayingTimer >= REQUIRED_PLAYING_TIME)
                {
                    Log.Info($"[MemoryManager] 弹琴触发进入梦境！");
                    _hasTriggeredThisSession = true;
                    _isCheckingAudio = false;

                    ForceDisablePlayerControl();
                    StartCoroutine(EnterMemoryMode());
                }
            }
            else
            {
                _audioPlayingTimer = 0f;
            }
        }

        /// <summary>
        /// 进入梦境模式
        /// </summary>
        private IEnumerator EnterMemoryMode()
        {
            Log.Info("[MemoryManager] 开始进入梦境...");

            IsInMemoryMode = true;

            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("[MemoryManager] HeroController.instance 为空！");
                IsInMemoryMode = false;
                yield break;
            }

            // 停止动画控制，准备播放自定义动画序列
            hero.StopAnimationControl();
            var animCtrl = hero.AnimCtrl;
            var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();

            if (tk2dAnimator != null && animCtrl != null)
            {
                // 1. 先播放跪下动画（从站立到跪下）
                // 尝试使用 "Abyss Kneel" 或类似的动画，如果没有则使用 FSM 状态
                tk2dAnimator.Play("Abyss Kneel");
                // 2. 等待跪下动画完成（如果存在）
                yield return new WaitForSeconds(0.8f);
                // 3. 播放从跪到躺下的动画
                tk2dAnimator.Play("Kneel To Prostrate");
                yield return new WaitForSeconds(1f);

            }
            else
            {
                yield return new WaitForSeconds(1.8f);
            }

            // 设置为梦境场景
            if (GameManager._instance != null)
            {
                GameManager._instance.ForceCurrentSceneIsMemory(true);
            }

            // 传送到梦境场景
            try
            {
                GameManager._instance?.BeginSceneTransition(new GameManager.SceneLoadInfo
                {
                    SceneName = TARGET_SCENE,
                    EntryGateName = "",
                    HeroLeaveDirection = new GlobalEnums.GatePosition?(GlobalEnums.GatePosition.unknown),
                    EntryDelay = 0f,
                    WaitForSceneTransitionCameraFade = true,
                    Visualization = GameManager.SceneLoadVisualizations.Default,
                    AlwaysUnloadUnusedAssets = false
                });
                Log.Info("[MemoryManager] 场景传送已触发");
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 场景传送失败: {ex.Message}");
                IsInMemoryMode = false;
                ForceEnablePlayerControl();
            }
        }

        /// <summary>
        /// 进入梦境场景后的处理
        /// </summary>
        private IEnumerator EnterMemorySceneRoutine()
        {
            // 等待场景加载
            yield return new WaitForSeconds(0.5f);

            var hero = HeroController.instance;
            if (hero == null || GameManager._instance == null)
            {
                Log.Error("[MemoryManager] HeroController 或 GameManager 为空！");
                yield break;
            }

            // 创建临时重生标记
            CreateMemoryRespawnMarker(SPAWN_POS_X, SPAWN_POS_Y, SPAWN_POS_Z);

            // 设置重生信息
            PlayerData.instance.respawnScene = TARGET_SCENE;
            PlayerData.instance.respawnMarkerName = MEMORY_RESPAWN_MARKER_NAME;
            PlayerData.instance.respawnType = 0;

            // 淡入
            FadeSceneIn();

            // 播放醒来动画
            yield return hero.StartCoroutine(hero.Respawn(_memoryRespawnMarker?.transform));
            Log.Info("[MemoryManager] 进入梦境完成");

            // 不清理标记，保留作为梦境中的固定复活点
            // CleanupMemoryRespawnMarker();

            // 恢复控制
            ForceEnablePlayerControl();

            // 恢复 TransitionPoint
            yield return null;
            EnableAllTransitionPoints();
        }

        #endregion

        #region 退出梦境

        /// <summary>
        /// 检测弹琴退出梦境
        /// </summary>
        private void CheckAudioForExitMemory()
        {
            if (_needolinAudioSource == null)
            {
                StartCoroutine(SetupAudioDetection());
                return;
            }

            if (_needolinAudioSource.isPlaying)
            {
                _audioPlayingTimer += Time.deltaTime;

                if (_audioPlayingTimer >= REQUIRED_PLAYING_TIME)
                {
                    Log.Info($"[MemoryManager] 弹琴触发退出梦境！");
                    _hasTriggeredThisSession = true;
                    _isCheckingAudio = false;
                    StartCoroutine(ExitMemoryByPlaying());
                }
            }
            else
            {
                _audioPlayingTimer = 0f;
            }
        }

        /// <summary>
        /// 弹琴退出梦境
        /// </summary>
        private IEnumerator ExitMemoryByPlaying()
        {
            Log.Info("[MemoryManager] 开始退出梦境...");

            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("[MemoryManager] HeroController.instance 为空！");
                yield break;
            }

            ForceDisablePlayerControl();

            // 停止动画控制，准备播放自定义动画序列
            hero.StopAnimationControl();
            var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();

            if (tk2dAnimator != null)
            {  
                tk2dAnimator.Play("Abyss Kneel");
                // 2. 等待跪下动画完成（如果存在）
                yield return new WaitForSeconds(0.8f);
                // 3. 播放从跪到躺下的动画
                tk2dAnimator.Play("Kneel To Prostrate");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                yield return new WaitForSeconds(1.8f);
            }

            // 设置返回标志
            _isReturningFromMemory = true;

            // 传送回触发场景
            try
            {
                if (GameManager._instance != null)
                {
                    GameManager._instance.ForceCurrentSceneIsMemory(false);
                }

                GameManager._instance?.BeginSceneTransition(new GameManager.SceneLoadInfo
                {
                    SceneName = TRIGGER_SCENE,
                    EntryGateName = "",
                    HeroLeaveDirection = new GlobalEnums.GatePosition?(GlobalEnums.GatePosition.unknown),
                    EntryDelay = 0f,
                    WaitForSceneTransitionCameraFade = true,
                    Visualization = GameManager.SceneLoadVisualizations.Default,
                    AlwaysUnloadUnusedAssets = false
                });
                Log.Info("[MemoryManager] 退出梦境传送已触发");
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 退出梦境失败: {ex.Message}");
                _isReturningFromMemory = false;
            }
        }

        /// <summary>
        /// 强制返回触发场景（当玩家尝试通过其他方式离开时）
        /// </summary>
        private IEnumerator ForceReturnToTriggerScene()
        {
            yield return new WaitForSeconds(0.1f);

            ForceDisablePlayerControl();

            Log.Info($"[MemoryManager] 强制返回 {TRIGGER_SCENE}");

            try
            {
                if (GameManager._instance != null)
                {
                    GameManager._instance.ForceCurrentSceneIsMemory(false);
                }

                GameManager._instance?.BeginSceneTransition(new GameManager.SceneLoadInfo
                {
                    SceneName = TRIGGER_SCENE,
                    EntryGateName = "",
                    HeroLeaveDirection = new GlobalEnums.GatePosition?(GlobalEnums.GatePosition.unknown),
                    EntryDelay = 0f,
                    WaitForSceneTransitionCameraFade = true,
                    Visualization = GameManager.SceneLoadVisualizations.Default,
                    AlwaysUnloadUnusedAssets = false
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 强制返回失败: {ex.Message}");
                _isReturningFromMemory = false;
            }
        }

        /// <summary>
        /// 从梦境返回后的处理
        /// </summary>
        private IEnumerator ReturnFromMemoryRoutine()
        {
            // 等待 HeroController 可用
            float waitTime = 0f;
            while ((HeroController.instance == null || GameManager._instance == null) && waitTime < 5f)
            {
                yield return new WaitForSeconds(0.1f);
                waitTime += 0.1f;
            }

            var hero = HeroController.instance;
            if (hero == null || GameManager._instance == null)
            {
                Log.Error("[MemoryManager] HeroController 或 GameManager 为空！");
                yield break;
            }

            // 立即移动玩家到返回位置
            hero.transform.position = new Vector3(RETURN_POS_X, RETURN_POS_Y, RETURN_POS_Z);
            var rb2d = hero.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
            }
            Log.Info($"[MemoryManager] 已移动玩家到返回位置: ({RETURN_POS_X}, {RETURN_POS_Y})");

            yield return new WaitForSeconds(0.3f);

            // 创建重生标记
            CreateMemoryRespawnMarker(RETURN_POS_X, RETURN_POS_Y, RETURN_POS_Z);

            // 设置重生信息
            PlayerData.instance.respawnScene = TRIGGER_SCENE;
            PlayerData.instance.respawnMarkerName = MEMORY_RESPAWN_MARKER_NAME;
            PlayerData.instance.respawnType = 0;

            // 淡入
            FadeSceneIn();

            // 播放醒来动画
            if (_memoryRespawnMarker != null)
            {
                yield return hero.StartCoroutine(hero.Respawn(_memoryRespawnMarker.transform));
            }
            Log.Info("[MemoryManager] 返回原场景完成");

            // 清理
            CleanupMemoryRespawnMarker();
            ForceEnablePlayerControl();

            yield return null;
            EnableAllTransitionPoints();
        }

        #endregion

        #region 辅助方法

        private bool CheckIsAct3()
        {
            try
            {
                if (GameManager.instance == null || PlayerData.instance == null) return false;
                var saveData = GameManager.instance.GetSaveGameData(PlayerData.instance.profileID);
                var saveStats = GameManager.GetSaveStatsFromData(saveData);
                return saveStats.IsAct3;
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 检测第三章失败: {ex.Message}");
                return false;
            }
        }

        private bool IsPlayerInTriggerRange()
        {
            if (HeroController.instance == null) return false;
            Vector3 playerPos = HeroController.instance.transform.position;
            float distance = Vector3.Distance(playerPos, TRIGGER_POSITION);
            return distance <= TRIGGER_RANGE;
        }

        private IEnumerator SetupAudioDetection()
        {
            yield return new WaitForSeconds(1f);

            try
            {
                if (HeroController.instance == null) yield break;

                GameObject heroObject = HeroController.instance.gameObject;
                Transform soundsTransform = heroObject.transform.Find("Sounds");
                if (soundsTransform == null) yield break;

                Transform needolinTransform = soundsTransform.Find("Needolin Memory");
                if (needolinTransform == null) yield break;

                _needolinAudioSource = needolinTransform.GetComponent<AudioSource>();
                if (_needolinAudioSource == null) yield break;

                Log.Info("[MemoryManager] 音频检测设置成功");
                _isCheckingAudio = true;
                _audioPlayingTimer = 0f;
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 设置音频检测失败: {ex.Message}");
                _isCheckingAudio = false;
            }
        }

        private void CreateMemoryRespawnMarker(float x, float y, float z)
        {
            try
            {
                CleanupMemoryRespawnMarker();

                _memoryRespawnMarker = new GameObject(MEMORY_RESPAWN_MARKER_NAME);
                _memoryRespawnMarker.transform.position = new Vector3(x, y, z);
                _memoryRespawnMarker.transform.rotation = Quaternion.identity;

                var markerComponent = _memoryRespawnMarker.AddComponent<RespawnMarker>();
                markerComponent.respawnFacingRight = true;
                markerComponent.customWakeUp = false;
                markerComponent.customFadeDuration = new OverrideFloat { IsEnabled = false, Value = 0f };

                Log.Info($"[MemoryManager] 创建重生标记: ({x}, {y}, {z})");
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 创建重生标记失败: {ex.Message}");
            }
        }

        private void CleanupMemoryRespawnMarker()
        {
            if (_memoryRespawnMarker != null)
            {
                UnityEngine.Object.Destroy(_memoryRespawnMarker);
                _memoryRespawnMarker = null;
            }
        }

        private void FadeSceneIn()
        {
            try
            {
                GameManager._instance?.screenFader_fsm?.SendEvent("SCENE FADE IN");
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 屏幕淡入失败: {ex.Message}");
            }
        }

        private void ForceDisablePlayerControl()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            GameManager._instance?.inputHandler?.StopAcceptingInput();
            hero.RelinquishControl();
            hero.StopAnimationControl();
            CancelSilkSpecialsFSM(hero);
        }

        private void ForceEnablePlayerControl()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            GameManager._instance?.inputHandler?.StartAcceptingInput();
            hero.RegainControl();
            hero.StartAnimationControl();
        }

        private void CancelSilkSpecialsFSM(HeroController hero)
        {
            try
            {
                var fsmComponents = hero.GetComponentsInChildren<PlayMakerFSM>(true);
                foreach (var fsm in fsmComponents)
                {
                    if (fsm.FsmName == "Silk Specials")
                    {
                        fsm.SendEvent("CANCEL");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryManager] 取消 Silk Specials FSM 失败: {ex.Message}");
            }
        }

        private void DisableAllTransitionPoints()
        {
            _disabledTransitionPoints.Clear();
            var transitionPoints = UnityEngine.Object.FindObjectsByType<TransitionPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tp in transitionPoints)
            {
                if (tp.gameObject.activeSelf)
                {
                    _disabledTransitionPoints.Add(tp);
                    tp.gameObject.SetActive(false);
                }
            }
            Log.Info($"[MemoryManager] 已禁用 {_disabledTransitionPoints.Count} 个 TransitionPoint");
        }

        private void EnableAllTransitionPoints()
        {
            foreach (var tp in _disabledTransitionPoints)
            {
                if (tp != null) tp.gameObject.SetActive(true);
            }
            Log.Info($"[MemoryManager] 已启用 {_disabledTransitionPoints.Count} 个 TransitionPoint");
            _disabledTransitionPoints.Clear();
        }

        #endregion

        #region 公开方法

        public static bool CheckIsMemoryMode() => IsInMemoryMode;

        public static void ExitMemoryMode()
        {
            if (IsInMemoryMode)
            {
                Log.Info("[MemoryManager] 手动退出 Memory 模式");
                IsInMemoryMode = false;
                GameManager._instance?.ForceCurrentSceneIsMemory(false);
            }
        }

        /// <summary>
        /// 触发退出梦境（由 BossPatches 调用，拦截场景切换时触发）
        /// </summary>
        public void TriggerExitMemory()
        {
            if (!IsInMemoryMode) return;

            Log.Info("[MemoryManager] 触发退出梦境（场景切换拦截）");
            _isReturningFromMemory = true;
            StartCoroutine(ForceReturnToTriggerScene());
        }

        /// <summary>
        /// 获取梦境重生标记的 Transform（供 DeathManager 使用）
        /// </summary>
        public Transform? GetRespawnMarkerTransform()
        {
            return _memoryRespawnMarker?.transform;
        }

        #endregion
    }
}
