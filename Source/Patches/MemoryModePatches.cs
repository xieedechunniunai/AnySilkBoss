using HarmonyLib;
using UnityEngine;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

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
        // 仅在梦境模式下生效
        if (!MemoryManager.IsInMemoryMode)
        {
            return true; // 执行原方法
        }

        // 检查是否是 HealthManager 类型
        HealthManager? healthManager = hitResponder as HealthManager;
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
