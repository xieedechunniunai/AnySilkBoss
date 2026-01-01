using System.Linq;
using System.Collections.Generic;
using System.Text;
using AnySilkBoss.Source.Behaviours;
using AnySilkBoss.Source.Behaviours.Normal;  // 普通版组件
using AnySilkBoss.Source.Behaviours.Memory;  // 梦境版组件
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
            if (MemoryManager.IsInMemoryMode)
            {
                // 禁用普通版组件，启用梦境版
                var normalBossBehavior = __instance.gameObject.GetComponent<BossBehavior>();
                if (normalBossBehavior != null)
                {
                    Object.Destroy(normalBossBehavior);
                    Log.Info("[Memory模式] 移除普通BossBehavior组件");
                }

                if (__instance.gameObject.GetComponent<MemoryBossBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<MemoryBossBehavior>();
                    Log.Info("[Memory模式] 添加梦境版 MemoryBossBehavior 组件");
                }

                var normalDamageReduction = __instance.gameObject.GetComponent<DamageReductionManager>();
                if (normalDamageReduction != null)
                {
                    Object.Destroy(normalDamageReduction);
                    Log.Info("[Memory模式] 移除普通DamageReductionManager组件");
                }

                if (__instance.gameObject.GetComponent<MemoryDamageReductionManager>() == null)
                {
                    __instance.gameObject.AddComponent<MemoryDamageReductionManager>();
                    Log.Info("[Memory模式] 添加梦境版 MemoryDamageReductionManager 组件");
                }
            }
            else
            {
                // 非梦境模式，确保使用普通版组件
                var memoryBossBehavior = __instance.gameObject.GetComponent<MemoryBossBehavior>();
                if (memoryBossBehavior != null)
                {
                    Object.Destroy(memoryBossBehavior);
                    Log.Info("[普通模式] 移除梦境版 MemoryBossBehavior 组件");
                }

                if (__instance.gameObject.GetComponent<BossBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<BossBehavior>();
                    Log.Info("检测到没有BossBehavior组件，添加BossBehavior组件");
                }

                var memoryDamageReduction = __instance.gameObject.GetComponent<MemoryDamageReductionManager>();
                if (memoryDamageReduction != null)
                {
                    Object.Destroy(memoryDamageReduction);
                    Log.Info("[普通模式] 移除梦境版 MemoryDamageReductionManager 组件");
                }

                if (__instance.gameObject.GetComponent<DamageReductionManager>() == null)
                {
                    __instance.gameObject.AddComponent<DamageReductionManager>();
                    Log.Info("添加DamageReductionManager组件，启用动态减伤系统");
                }
            }
        }
        //晕眩控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Stun Control")
        {
            if (MemoryManager.IsInMemoryMode)
            {
                var normalStunBehavior = __instance.gameObject.GetComponent<StunControlBehavior>();
                if (normalStunBehavior != null)
                {
                    Object.Destroy(normalStunBehavior);
                    Log.Info("[Memory模式] 移除普通StunControlBehavior组件");
                }

                if (__instance.gameObject.GetComponent<MemoryStunControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<MemoryStunControlBehavior>();
                    Log.Info("[Memory模式] 添加梦境版 MemoryStunControlBehavior 组件");
                }
            }
            else
            {
                var memoryStunBehavior = __instance.gameObject.GetComponent<MemoryStunControlBehavior>();
                if (memoryStunBehavior != null)
                {
                    Object.Destroy(memoryStunBehavior);
                    Log.Info("[普通模式] 移除梦境版 MemoryStunControlBehavior 组件");
                }

                if (__instance.gameObject.GetComponent<StunControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<StunControlBehavior>();
                    Log.Info("检测到没有StunControlBehavior组件，添加StunControlBehavior组件");
                }
            }
        }
        //阶段控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Phase Control")
        {
            // Memory 模式下使用梦境版 MemoryPhaseControl，不使用普通版 PhaseControlBehavior
            if (MemoryManager.IsInMemoryMode)
            {
                var normalPhaseBehavior = __instance.gameObject.GetComponent<PhaseControlBehavior>();
                if (normalPhaseBehavior != null)
                {
                    Object.Destroy(normalPhaseBehavior);
                    Log.Info("[Memory模式] 移除普通 PhaseControlBehavior 组件");
                }

                if (__instance.gameObject.GetComponent<MemoryPhaseControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<MemoryPhaseControlBehavior>();
                    Log.Info("[Memory模式] 添加梦境版 MemoryPhaseControl 组件");
                }
            }
            else
            {
                var memoryPhaseBehavior = __instance.gameObject.GetComponent<MemoryPhaseControlBehavior>();
                if (memoryPhaseBehavior != null)
                {
                    Object.Destroy(memoryPhaseBehavior);
                    Log.Info("[普通模式] 移除梦境版 MemoryPhaseControl 组件");
                }

                if (__instance.gameObject.GetComponent<PhaseControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<PhaseControlBehavior>();
                    Log.Info("[普通模式] 添加 PhaseControlBehavior 组件");
                }
            }
        }
        //攻击控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Attack Control")
        {
            if (MemoryManager.IsInMemoryMode)
            {
                var normalAttackBehavior = __instance.gameObject.GetComponent<AttackControlBehavior>();
                if (normalAttackBehavior != null)
                {
                    Object.Destroy(normalAttackBehavior);
                    Log.Info("[Memory模式] 移除普通 AttackControlBehavior 组件");
                }

                if (__instance.gameObject.GetComponent<MemoryAttackControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<MemoryAttackControlBehavior>();
                    Log.Info("[Memory模式] 添加梦境版 MemoryAttackControlBehavior 组件");
                }
            }
            else
            {
                var memoryAttackBehavior = __instance.gameObject.GetComponent<MemoryAttackControlBehavior>();
                if (memoryAttackBehavior != null)
                {
                    Object.Destroy(memoryAttackBehavior);
                    Log.Info("[普通模式] 移除梦境版 MemoryAttackControlBehavior 组件");
                }

                if (__instance.gameObject.GetComponent<AttackControlBehavior>() == null)
                {
                    __instance.gameObject.AddComponent<AttackControlBehavior>();
                    Log.Info("检测到没有AttackControlBehavior组件，添加AttackControlBehavior组件");
                }
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
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Language), nameof(Language.Get), typeof(string), typeof(string))]
    private static void ChangeBossTitle(string key, string sheetTitle, ref string __result)
    {
        try
        {
            // 检查语言系统是否已初始化,是否在BOSS场景
            if (Language.CurrentLanguage == null || !Plugin.IsInBossRoom)
                return;
            if (MemoryManager.IsInMemoryMode)
            {
                __result = key switch
                {
                    "SILK_SUPER" => Language.CurrentLanguage() switch
                    {
                        LanguageCode.EN => "Astral Oblivion",
                        LanguageCode.ZH => "苍耀归墟",
                        _ => __result
                    },
                    "SILK_MAIN" => Language.CurrentLanguage() switch
                    {
                        LanguageCode.EN => "Dawn",
                        LanguageCode.ZH => "破晓",
                        _ => __result
                    },
                    _ => __result
                };
            }
            else
            {
                __result = key switch
                {
                    "SILK_SUPER" => Language.CurrentLanguage() switch
                    {
                        LanguageCode.EN => "Loombinder Matriarch",
                        LanguageCode.ZH => "织狱圣母",
                        _ => __result
                    },
                    "SILK_MAIN" => Language.CurrentLanguage() switch
                    {
                        LanguageCode.EN => "Silk",
                        LanguageCode.ZH => "灵丝",
                        _ => __result
                    },
                    _ => __result
                };
            }
        }
        catch (System.Exception ex)
        {
            Log.Error($"Language.Get补丁执行失败: {ex.Message}");
        }
    }

    // 梦境死亡处理已集成到 DeathManager 中

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

