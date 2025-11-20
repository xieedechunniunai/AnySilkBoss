using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 动态减伤管理器 - 负责记录BOSS受到的伤害并计算动态减伤
    /// 机制：5秒内受到的伤害总和每高于100，BOSS获得10%的伤害减免
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
        
        /// <summary>
        /// 减伤档位变化时的冷却时间（秒），用于视觉反馈去抖
        /// </summary>
        private const float REDUCTION_CHANGE_COOLDOWN = 0.1f;
        
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
        
        private void OnDestroy()
        {
            // Boss销毁时清理所有记录
            ClearAllRecords();
        }
        
        #endregion

        #region 公共方法
        
        /// <summary>
        /// 计算如果受到指定原始伤害，应该应用的减伤比例
        /// 判断档位时：用原始伤害 + 记录的减伤后伤害
        /// </summary>
        /// <param name="incomingOriginalDamage">即将受到的原始伤害</param>
        /// <returns>应该应用的减伤比例（0-1）</returns>
        public float CalculateReductionForIncomingDamage(float incomingOriginalDamage)
        {
            // 先清理过期记录
            CleanExpiredRecords();
            
            // 计算当前5秒内的总伤害（这些是已经减伤后的伤害）
            float currentReducedTotal = _damageRecords.Sum(r => r.Damage);
            
            // 加上即将受到的原始伤害（注意：即将受到的是原始伤害，还未减伤）
            float totalForThreshold = currentReducedTotal + incomingOriginalDamage;
            
            // 计算减伤档位
            int thresholdLevel = Mathf.FloorToInt(totalForThreshold / DAMAGE_THRESHOLD);
            
            // 计算减伤比例
            float reduction = thresholdLevel * REDUCTION_PER_THRESHOLD;
            
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
        /// 清理过期的伤害记录（超过5秒）
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
