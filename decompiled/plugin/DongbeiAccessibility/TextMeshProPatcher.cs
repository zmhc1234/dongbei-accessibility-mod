using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace DongbeiAccessibility;

public static class TextMeshProPatcher
{
	private static Type _tmpTextType;

	private static bool _patched;

	public static void PatchAll(Harmony harmony)
	{
		if (_patched)
		{
			return;
		}
		Plugin.Log.LogInfo((object)"开始查找 TextMeshPro 基类 TMP_Text...");
		try
		{
			_tmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
			Plugin.Log.LogInfo((object)("TMP_Text 基类查找结果: " + ((_tmpTextType != null) ? "找到" : "未找到")));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("查找 TMP_Text 类型时出错: " + ex.GetType().Name + " - " + ex.Message));
		}
		if (_tmpTextType == null)
		{
			Plugin.Log.LogInfo((object)"基类未找到，尝试查找子类...");
			try
			{
				_tmpTextType = Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro");
				Plugin.Log.LogInfo((object)("TextMeshPro 查找结果: " + ((_tmpTextType != null) ? "找到" : "未找到")));
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning((object)("查找 TextMeshPro 时出错: " + ex2.GetType().Name + " - " + ex2.Message));
			}
		}
		if (_tmpTextType == null)
		{
			Plugin.Log.LogWarning((object)"未找到任何 TextMeshPro 相关类型，文本捕获功能不可用");
			return;
		}
		Plugin.Log.LogInfo((object)("找到类型 " + _tmpTextType.FullName + "，开始应用补丁..."));
		try
		{
			PatchTextProperty(harmony, _tmpTextType);
		}
		catch (Exception ex3)
		{
			Plugin.Log.LogError((object)("Hook text 属性失败: " + ex3.GetType().Name + " - " + ex3.Message));
			Plugin.Log.LogError((object)("堆栈跟踪: " + ex3.StackTrace));
		}
		_patched = true;
		Plugin.Log.LogInfo((object)"TextMeshPro 补丁应用完成");
	}

	private static void PatchTextProperty(Harmony harmony, Type type)
	{
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Expected O, but got Unknown
		Plugin.Log.LogInfo((object)("正在获取 " + type.Name + ".text 属性..."));
		PropertyInfo property = type.GetProperty("text", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			Plugin.Log.LogInfo((object)"当前类未找到 text 属性，尝试基类...");
			property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		}
		if (property == null)
		{
			Plugin.Log.LogWarning((object)("未找到 " + type.Name + ".text 属性"));
			return;
		}
		Plugin.Log.LogInfo((object)("找到 " + type.Name + ".text 属性"));
		MethodInfo setMethod = property.GetSetMethod();
		if (setMethod == null)
		{
			Plugin.Log.LogWarning((object)(type.Name + ".text 属性没有 setter"));
			return;
		}
		Plugin.Log.LogInfo((object)("找到 " + type.Name + ".text setter 方法: " + setMethod.DeclaringType.Name + "." + setMethod.Name));
		HarmonyMethod val = new HarmonyMethod(typeof(TextMeshProPatcher).GetMethod("TextSetPostfix", BindingFlags.Static | BindingFlags.NonPublic));
		Plugin.Log.LogInfo((object)"正在应用补丁...");
		harmony.Patch((MethodBase)setMethod, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		Plugin.Log.LogInfo((object)("已成功 Hook " + type.Name + ".text setter"));
	}

	private static void TextSetPostfix(object __instance, string value)
	{
		try
		{
			if (!string.IsNullOrEmpty(value))
			{
				Plugin.RequestSpeak(value);
				Plugin.MarkNeedDetect();
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogDebug((object)("TextSetPostfix 异常: " + ex.Message));
			}
		}
	}
}
