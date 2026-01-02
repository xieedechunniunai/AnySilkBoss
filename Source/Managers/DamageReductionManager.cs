using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 动态减伤管理器 - 负责记录BOSS受到的伤害并计算动态减伤
    /// 机制：总减伤层数 = 基础减伤层数 + 累计伤害层数
    /// 每层提供10%减伤，最高90%
    /// </summary>
    internal class DamageReductionManager : MonoBehaviour
    {
        #region 常量配置
        
        /// <summary>
        /// 伤害记录的有效时间（秒）
        /// </summary>
        private const float DAMAGE_RECORD_LIFETIME = 10f;
        
        /// <summary>
        /// 每多少伤害获得一次减免
        /// </summary>
        private const int DAMAGE_THRESHOLD = 100;
        
        /// <summary>
        /// 每个阈值提供的减伤百分比（0.1 = 10%）
        /// </summary>
        private const float REDUCTION_PER_THRESHOLD = 0.1f;
        
        /// <summary>
        /// 最大减伤百分比（0.9 = 90%）
        /// </summary>
        private const float MAX_REDUCTION = 0.9f;
        
        #endregion

        #region 基础减伤层数（状态驱动）
        
        /// <summary>
        /// 基础减伤层数（由BOSS状态决定）
        /// </summary>
        private int _baseReductionLevel = 0;
        
        /// <summary>
        /// 获取当前的基础减伤层数
        /// </summary>
        public int BaseReductionLevel => _baseReductionLevel;
        
        /// <summary>
        /// 获取总减伤层数（基础 + 累计）
        /// </summary>
        public int TotalReductionLevel => _baseReductionLevel + CurrentThresholdLevel;
        
        // FSM 引用（用于状态监听）
        private PlayMakerFSM? _attackControlFsm;
        private PlayMakerFSM? _phaseControlFsm;
        
        /// <summary>
        /// 需要5层基础减伤的状态名集合（Roar/P6 Web/Domain Slash）
        /// </summary>
        private static readonly HashSet<string> Level5States = new HashSet<string>
        {
            "Roar Antic", "Roar", "Roar End",
            "P6 Web Prepare", "P6 Web Cast", "P6 Web Attack 1", 
            "P6 Web Attack 2", "P6 Web Attack 3", "P6 Web Recover",
            "P6 Domain Slash"
        };
        
        /// <summary>
        /// 需要3层基础减伤的状态名集合（PinArray）
        /// </summary>
        private static readonly HashSet<string> Level3States = new HashSet<string>
        {
            "PinArray Roar", "PinArray Roar Wait", "PinArray Prepare",
            "PinArray Start", "PinArray Wait", "PinArray End"
        };
        
        /// <summary>
        /// 设置基础减伤层数
        /// </summary>
        /// <param name="level">基础层数（0-9）</param>
        public void SetBaseReductionLevel(int level)
        {
            int clampedLevel = Mathf.Clamp(level, 0, 9);
            if (_baseReductionLevel != clampedLevel)
            {
                int oldLevel = _baseReductionLevel;
                _baseReductionLevel = clampedLevel;
                Log.Info($"[减伤系统] 基础减伤层数: {oldLevel} → {_baseReductionLevel} (基础减伤: {_baseReductionLevel * 10}%)");
            }
        }
        
        /// <summary>
        /// 初始化 FSM 引用（用于状态监听）
        /// </summary>
        /// <param name="attackControl">Attack Control FSM</param>
        /// <param name="phaseControl">Phase Control FSM（可为null）</param>
        public void InitializeFsmReferences(PlayMakerFSM attackControl, PlayMakerFSM? phaseControl)
        {
            _attackControlFsm = attackControl;
            _phaseControlFsm = phaseControl;
            Log.Info($"[减伤系统] FSM引用已初始化 (AttackControl: {attackControl != null}, PhaseControl: {phaseControl != null})");
        }
        
        #endregion

        #region 伤害记录
        
        /// <summary>
        /// 伤害记录结构
        /// </summary>
        private struct DamageRecord
        {
            public float Damage;      // 伤害值
            public float Timestamp;   // 记录时间（Time.time）
            
            public DamageRecord(float damage, float timestamp)
            {
                Damage = damage;
                Timestamp = timestamp;
            }
        }
        
        /// <summary>
        /// 伤害记录列表
        /// </summary>
        private List<DamageRecord> _damageRecords = new List<DamageRecord>();
        
        #endregion

        #region 公共属性
        
        /// <summary>
        /// 获取当前的减伤百分比（0-1之间）
        /// </summary>
        public float CurrentReduction { get; private set; } = 0f;
        
        /// <summary>
        /// 获取5秒内的总伤害
        /// </summary>
        public float TotalDamageInWindow { get; private set; } = 0f;
        
        /// <summary>
        /// 获取当前的减伤档位
        /// </summary>
        public int CurrentThresholdLevel { get; private set; } = 0;
        
        /// <summary>
        /// 减伤档位变化事件（参数：新档位，新减伤比例）
        /// </summary>
        public event Action<int, float>? OnReductionChanged;
        
        #endregion

        #region Unity生命周期
        
        private void Update()
        {
            // 根据当前 FSM 状态自动更新基础减伤层数
            UpdateBaseReductionFromState();
        }
        
        private void OnDestroy()
        {
            // Boss销毁时清理所有记录
            ClearAllRecords();
        }
        
        #endregion

        #region 公共方法
        
        /// <summary>
        /// 计算如果受到指定原始伤害，应该应用的减伤比例
        /// 总减伤层数 = 基础减伤层数 + 累计伤害层数
        /// </summary>
        /// <param name="incomingOriginalDamage">即将受到的原始伤害</param>
        /// <returns>应该应用的减伤比例（0-1）</returns>
        public float CalculateReductionForIncomingDamage(float incomingOriginalDamage)
        {
            // 先清理过期记录
            CleanExpiredRecords();
            
            // 计算当前时间窗口内的总伤害（这些是已经减伤后的伤害）
            float currentReducedTotal = _damageRecords.Sum(r => r.Damage);
            
            // 加上即将受到的原始伤害（注意：即将受到的是原始伤害，还未减伤）
            float totalForThreshold = currentReducedTotal + incomingOriginalDamage;
            
            // 计算累计伤害层数
            int accumulatedLevel = Mathf.FloorToInt(totalForThreshold / DAMAGE_THRESHOLD);
            
            // 总减伤层数 = 基础减伤层数 + 累计伤害层数
            int totalLevel = _baseReductionLevel + accumulatedLevel;
            
            // 计算减伤比例
            float reduction = totalLevel * REDUCTION_PER_THRESHOLD;
            
            // 限制在最大减伤范围内
            return Mathf.Min(reduction, MAX_REDUCTION);
        }
        
        /// <summary>
        /// 记录减伤后的实际伤害并更新减伤档位
        /// </summary>
        /// <param name="reducedDamage">减伤后的实际伤害值</param>
        public void RecordReducedDamage(float reducedDamage)
        {
            if (reducedDamage <= 0)
                return;
                
            // 先清理过期记录
            CleanExpiredRecords();
            
            // 记录本次减伤后的实际伤害
            var record = new DamageRecord(reducedDamage, Time.time);
            _damageRecords.Add(record);
            
            // 更新减伤比例
            int oldLevel = CurrentThresholdLevel;
            UpdateReduction();
            
            // 如果减伤档位发生变化，触发事件
            if (oldLevel != CurrentThresholdLevel)
            {
                OnReductionChanged?.Invoke(CurrentThresholdLevel, CurrentReduction);
                Log.Info($"[减伤系统] ⚡ 减伤档位变化: {oldLevel} → {CurrentThresholdLevel} (减伤: {CurrentReduction * 100:F0}%)");
            }
        }
        
        /// <summary>
        /// 计算应用指定减伤比例后的实际伤害
        /// </summary>
        /// <param name="originalDamage">原始伤害</param>
        /// <param name="reductionRatio">减伤比例（0-1）</param>
        /// <returns>减伤后的伤害</returns>
        public static float ApplyReductionRatio(float originalDamage, float reductionRatio)
        {
            if (reductionRatio <= 0)
                return originalDamage;
                
            float reducedDamage = originalDamage * (1f - reductionRatio);
            
            return reducedDamage;
        }
        
        /// <summary>
        /// 清除所有伤害记录（用于BOSS死亡或重置）
        /// </summary>
        public void ClearAllRecords()
        {
            _damageRecords.Clear();
            CurrentReduction = 0f;
            TotalDamageInWindow = 0f;
            
            Log.Info("[减伤系统] 清除所有伤害记录");
        }
        
        #endregion

        #region 私有方法
        
        /// <summary>
        /// 根据当前 FSM 状态自动更新基础减伤层数
        /// </summary>
        private void UpdateBaseReductionFromState()
        {
            string? attackState = null;
            string? phaseState = null;
            
            // 获取 Attack Control FSM 当前状态
            if (_attackControlFsm != null)
            {
                attackState = _attackControlFsm.ActiveStateName;
            }
            
            // 获取 Phase Control FSM 当前状态
            if (_phaseControlFsm != null)
            {
                phaseState = _phaseControlFsm.ActiveStateName;
            }
            
            // 确定目标基础减伤层数
            int targetLevel = 0;
            
            // 优先检查 Attack Control 的 Level5 状态
            if (attackState != null && Level5States.Contains(attackState))
            {
                targetLevel = 5;
            }
            // 然后检查 Phase Control 的 Level3 状态（PinArray）
            else if (phaseState != null && Level3States.Contains(phaseState))
            {
                targetLevel = 3;
            }
            
            // 只在层数变化时更新（避免频繁日志）
            if (_baseReductionLevel != targetLevel)
            {
                SetBaseReductionLevel(targetLevel);
            }
        }
        
        /// <summary>
        /// 清理过期的伤害记录
        /// </summary>
        public void CleanExpiredRecords()
        {
            float currentTime = Time.time;
            float expirationTime = currentTime - DAMAGE_RECORD_LIFETIME;
            
            // 移除所有过期的记录
            int removedCount = _damageRecords.RemoveAll(record => record.Timestamp < expirationTime);
            
        }
        
        /// <summary>
        /// 更新当前的减伤百分比
        /// </summary>
        private void UpdateReduction()
        {
            // 计算5秒内的总伤害
            TotalDamageInWindow = _damageRecords.Sum(r => r.Damage);
            
            // 计算减伤档位数（每100伤害一档）
            CurrentThresholdLevel = Mathf.FloorToInt(TotalDamageInWindow / DAMAGE_THRESHOLD);
            
            // 计算减伤百分比
            float calculatedReduction = CurrentThresholdLevel * REDUCTION_PER_THRESHOLD;
            
            // 限制在最大减伤范围内
            CurrentReduction = Mathf.Min(calculatedReduction, MAX_REDUCTION);
        }
        
        #endregion
    }
}