/// <summary>
/// 拦截场景切换，在梦境模式下阻止离开 BOSS 房间
/// </summary>
[HarmonyPatch(typeof(GameManager))]
internal static class MemorySceneTransitionPatch
{
    private const string TARGET_SCENE = "Cradle_03";
    private const string TRIGGER_SCENE = "Cradle_03_Destroyed";

    /// <summary>
    /// 拦截 BeginSceneTransition，在梦境模式下重定向到退出梦境
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("BeginSceneTransition")]
    private static bool OnBeginSceneTransition(GameManager __instance, GameManager.SceneLoadInfo info)
    {
        // 不在梦境模式，不拦截
        if (!MemoryManager.IsInMemoryMode)
            return true;

        // 当前不在 BOSS 房间，不拦截
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentScene != TARGET_SCENE)
            return true;

        // 目标是主菜单或退出游戏，不拦截
        if (info.SceneName == "Menu_Title" || info.SceneName == "Quit_To_Menu" ||
            info.SceneName == "PermaDeath" || string.IsNullOrEmpty(info.SceneName))
            return true;

        // 目标是原触发场景（正常退出梦境），不拦截
        if (info.SceneName == TRIGGER_SCENE)
            return true;

        // 目标是当前场景（同场景重生），不拦截
        if (info.SceneName == currentScene)
        {
            Log.Info($"[MemoryPatch] 允许同场景重生: {currentScene}");
            return true;
        }

        // 梦境模式下试图离开 BOSS 房间到其他场景，拦截并改为退出梦境
        Log.Info($"[MemoryPatch] 拦截梦境中的场景切换: {currentScene} → {info.SceneName}，改为退出梦境");

        // 触发退出梦境流程
        MemoryManager.Instance?.TriggerExitMemory();

        // 阻止原来的场景切换
        return false;
    }
}

