using UnityEngine;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 伤害累积管理器 - 追踪丝球和地刺的伤害时间，实现10秒内翻倍机制
    /// </summary>
    internal static class DamageStackManager
    {
        /// <summary>
        /// 伤害来源类型
        /// </summary>
        public enum DamageSourceType
        {
            None,
            SilkBall,
            SpikeFloor
        }

        /// <summary>
        /// 累积窗口时间（秒）
        /// </summary>
        public static float StackWindow = 10f;

        /// <summary>
        /// 基础伤害
        /// </summary>
        public static int BaseDamage = 1;

        /// <summary>
        /// 累积后的伤害倍数
        /// </summary>
        public static int StackMultiplier = 2;

        /// <summary>
        /// 上次受到丝球伤害的时间
        /// </summary>
        private static float _lastSilkBallDamageTime = -999f;

        /// <summary>
        /// 上次受到地刺伤害的时间
        /// </summary>
        private static float _lastSpikeFloorDamageTime = -999f;

        /// <summary>
        /// 获取伤害值（根据累积情况返回1或2）
        /// </summary>
        /// <param name="sourceType">伤害来源类型</param>
        /// <returns>最终伤害值</returns>
        public static int GetDamageAmount(DamageSourceType sourceType)
        {
            float lastDamageTime = GetLastDamageTime(sourceType);
            float timeSinceLastDamage = Time.time - lastDamageTime;

            // 如果在10秒内受过同类型伤害，翻倍
            if (timeSinceLastDamage <= StackWindow)
            {
                return BaseDamage * StackMultiplier;
            }

            return BaseDamage;
        }

        /// <summary>
        /// 获取伤害倍数（根据累积情况返回 1 或 StackMultiplier）
        /// </summary>
        /// <param name="sourceType">伤害来源类型</param>
        public static int GetDamageMultiplier(DamageSourceType sourceType)
        {
            float lastDamageTime = GetLastDamageTime(sourceType);
            float timeSinceLastDamage = Time.time - lastDamageTime;
            return timeSinceLastDamage <= StackWindow ? StackMultiplier : 1;
        }

        /// <summary>
        /// 记录伤害时间
        /// </summary>
        /// <param name="sourceType">伤害来源类型</param>
        public static void RecordDamage(DamageSourceType sourceType)
        {
            switch (sourceType)
            {
                case DamageSourceType.SilkBall:
                    _lastSilkBallDamageTime = Time.time;
                    break;
                case DamageSourceType.SpikeFloor:
                    _lastSpikeFloorDamageTime = Time.time;
                    break;
            }
        }

        /// <summary>
        /// 获取上次伤害时间
        /// </summary>
        private static float GetLastDamageTime(DamageSourceType sourceType)
        {
            return sourceType switch
            {
                DamageSourceType.SilkBall => _lastSilkBallDamageTime,
                DamageSourceType.SpikeFloor => _lastSpikeFloorDamageTime,
                _ => -999f
            };
        }

        /// <summary>
        /// 重置所有计时器（场景切换时调用）
        /// </summary>
        public static void Reset()
        {
            _lastSilkBallDamageTime = -999f;
            _lastSpikeFloorDamageTime = -999f;
        }

        /// <summary>
        /// 识别伤害来源类型
        /// </summary>
        /// <param name="damageSource">造成伤害的 GameObject</param>
        /// <returns>伤害来源类型</returns>
        public static DamageSourceType IdentifyDamageSource(GameObject damageSource)
        {
            if (damageSource == null) return DamageSourceType.None;

            // 检查是否是丝球：向上查找父物体是否有 SilkBallBehavior
            Transform current = damageSource.transform;
            while (current != null)
            {
                if (current.GetComponent<Behaviours.Common.SilkBallBehavior>() != null)
                {
                    return DamageSourceType.SilkBall;
                }
                current = current.parent;
            }

            // 检查是否是地刺：名称为 "Spike Collider" 且祖先有 MemorySpikeFloorBehavior
            current = damageSource.transform;
            while (current != null)
            {
                if (current.GetComponent<Behaviours.Memory.MemorySpikeFloorBehavior>() != null)
                {
                    return DamageSourceType.SpikeFloor;
                }
                current = current.parent;
            }

            return DamageSourceType.None;
        }
    }
}
