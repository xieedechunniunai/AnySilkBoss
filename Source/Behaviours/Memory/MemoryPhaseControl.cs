using System;
using System.Collections;
using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// [梦境版] Boss 阶段控制器
    /// 用于 Memory 模式下的 Boss 战斗阶段管理
    /// 不使用普通版的加强逻辑
    /// </summary>
    internal class MemoryPhaseControl : MonoBehaviour
    {
        [Header("Boss 引用")]
        private GameObject? _bossObject;
        private HealthManager? _healthManager;
        private PlayMakerFSM? _bossControlFsm;
        
        [Header("阶段配置")]
        private int _currentPhase = 1;
        private const int MAX_PHASE = 3;
        
        // 阶段血量阈值（百分比）
        private const float PHASE_2_THRESHOLD = 0.66f;  // 66% 血量进入第二阶段
        private const float PHASE_3_THRESHOLD = 0.33f;  // 33% 血量进入第三阶段
        
        private int _maxHealth = 0;
        private bool _isInitialized = false;
        
        // 事件
        public event Action<int>? OnPhaseChanged;

        private void Awake()
        {
            Log.Info("[MemoryPhaseControl] Awake");
        }

        private void Start()
        {
            StartCoroutine(DelayedInitialize());
        }

        private IEnumerator DelayedInitialize()
        {
            yield return new WaitForSeconds(1f);
            
            Initialize();
        }

        /// <summary>
        /// 初始化阶段控制器
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                // 查找 Boss 对象
                _bossObject = GameObject.Find("Silk Boss");
                if (_bossObject == null)
                {
                    Log.Warn("[MemoryPhaseControl] 未找到 Silk Boss 对象");
                    return;
                }
                
                Log.Info($"[MemoryPhaseControl] 找到 Boss 对象: {_bossObject.name}");
                
                // 获取 HealthManager
                _healthManager = _bossObject.GetComponent<HealthManager>();
                if (_healthManager == null)
                {
                    Log.Warn("[MemoryPhaseControl] Boss 没有 HealthManager 组件");
                    return;
                }
                
                _maxHealth = _healthManager.hp;
                Log.Info($"[MemoryPhaseControl] Boss 最大血量: {_maxHealth}");
                
                // 获取 Boss Control FSM
                var fsms = _bossObject.GetComponents<PlayMakerFSM>();
                foreach (var fsm in fsms)
                {
                    if (fsm.FsmName == "Boss Control")
                    {
                        _bossControlFsm = fsm;
                        break;
                    }
                }
                
                if (_bossControlFsm != null)
                {
                    Log.Info("[MemoryPhaseControl] 找到 Boss Control FSM");
                }
                
                _isInitialized = true;
                _currentPhase = 1;
                
                Log.Info("[MemoryPhaseControl] 初始化完成，当前阶段: 1");
            }
            catch (Exception ex)
            {
                Log.Error($"[MemoryPhaseControl] 初始化失败: {ex.Message}");
            }
        }

        private void Update()
        {
            if (!_isInitialized || _healthManager == null) return;
            
            // 检测阶段变化
            CheckPhaseTransition();
        }

        /// <summary>
        /// 检测阶段转换
        /// </summary>
        private void CheckPhaseTransition()
        {
            if (_healthManager == null || _maxHealth <= 0) return;
            
            float healthPercent = (float)_healthManager.hp / _maxHealth;
            int newPhase = CalculatePhase(healthPercent);
            
            if (newPhase != _currentPhase)
            {
                int oldPhase = _currentPhase;
                _currentPhase = newPhase;
                
                Log.Info($"[MemoryPhaseControl] ====== 阶段转换: {oldPhase} -> {newPhase} (血量: {healthPercent:P0}) ======");
                
                OnPhaseChanged?.Invoke(_currentPhase);
                HandlePhaseChange(_currentPhase);
            }
        }

        /// <summary>
        /// 根据血量百分比计算当前阶段
        /// </summary>
        private int CalculatePhase(float healthPercent)
        {
            if (healthPercent <= PHASE_3_THRESHOLD)
                return 3;
            if (healthPercent <= PHASE_2_THRESHOLD)
                return 2;
            return 1;
        }

        /// <summary>
        /// 处理阶段变化
        /// </summary>
        private void HandlePhaseChange(int newPhase)
        {
            switch (newPhase)
            {
                case 2:
                    Log.Info("[MemoryPhaseControl] 进入第二阶段 - 可在此添加增强逻辑");
                    // TODO: 第二阶段增强逻辑
                    break;
                    
                case 3:
                    Log.Info("[MemoryPhaseControl] 进入第三阶段 - 可在此添加增强逻辑");
                    // TODO: 第三阶段增强逻辑
                    break;
            }
        }

        /// <summary>
        /// 获取当前阶段
        /// </summary>
        public int GetCurrentPhase()
        {
            return _currentPhase;
        }

        /// <summary>
        /// 获取 Boss 当前血量百分比
        /// </summary>
        public float GetHealthPercent()
        {
            if (_healthManager == null || _maxHealth <= 0) return 1f;
            return (float)_healthManager.hp / _maxHealth;
        }

        /// <summary>
        /// 检查是否在 Memory 模式
        /// </summary>
        public bool IsMemoryMode()
        {
            return MemoryManager.IsInMemoryMode;
        }

        private void OnDestroy()
        {
            Log.Info("[MemoryPhaseControl] 销毁");
            _isInitialized = false;
        }
    }
}