/// <summary>
/// 梦境模式下允许攻击同时命中BOSS和触发拼刀
/// 原版逻辑：如果这次攻击已经打到BOSS，则跳过拼刀判定
/// 此Patch在梦境模式下绕过这个检查，允许同一次攻击既打到BOSS又触发拼刀
/// 
/// DamageHero.TryClashTinkCollider 中调用：
///   if (componentInChildren.HasBeenDamaged(this.healthManager)) { return; }
/// 其中 healthManager 是 HealthManager 类型，实现了 IHitResponder 接口
/// </summary>
[HarmonyPatch(typeof(DamageEnemies), nameof(DamageEnemies.HasBeenDamaged), new System.Type[] { typeof(IHitResponder) })]
internal static class MemoryClashTinkPatch
{
    /// <summary>
    /// 在梦境模式下，强制返回 false，使拼刀判定不会因为"已经打到BOSS"而被跳过
    /// </summary>
    [HarmonyPrefix]
    private static bool Prefix(DamageEnemies __instance, IHitResponder hitResponder, ref bool __result)
    {
        Log.Info("[MemoryClashTinkPatch] 检测是否梦境模式");
        // 仅在梦境模式下生效
        if (!MemoryManager.IsInMemoryMode)
        {
            return true; // 执行原方法
        }

        // 检查是否是 HealthManager 类型
        HealthManager healthManager = hitResponder as HealthManager;
        if (healthManager == null)
        {
            return true; // 非 HealthManager，执行原方法
        }

        // 检查是否是 Silk Boss
        if (healthManager.gameObject.name != "Silk Boss")
        {
            return true; // 非 Silk Boss，执行原方法
        }

        // 梦境模式下，强制返回 false，允许拼刀判定继续
        Log.Info("[MemoryClashTinkPatch] 梦境模式下允许拼刀判定继续");
        __result = false;
        return false; // 跳过原方法
    }
}

/// <summary>
/// 梦境模式下拼刀也能刷新下劈攻击
/// 原版 NailParryRecover 只刷新横劈冷却，不刷新下劈状态
/// 此 Patch 在 NailParryRecover 执行后额外设置 allowAttackCancellingDownspikeRecovery = true
/// 使玩家可以在下劈弹跳/恢复期间立即再次下劈
/// 
/// 注意：保留弹跳状态（downSpikeBouncing），只是允许取消恢复期
/// </summary>
[HarmonyPatch(typeof(HeroController), nameof(HeroController.NailParryRecover))]
internal static class MemoryParryDownspikePatch
{
    /// <summary>
    /// 在 NailParryRecover 执行后，额外允许取消下劈恢复期
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(HeroController __instance)
    {
        // 仅在梦境模式下生效
        if (!MemoryManager.IsInMemoryMode)
        {
            return;
        }

        // 通过反射设置 allowAttackCancellingDownspikeRecovery = true
        // 这个字段是 private 的，需要反射访问
        var field = typeof(HeroController).GetField(
            "allowAttackCancellingDownspikeRecovery",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field != null)
        {
            field.SetValue(__instance, true);
            Log.Info("[MemoryParryDownspikePatch] 梦境模式下拼刀刷新下劈");
        }
    }
}

/// <summary>
/// 拦截玩家受伤方法，实现丝球/地刺伤害累积机制
/// 10秒内再次受到同类型伤害则翻倍
/// 仅在梦境模式(Memory)下生效
/// </summary>
[HarmonyPatch(typeof(HeroController), nameof(HeroController.TakeDamage),
    new System.Type[]
    {
        typeof(GameObject),
        typeof(GlobalEnums.CollisionSide),
        typeof(int),
        typeof(GlobalEnums.HazardType),
        typeof(GlobalEnums.DamagePropertyFlags)
    })]
internal static class HeroDamageStackPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        GameObject go,
        GlobalEnums.CollisionSide damageSide,
        ref int damageAmount,
        GlobalEnums.HazardType hazardType,
        GlobalEnums.DamagePropertyFlags damagePropertyFlags)
    {
        // 仅梦境模式生效
        if (!MemoryManager.IsInMemoryMode)
        {
            return;
        }

        // 只处理敌人类型伤害
        if (hazardType != GlobalEnums.HazardType.ENEMY)
        {
            return;
        }
        if (go == null)
        {
            return;
        }
        // 识别伤害来源
        var sourceType = DamageStackManager.IdentifyDamageSource(go);
        if (sourceType == DamageStackManager.DamageSourceType.None)
        {
            return;
        }
        int multiplier = DamageStackManager.GetDamageMultiplier(sourceType);
        int newDamage = damageAmount * multiplier;
        // 记录本次伤害时间
        DamageStackManager.RecordDamage(sourceType);
        damageAmount = newDamage;
    }
}
