using HarmonyLib;
using UnityEngine;
using AnySilkBoss.Source.Behaviours.Normal;
using AnySilkBoss.Source.Behaviours.Memory;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// Boss 行为相关 Harmony 补丁
/// 在 PlayMakerFSM 启动时添加/切换 Boss 行为组件
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
        if (__instance.name == "Silk Boss" && __instance.FsmName == "Control")
        {
            HandleBossControl(__instance);
        }
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Stun Control")
        {
            HandleStunControl(__instance);
        }
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Phase Control")
        {
            HandlePhaseControl(__instance);
        }
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Attack Control")
        {
            HandleAttackControl(__instance);
        }
        else if ((__instance.name == "Rubble Field M" || __instance.name == "Rubble Field L" || __instance.name == "Rubble Field R") && __instance.FsmName == "FSM")
        {
            HandleRubbleField(__instance);
        }
        else if (__instance.name.Contains("Silk Boulder") && __instance.FsmName == "Control")
        {
            HandleSilkBoulder(__instance);
        }
    }

    #region Boss Control
    private static void HandleBossControl(PlayMakerFSM __instance)
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
    #endregion

    #region Stun Control
    private static void HandleStunControl(PlayMakerFSM __instance)
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
    #endregion

    #region Phase Control
    private static void HandlePhaseControl(PlayMakerFSM __instance)
    {
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
    #endregion

    #region Attack Control
    private static void HandleAttackControl(PlayMakerFSM __instance)
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
    #endregion

    #region Rubble Field & Boulder
    private static void HandleRubbleField(PlayMakerFSM __instance)
    {
        if (__instance.gameObject.GetComponent<RubbleFieldBehavior>() == null)
        {
            __instance.gameObject.AddComponent<RubbleFieldBehavior>();
            Log.Info("检测到没有RubbleFieldBehavior组件，添加RubbleFieldBehavior组件");
        }
    }

    private static void HandleSilkBoulder(PlayMakerFSM __instance)
    {
        if (__instance.gameObject.GetComponent<RubbleRockBehavior>() == null)
        {
            __instance.gameObject.AddComponent<RubbleRockBehavior>();
        }
    }
    #endregion
}
