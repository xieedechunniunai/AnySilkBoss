using HarmonyLib;
using UnityEngine;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 动态减伤系统 Harmony 补丁
/// 拦截 HealthManager 的 TakeDamage 方法实现减伤（只有真正受伤时才触发）
/// </summary>
internal static class DamageReductionPatches
{
    /// <summary>
    /// 在TakeDamage方法执行前修改DamageDealt，应用减伤
    /// TakeDamage 只有在 Hit 方法判断不是无敌帧后才会被调用，
    /// 因此这里不会在 BOSS 无敌时错误累加减伤层数
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HealthManager), "TakeDamage")]
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

        // 保存原始伤害
        int originalDamage = hitInstance.DamageDealt;

        // 计算减伤比例
        float reductionRatio = damageReductionManager.CalculateReductionForIncomingDamage(originalDamage);

        if (reductionRatio <= 0f)
        {
            // 没有减伤，但仍需记录伤害用于累计
            damageReductionManager.RecordReducedDamage(originalDamage);
            return;
        }

        // 计算减伤后的伤害
        float reducedDamageFloat = DamageReductionManager.ApplyReductionRatio(originalDamage, reductionRatio);
        int reducedDamage = Mathf.Max(1, Mathf.RoundToInt(reducedDamageFloat));

        // 修改伤害值
        hitInstance.DamageDealt = reducedDamage;

        // 记录减伤后的实际伤害
        damageReductionManager.RecordReducedDamage(reducedDamage);

        Log.Info($"[减伤系统] 原始伤害: {originalDamage} → 减伤后: {reducedDamage} (减伤率: {reductionRatio:P0})");
    }
}
