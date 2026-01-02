using HarmonyLib;
using TeamCherry.Localization;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 本地化相关 Harmony 补丁
/// 用于修改 Boss 名称等文本显示
/// </summary>
internal static class I18nPatches
{
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
}
