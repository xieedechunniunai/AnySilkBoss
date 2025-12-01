using System.Linq;
using System.Collections.Generic;
using System.Text;
using AnySilkBoss.Source.Behaviours;
using AnySilkBoss.Source.Managers;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using TeamCherry.Localization;
using UnityEngine.SceneManagement;
using UnityEngine;
using AnySilkBoss.Source.Tools;
namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 通用Boss补丁系统
/// 这是一个框架，可以用于修改任何Boss的行为
/// </summary>
internal static class BossPatches
{

    /// <summary>
    /// 在PlayMakerFSM启动时修改Boss行为
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayMakerFSM), "Start")]
    private static void ModifyBoss(PlayMakerFSM __instance)
    {
        // if (!Plugin.IsBossSaveLoaded)
        //     return;
        if (__instance.name == "Silk Boss" && __instance.FsmName == "Control")
        {
            if (__instance.gameObject.GetComponent<BossBehavior>() == null)
            {
                __instance.gameObject.AddComponent<BossBehavior>();
                Log.Info("检测到没有BossBehavior组件，添加BossBehavior组件");
            }

            // 添加动态减伤管理器
            if (__instance.gameObject.GetComponent<AnySilkBoss.Source.Managers.DamageReductionManager>() == null)
            {
                __instance.gameObject.AddComponent<AnySilkBoss.Source.Managers.DamageReductionManager>();
                Log.Info("添加DamageReductionManager组件，启用动态减伤系统");
            }
        }
        //晕眩控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Stun Control")
        {
            if (__instance.gameObject.GetComponent<StunControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<StunControlBehavior>();
                Log.Info("检测到没有StunControlBehavior组件，添加StunControlBehavior组件");
            }
        }
        //阶段控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Phase Control")
        {
            if (__instance.gameObject.GetComponent<PhaseControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<PhaseControlBehavior>();
                Log.Info("检测到没有PhaseControlBehavior组件，添加PhaseControlBehavior组件");
            }
        }
        //攻击控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Attack Control")
        {
            if (__instance.gameObject.GetComponent<AttackControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<AttackControlBehavior>();
                Log.Info("检测到没有AttackControlBehavior组件，添加AttackControlBehavior组件");
            }
        }
        else if ((__instance.name == "Rubble Field M" || __instance.name == "Rubble Field L" || __instance.name == "Rubble Field R") && __instance.FsmName == "FSM")
        {

            if (__instance.gameObject.GetComponent<RubbleFieldBehavior>() == null)
            {
                __instance.gameObject.AddComponent<RubbleFieldBehavior>();
                Log.Info("检测到没有RubbleFieldBehavior组件，添加RubbleFieldBehavior组件");
            }
        }
        else if (__instance.name.Contains("Silk Boulder") && __instance.FsmName == "Control")
        {
            if (__instance.gameObject.GetComponent<RubbleRockBehavior>() == null)
            {
                __instance.gameObject.AddComponent<RubbleRockBehavior>();
            }
        }
    }

    /// <summary>
    /// 修改Boss标题文本
    /// 用于自定义Boss名称显示
    /// </summary>
    /*
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Language), nameof(Language.Get), typeof(string), typeof(string))]
    private static void ChangeBossTitle(string key, string sheetTitle, ref string __result)
    {
        try
        {
            // 检查语言系统是否已初始化
            if (Language.CurrentLanguage == null)
                return;

            // ========== 示例：机枢舞者标题修改 ==========
            __result = key switch
            {
                "COGWORK_DANCERS_SUPER" => Language.CurrentLanguage() switch
                {
                    LanguageCode.EN => "Custom",
                    LanguageCode.ZH => "自定义",
                    _ => __result
                },
                "COGWORK_DANCERS_SUB" => Language.CurrentLanguage() switch
                {
                    LanguageCode.ZH => "自定义副标题",
                    _ => __result
                },
                "COGWORK_DANCERS_MAIN" => Language.CurrentLanguage() switch
                {
                    LanguageCode.EN => "Custom Boss Name",
                    LanguageCode.ZH => "自定义Boss名称",
                    _ => __result
                },
                _ => __result
            };
        }
        catch (System.Exception ex)
        {
            Log.Error($"Language.Get补丁执行失败: {ex.Message}");
        }
    }
    */

    #region 动态减伤系统

    /// <summary>
    /// 用于在Prefix和Postfix之间传递原始伤害的字典
    /// Key: HealthManager实例的GetHashCode()，Value: 原始伤害值
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<int, int> _originalDamageCache = new System.Collections.Generic.Dictionary<int, int>();

    /// <summary>
    /// 动态减伤系统 - 拦截HealthManager的Hit方法
    /// 通过直接修改hitInstance.DamageDealt实现减伤，与原版减伤系统并行
    /// </summary>
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
        var damageReductionManager = __instance.GetComponent<AnySilkBoss.Source.Managers.DamageReductionManager>();
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
        var damageReductionManager = __instance.GetComponent<AnySilkBoss.Source.Managers.DamageReductionManager>();
        if (damageReductionManager == null)
            return;

        // 从缓存中获取原始伤害（用于日志显示）
        int instanceKey = __instance.GetHashCode();
        int originalDamage = 0;
        if (_originalDamageCache.TryGetValue(instanceKey, out originalDamage))
        {
            // 清理缓存
            _originalDamageCache.Remove(instanceKey);
        }

        // 计算减伤后的实际伤害（hitInstance.DamageDealt 已经被我们在 Prefix 中修改过了）
        int reducedDamage = hitInstance.DamageDealt;

        // 记录减伤后的实际伤害，用于更新减伤档位
        damageReductionManager.RecordReducedDamage(reducedDamage);
    }

    #endregion
}

