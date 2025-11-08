using System.Linq;
using System.Collections.Generic;
using System.Text;
using AnySilkBoss.Source.Behaviours;
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
        if (__instance.name == "Silk Boss" && __instance.FsmName == "Control" )
        {
            Log.Info("检测到Silk Boss，应用mod行为");
            
            if (__instance.gameObject.GetComponent<BossBehavior>() == null)
            {
                __instance.gameObject.AddComponent<BossBehavior>();
                Log.Info("检测到没有BossBehavior组件，添加BossBehavior组件");
            }
            // if (__instance.gameObject.GetComponent<NeedleController>() == null)
            // {
            //     __instance.gameObject.AddComponent<NeedleController>();
            // }
            
            // if (__instance.gameObject.GetComponent<GlobalEventHandler>() == null)
            // {
            //     __instance.gameObject.AddComponent<GlobalEventHandler>();
            // }
        }
        //晕眩控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Stun Control")
        {
            Log.Info("检测到Silk Boss Stun Control，应用mod行为");
            
            if (__instance.gameObject.GetComponent<StunControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<StunControlBehavior>();
                Log.Info("检测到没有StunControlBehavior组件，添加StunControlBehavior组件");
            }
        }
        //阶段控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Phase Control")
        {
            Log.Info("检测到Silk Boss Phase Control，应用mod行为");
            
            if (__instance.gameObject.GetComponent<PhaseControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<PhaseControlBehavior>();
                Log.Info("检测到没有PhaseControlBehavior组件，添加PhaseControlBehavior组件");
            }
        }
        //攻击控制器
        else if (__instance.name == "Silk Boss" && __instance.FsmName == "Attack Control")
        {
            Log.Info("检测到Silk Boss Attack Control，添加攻击控制器");
            
            if (__instance.gameObject.GetComponent<AttackControlBehavior>() == null)
            {
                __instance.gameObject.AddComponent<AttackControlBehavior>();
                Log.Info("检测到没有AttackControlBehavior组件，添加AttackControlBehavior组件");
            }
        }
    }

    /// <summary>
    /// 修改Boss标题文本
    /// 用于自定义Boss名称显示
    /// 注意：此补丁暂时禁用，因为它可能导致游戏初始化时的NullReferenceException
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

    /// <summary>
    /// 在Boss战斗中允许播放乐器
    /// </summary>
    [HarmonyPatch(typeof(HeroController), nameof(HeroController.CanPlayNeedolin))]
    public class CanPlayNeedolinPatch
    {
        public static bool Prefix(HeroController __instance, ref bool __result)
        {
            // 如果已加载 BOSS 存档，则允许播放 Needolin
            if (Plugin.IsBossSaveLoaded)
            {
                __result = true;
                return false; // 跳过原方法执行
            }

            // 否则执行原方法逻辑
            return true;
        }
    }
}

