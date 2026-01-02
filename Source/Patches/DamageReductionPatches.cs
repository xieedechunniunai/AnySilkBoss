using HarmonyLib;
using UnityEngine;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 动态减伤系统 Harmony 补丁
/// 拦截 HealthManager 的 Hit 方法实现减伤
/// </summary>
internal static class DamageReductionPatches
{
    /// <summary>
    /// 用于在Prefix和Postfix之间传递原始伤害的字典
    /// Key: HealthManager实例的GetHashCode()，Value: 原始伤害值
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<int, int> _originalDamageCache = new();

    /// <summary>
    /// 在Hit方法执行前修改DamageDealt，应用减伤
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HealthManager), nameof(HealthManager.Hit))]
    public static void Prefix(HealthManager __instance, ref HitInstance hitInstance)
    {
        // 只对Silk Boss应用减伤
        if (__instance.gameObject.name != "Silk Boss")
            return;

        // 获取DamageReductionManager
        var damageReductionManager = __instance.GetComponent<DamageReductionManager>();
        if (damageReductionManager == null)
        {
            Log.Error("[减伤系统] ❌ 未找到DamageReductionManager组件！");
            return;
        }

        // 保存原始伤害（在任何修改之前）
        int originalDamage = hitInstance.DamageDealt;

        // 将原始伤害缓存，供Postfix使用
        int instanceKey = __instance.GetHashCode();
        _originalDamageCache[instanceKey] = originalDamage;

        // 计算如果受到这次伤害，应该应用的减伤比例
        // 这个方法会考虑本次伤害加入后的累计伤害
        float reductionRatio = damageReductionManager.CalculateReductionForIncomingDamage(originalDamage);

        // 如果没有减伤，直接返回
        if (reductionRatio <= 0f)
        {
            return;
        }

        // 计算减伤后的伤害：新伤害 = 原伤害 * (1 - 减伤比例)
        float reducedDamageFloat = DamageReductionManager.ApplyReductionRatio(originalDamage, reductionRatio);
        int reducedDamage = Mathf.Max(1, Mathf.RoundToInt(reducedDamageFloat)); // 至少保留1点伤害

        // 直接修改DamageDealt
        hitInstance.DamageDealt = reducedDamage;
    }

    /// <summary>
    /// 在Hit方法执行后记录减伤后的实际伤害
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HealthManager), nameof(HealthManager.Hit))]
    public static void Postfix(HealthManager __instance, HitInstance hitInstance)
    {
        // 只对Silk Boss记录伤害
        if (__instance.gameObject.name != "Silk Boss")
            return;

        // 获取DamageReductionManager
        var damageReductionManager = __instance.GetComponent<DamageReductionManager>();
        if (damageReductionManager == null)
            return;

        // 从缓存中获取原始伤害（用于日志显示）
        int instanceKey = __instance.GetHashCode();
        if (_originalDamageCache.TryGetValue(instanceKey, out int originalDamage))
        {
            // 清理缓存
            _originalDamageCache.Remove(instanceKey);
        }

        // 计算减伤后的实际伤害（hitInstance.DamageDealt 已经被我们在 Prefix 中修改过了）
        int reducedDamage = hitInstance.DamageDealt;

        // 记录减伤后的实际伤害，用于更新减伤档位
        damageReductionManager.RecordReducedDamage(reducedDamage);
    }
}
