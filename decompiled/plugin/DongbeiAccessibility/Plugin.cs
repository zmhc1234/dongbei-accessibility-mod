using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DongbeiAccessibility;

[BepInPlugin("com.dongbei.accessibility", "东北往事 无障碍插件", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
	private enum UIState
	{
		Unknown,
		MainMenu,
		Storyline,
		Dialogue,
		Options,
		Settings,
		QTE
	}

	private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

	private delegate void TimerProc(IntPtr hWnd, uint uMsg, IntPtr nIDEvent, uint dwTime);

	internal static ManualLogSource Log;

	internal static Plugin Instance;

	private static Harmony _harmony;

	private static string _lastSpokenText = "";

	private static float _lastSpeakTime;

	private const float MIN_SPEAK_INTERVAL = 0.1f;

	private static bool _inOptionsMode;

	private static OptionItem[] _options = new OptionItem[0];

	private static int _currentOptionIndex;

	private static bool _isHorizontalLayout;

	private static Type _chapterStorylineControllerType;

	private static Type _storylineUIManagerType;

	private static Type _progressTreeGraphControllerType;

	private static Type _gameControllerType;

	private static Type _gameNodeType;

	private static Type _gameOptionType;

	private static Type _progressTreeNodeComponentType;

	private static bool _storylineTypesResolved = false;

	private static bool _inNodeMode;

	private static OptionItem[] _storylineNodes = new OptionItem[0];

	private static int _currentNodeIndex;

	private static bool _inStorylineMode;

	private static int _storylineMissCount = 0;

	private const int STORYLINE_MISS_THRESHOLD = 3;

	private static int _optionsMissCount = 0;

	private const int OPTIONS_MISS_THRESHOLD = 3;

	private static bool _autoQTEEnabled = false;

	private static bool _suppressCurrentKey = false;

	private static Type _qteControllerType;

	private static bool _qteTypesResolved = false;

	private static Type _triggerAreaType;

	private static bool _triggerAreaTypesResolved = false;

	private static Type _subtitleManagerType;

	private static object _subtitleTextComponent;

	private static bool _subtitleTypesResolved = false;

	private static UIState _currentUIState = UIState.Unknown;

	private static bool _needDetect = true;

	private static bool _pluginInitialized = false;

	private static bool _isApplicationQuitting = false;

	private static bool _subtitleSpeakEnabled = true;

	private static string _lastDetectedSignature = "";

	private static float _lastDetectTime = 0f;

	private const float MIN_DETECT_INTERVAL = 0.5f;

	private static Type _settingsType;

	private static Type _audioManagerType;

	private static bool _settingsTypesResolved = false;

	private static bool _inSettingsMode;

	private static SettingItem[] _settings = new SettingItem[0];

	private static int _currentSettingIndex;

	private static DateTime _ignoreSettingsUntilUtc = DateTime.MinValue;

	private const int WH_KEYBOARD_LL = 13;

	private const int WM_KEYDOWN = 256;

	private const int WM_SYSKEYDOWN = 260;

	private static LowLevelKeyboardProc _keyboardProc;

	private static IntPtr _hookId = IntPtr.Zero;

	private static uint _gameProcessId = 0u;

	private const int VK_F5 = 116;

	private const int VK_F6 = 117;

	private const int VK_F7 = 118;

	private const int VK_F8 = 119;

	private const int VK_F9 = 120;

	private const int VK_F10 = 121;

	private const int VK_F11 = 122;

	private const int VK_F12 = 123;

	private const int VK_D = 68;

	private const int VK_RETURN = 13;

	private const int VK_UP = 38;

	private const int VK_DOWN = 40;

	private const int VK_LEFT = 37;

	private const int VK_RIGHT = 39;

	private const int VK_SPACE = 32;

	private const uint MOUSEEVENTF_LEFTDOWN = 2u;

	private const uint MOUSEEVENTF_LEFTUP = 4u;

	private const uint MOUSEEVENTF_RIGHTDOWN = 8u;

	private const uint MOUSEEVENTF_RIGHTUP = 16u;

	private const int SM_CXSCREEN = 0;

	private const int SM_CYSCREEN = 1;

	private const uint KEYEVENTF_KEYUP = 2u;

	private static IntPtr _timerId = IntPtr.Zero;

	private const uint AUTO_DETECT_INTERVAL = 500u;

	private static readonly string[] CODE_EXPLORE_KEYWORDS = new string[27]
	{
		"QTE", "QuickTime", "QuickTimeEvent", "QTEvent", "Drag", "Dragger", "Draggable", "Event", "EventHandler", "EventSystem",
		"Manager", "Controller", "System", "Game", "Play", "Story", "Battle", "Fight", "Combat", "Input",
		"Touch", "Action", "Sequence", "Trigger", "Tutorial", "MiniGame", "MiniGame"
	};

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetCursorPos(int X, int Y);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr GetModuleHandle(string lpModuleName);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern uint GetCurrentProcessId();

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

	private void Awake()
	{
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Expected O, but got Unknown
		Instance = this;
		Log = Logger;
		Log.LogInfo((object)"========== 插件 东北往事 无障碍插件 v1.0.0 正在加载... ==========");
		Log.LogInfo((object)"[诊断] Awake 被调用，插件对象创建成功");
		try
		{
			ManualLogSource log = Log;
			GameObject gameObject = ((Component)this).gameObject;
			log.LogInfo((object)("[诊断] 插件对象名称: " + ((gameObject != null) ? ((UnityEngine.Object)gameObject).name : null)));
		}
		catch
		{
		}
		_pluginInitialized = true;
		Log.LogInfo((object)"[修复] 插件功能已激活，即使对象被销毁也将继续运行");
		_gameProcessId = GetCurrentProcessId();
		Log.LogInfo((object)$"游戏进程 ID: {_gameProcessId}");
		TolkHelper.Initialize();
		if (TolkHelper.IsAvailable)
		{
			string text = TolkHelper.DetectScreenReader();
			Log.LogInfo((object)("Tolk 初始化成功，当前屏幕阅读器: " + text));
			TolkHelper.Speak("东北往事 无障碍插件已启动");
		}
		else
		{
			Log.LogWarning((object)"Tolk 初始化失败，所有朗读功能将不可用");
		}
		_harmony = new Harmony("com.dongbei.accessibility");
		try
		{
			TextMeshProPatcher.PatchAll(_harmony);
			Log.LogInfo((object)"TextMeshPro 文本捕获补丁已应用");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("应用 TextMeshPro 补丁失败: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈跟踪: " + ex.StackTrace));
		}
		try
		{
			Log.LogInfo((object)"QTE 自动跳过补丁已应用");
		}
		catch (Exception ex2)
		{
			Log.LogError((object)("应用 QTE 补丁失败: " + ex2.GetType().Name + " - " + ex2.Message));
			Log.LogError((object)("堆栈跟踪: " + ex2.StackTrace));
		}
		try
		{
			InstallKeyboardHook();
		}
		catch (Exception ex3)
		{
			Log.LogError((object)("安装键盘钩子失败: " + ex3.GetType().Name + " - " + ex3.Message));
			Log.LogError((object)("堆栈跟踪: " + ex3.StackTrace));
		}
		try
		{
			StartAutoDetectTimer();
		}
		catch (Exception ex4)
		{
			Log.LogError((object)("启动自动检测定时器失败: " + ex4.GetType().Name + " - " + ex4.Message));
		}
		Log.LogInfo((object)"插件加载完成！");
		Log.LogInfo((object)"提示：全自动模式已启用，自动识别界面，上下左右切换选项，回车确认");
	}

	private void OnEnable()
	{
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"[诊断] OnEnable 被调用，插件已启用");
		}
	}

	private void OnDisable()
	{
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"[诊断] OnDisable 被调用，插件已禁用");
		}
		try
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogInfo((object)"[诊断] === OnDisable 调用堆栈开始 ===");
			}
			ManualLogSource log3 = Log;
			if (log3 != null)
			{
				log3.LogInfo((object)Environment.StackTrace);
			}
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogInfo((object)"[诊断] === OnDisable 调用堆栈结束 ===");
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log5 = Log;
			if (log5 != null)
			{
				log5.LogInfo((object)("[诊断] 获取堆栈失败: " + ex.Message));
			}
		}
	}

	private void OnApplicationQuit()
	{
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"[修复] 检测到游戏正在退出");
		}
		_isApplicationQuitting = true;
	}

	private void OnDestroy()
	{
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"========== OnDestroy 被调用 ==========");
		}
		ManualLogSource log2 = Log;
		if (log2 != null)
		{
			log2.LogInfo((object)"[诊断] 插件对象即将被销毁");
		}
		try
		{
			ManualLogSource log3 = Log;
			if (log3 != null)
			{
				GameObject gameObject = ((Component)this).gameObject;
				log3.LogInfo((object)$"[诊断] 对象是否激活: {((gameObject != null) ? new bool?(gameObject.activeSelf) : ((bool?)null))}");
			}
		}
		catch
		{
		}
		try
		{
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogInfo((object)"[诊断] === 调用堆栈开始 ===");
			}
			ManualLogSource log5 = Log;
			if (log5 != null)
			{
				log5.LogInfo((object)Environment.StackTrace);
			}
			ManualLogSource log6 = Log;
			if (log6 != null)
			{
				log6.LogInfo((object)"[诊断] === 调用堆栈结束 ===");
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log7 = Log;
			if (log7 != null)
			{
				log7.LogInfo((object)("[诊断] 获取堆栈失败: " + ex.Message));
			}
		}
		if (_isApplicationQuitting)
		{
			ManualLogSource log8 = Log;
			if (log8 != null)
			{
				log8.LogInfo((object)"[修复] 检测到游戏正在退出，执行正常清理...");
			}
			try
			{
				if (_timerId != IntPtr.Zero)
				{
					KillTimer(IntPtr.Zero, _timerId);
					_timerId = IntPtr.Zero;
					ManualLogSource log9 = Log;
					if (log9 != null)
					{
						log9.LogInfo((object)"定时器已停止");
					}
				}
			}
			catch (Exception ex2)
			{
				ManualLogSource log10 = Log;
				if (log10 != null)
				{
					log10.LogError((object)("停止定时器失败: " + ex2.Message));
				}
			}
			try
			{
				if (_hookId != IntPtr.Zero)
				{
					UnhookWindowsHookEx(_hookId);
					_hookId = IntPtr.Zero;
					ManualLogSource log11 = Log;
					if (log11 != null)
					{
						log11.LogInfo((object)"键盘钩子已卸载");
					}
				}
			}
			catch (Exception ex3)
			{
				ManualLogSource log12 = Log;
				if (log12 != null)
				{
					log12.LogError((object)("卸载键盘钩子失败: " + ex3.Message));
				}
			}
			try
			{
				if (_harmony != null)
				{
					_harmony.UnpatchSelf();
					ManualLogSource log13 = Log;
					if (log13 != null)
					{
						log13.LogInfo((object)"Harmony 补丁已卸载");
					}
				}
			}
			catch (Exception ex4)
			{
				ManualLogSource log14 = Log;
				if (log14 != null)
				{
					log14.LogError((object)("卸载 Harmony 补丁失败: " + ex4.Message));
				}
			}
			try
			{
				TolkHelper.Unload();
				ManualLogSource log15 = Log;
				if (log15 != null)
				{
					log15.LogInfo((object)"Tolk 已关闭");
				}
			}
			catch (Exception ex5)
			{
				ManualLogSource log16 = Log;
				if (log16 != null)
				{
					log16.LogError((object)("关闭 Tolk 失败: " + ex5.Message));
				}
			}
			ManualLogSource log17 = Log;
			if (log17 != null)
			{
				log17.LogInfo((object)"插件清理完成！");
			}
		}
		else
		{
			ManualLogSource log18 = Log;
			if (log18 != null)
			{
				log18.LogInfo((object)"[修复] 对象被意外销毁，但插件功能继续运行！不执行清理");
			}
			ManualLogSource log19 = Log;
			if (log19 != null)
			{
				log19.LogInfo((object)"[修复] 键盘钩子、定时器、Harmony补丁将继续工作");
			}
		}
		Instance = null;
		_pluginInitialized = false;
	}

	private static void StartAutoDetectTimer()
	{
		if (!(_timerId != IntPtr.Zero))
		{
			Log.LogInfo((object)$"启动自动检测定时器，间隔 {500u} 毫秒");
			TimerProc lpTimerFunc = AutoDetectTimerProc;
			_timerId = SetTimer(IntPtr.Zero, IntPtr.Zero, 500u, lpTimerFunc);
			if (_timerId != IntPtr.Zero)
			{
				Log.LogInfo((object)"自动检测定时器启动成功");
				return;
			}
			int lastWin32Error = Marshal.GetLastWin32Error();
			Log.LogError((object)$"自动检测定时器启动失败，错误码: {lastWin32Error}");
		}
	}

	private static void AutoDetectTimerProc(IntPtr hWnd, uint uMsg, IntPtr nIDEvent, uint dwTime)
	{
		try
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)"[诊断] 定时器触发");
			}
			if (IsGameWindowActive() && _needDetect)
			{
				_needDetect = false;
				DetectUIState();
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogError((object)("自动检测定时器异常: " + ex.GetType().Name + " - " + ex.Message));
			}
		}
	}

	public static void MarkNeedDetect()
	{
		_needDetect = true;
	}

	private static void DetectUIState()
	{
		try
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)"[诊断] 开始检测界面状态");
			}
			UIState uIState = UIState.Unknown;
			string text = "";
			if (IsQTEActive())
			{
				uIState = UIState.QTE;
				text = "qte";
			}
			else if (IsInStorylinePage())
			{
				uIState = UIState.Storyline;
				text = GetStorylineSignature();
			}
			else if (DateTime.UtcNow < _ignoreSettingsUntilUtc)
			{
				OptionItem[] clickableOptions = GetClickableOptions();
				LogInputState("DetectUIState settings ignored; candidates=" + ((clickableOptions != null) ? clickableOptions.Length.ToString() : "null"));
				if (clickableOptions != null && clickableOptions.Length >= 2)
				{
					uIState = UIState.Options;
					text = GetOptionsSignature(clickableOptions);
				}
				else
				{
					uIState = UIState.Dialogue;
					text = "dialogue";
				}
			}
			else if (IsInSettingsPage())
			{
				uIState = UIState.Settings;
				text = GetSettingsSignature();
			}
			else
			{
				OptionItem[] clickableOptions = GetClickableOptions();
				LogInputState("DetectUIState candidates=" + ((clickableOptions != null) ? clickableOptions.Length.ToString() : "null"));
				if (clickableOptions != null && clickableOptions.Length >= 2)
				{
					uIState = UIState.Options;
					text = GetOptionsSignature(clickableOptions);
				}
				else
				{
					uIState = UIState.Dialogue;
					text = "dialogue";
				}
			}
			LogInputState("DetectUIState raw=" + uIState + ", signature=" + text);
			if (_currentUIState == UIState.Storyline && uIState != UIState.Storyline)
			{
				if (uIState == UIState.Options || uIState == UIState.Settings || uIState == UIState.QTE || uIState == UIState.Dialogue)
				{
					Log.LogInfo((object)$"[防抖] 故事线切换到明确界面 {uIState}，立即切换");
					_storylineMissCount = 0;
				}
				else
				{
					_storylineMissCount++;
					Log.LogInfo((object)$"[防抖] 故事线检测失败，连续失败次数: {_storylineMissCount}/{3}");
					if (_storylineMissCount < 3)
					{
						Log.LogInfo((object)"[防抖] 未达到阈值，保持故事线状态");
						uIState = UIState.Storyline;
						text = GetStorylineSignature();
					}
					else
					{
						Log.LogInfo((object)"[防抖] 达到阈值，切换到新状态");
					}
				}
			}
			else if (uIState == UIState.Storyline)
			{
				if (_storylineMissCount > 0)
				{
					Log.LogInfo((object)"[防抖] 重新检测到故事线，重置计数器");
					_storylineMissCount = 0;
				}
			}
			else if (_currentUIState == UIState.Options && uIState != UIState.Options)
			{
				if (uIState == UIState.Settings || uIState == UIState.Storyline || uIState == UIState.QTE)
				{
					Log.LogInfo((object)$"[防抖] 选项切换到明确界面 {uIState}，立即切换");
					_optionsMissCount = 0;
				}
				else
				{
				_optionsMissCount++;
				Log.LogInfo((object)$"[防抖] 选项检测失败，连续失败次数: {_optionsMissCount}/{3}");
				if (_optionsMissCount < 3)
				{
					Log.LogInfo((object)"[防抖] 未达到阈值，保持选项状态");
					uIState = UIState.Options;
					text = _lastDetectedSignature;
				}
				else
				{
					Log.LogInfo((object)"[防抖] 达到阈值，切换到新状态");
				}
				}
			}
			else if (uIState == UIState.Options && _optionsMissCount > 0)
			{
				Log.LogInfo((object)"[防抖] 重新检测到选项，重置计数器");
				_optionsMissCount = 0;
			}
			if (uIState != _currentUIState || text != _lastDetectedSignature)
			{
				Log.LogInfo((object)$"界面变化: {_currentUIState} -> {uIState}, 特征: {_lastDetectedSignature} -> {text}");
				_currentUIState = uIState;
				_lastDetectedSignature = text;
				OnUIStateChanged(uIState);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("检测界面状态异常: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
		}
	}

	private static void OnUIStateChanged(UIState newState)
	{
		try
		{
			switch (newState)
			{
			case UIState.Storyline:
				Log.LogInfo((object)"进入故事线页面");
				EnterStorylineMode();
				break;
			case UIState.Options:
				Log.LogInfo((object)"进入选项界面");
				_inSettingsMode = false;
				_settings = new SettingItem[0];
				_currentSettingIndex = 0;
				EnterOptionsModeAuto();
				break;
			case UIState.Settings:
				Log.LogInfo((object)"进入设置界面");
				EnterSettingsMode();
				break;
			case UIState.QTE:
				Log.LogInfo((object)"检测到 QTE");
				if (_autoQTEEnabled)
				{
					Log.LogInfo((object)"自动过 QTE 已开启，尝试跳过...");
					SkipCurrentQTE();
				}
				else
				{
					TolkHelper.Speak("空格", interrupt: true);
				}
				break;
			case UIState.Dialogue:
				Log.LogInfo((object)"进入对话界面");
				LeaveOptions();
				break;
			default:
				LeaveOptions();
				break;
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("处理界面变化异常: " + ex.GetType().Name + " - " + ex.Message));
		}
	}

	private static bool IsQTEActive()
	{
		try
		{
			ResolveQTETypes();
			if (_qteControllerType == null)
			{
				return false;
			}
			Array array = FindObjectsOfType(_qteControllerType);
			if (array == null || array.Length == 0)
			{
				return false;
			}
			foreach (object item in array)
			{
				try
				{
					PropertyInfo property = _qteControllerType.GetProperty("isActiveAndEnabled");
					if (property != null && (bool)property.GetValue(item))
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("检测 QTE 激活状态失败: " + ex.Message));
			return false;
		}
	}

	private static string GetStorylineSignature()
	{
		try
		{
			ChapterInfo[] storylineChapters = GetStorylineChapters();
			return $"storyline_{storylineChapters.Length}";
		}
		catch
		{
			return "storyline";
		}
	}

	private static string GetOptionsSignature(OptionItem[] options)
	{
		try
		{
			if (options == null || options.Length == 0)
			{
				return "options_0";
			}
			string arg = ((options.Length != 0) ? options[0].Text : "");
			return $"options_{options.Length}_{arg}";
		}
		catch
		{
			return "options";
		}
	}

	private static void EnterOptionsModeAuto()
	{
		OptionItem[] clickableOptions = GetClickableOptions();
		if (clickableOptions == null || clickableOptions.Length < 2)
		{
			LeaveOptions();
			return;
		}
		SetOptions(SortOptions(clickableOptions));
		string arg = (_isHorizontalLayout ? "横向" : "纵向");
		TolkHelper.Speak($"检测到 {clickableOptions.Length} 个选项，{arg}排列，按上下左右切换，按回车确认", interrupt: true);
		if (_options != null && _options.Length != 0)
		{
			Thread.Sleep(500);
			TolkHelper.Speak("第1项，" + _options[0].Text);
		}
	}

	private static OptionItem[] GetOptionsFromGameController()
	{
		try
		{
			if (_gameControllerType == null || _gameNodeType == null || _gameOptionType == null)
			{
				return new OptionItem[0];
			}
			Array array = FindObjectsOfType(_gameControllerType);
			if (array == null || array.Length == 0)
			{
				return new OptionItem[0];
			}
			object value = array.GetValue(0);
			if (value == null)
			{
				return new OptionItem[0];
			}
			FieldInfo field = _gameControllerType.GetField("optionsShown", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null && !(bool)field.GetValue(value))
			{
				return new OptionItem[0];
			}
			MethodInfo method = _gameControllerType.GetMethod("GetCurrentNode", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				return new OptionItem[0];
			}
			object obj = method.Invoke(value, null);
			if (obj == null)
			{
				return new OptionItem[0];
			}
			FieldInfo field2 = _gameNodeType.GetField("options", BindingFlags.Instance | BindingFlags.Public);
			if (field2 == null)
			{
				return new OptionItem[0];
			}
			if (!(field2.GetValue(obj) is Array { Length: not 0 } array2))
			{
				return new OptionItem[0];
			}
			List<OptionItem> list = new List<OptionItem>();
			for (int i = 0; i < array2.Length; i++)
			{
				object value2 = array2.GetValue(i);
				if (value2 == null)
				{
					continue;
				}
				FieldInfo field3 = _gameOptionType.GetField("buttonword", BindingFlags.Instance | BindingFlags.Public);
				if (!(field3 == null))
				{
					string text = field3.GetValue(value2) as string;
					if (!string.IsNullOrWhiteSpace(text))
					{
						OptionItem optionItem = new OptionItem();
						optionItem.Text = text.Trim();
						optionItem.Index = i;
						optionItem.ClickableComponent = value;
						optionItem.Index = i;
						list.Add(optionItem);
					}
				}
			}
			Log.LogInfo((object)$"[精准检测] 从GameController获取到 {list.Count} 个选项");
			return list.ToArray();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("从GameController获取选项失败: " + ex.Message));
			return new OptionItem[0];
		}
	}

	private static OptionItem[] GetClickableOptions()
	{
		try
		{
			OptionItem[] optionsFromGameController = GetOptionsFromGameController();
			if (optionsFromGameController != null && optionsFromGameController.Length >= 2)
			{
				return optionsFromGameController;
			}
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			if (allVisibleTextsWithPosition == null || allVisibleTextsWithPosition.Length == 0)
			{
				return new OptionItem[0];
			}
			List<OptionItem> list = new List<OptionItem>();
			OptionItem[] array = allVisibleTextsWithPosition;
			OptionItem[] array2 = array;
			foreach (OptionItem optionItem in array2)
			{
				if (optionItem.ClickableComponent != null)
				{
					list.Add(optionItem);
				}
			}
			if (list.Count >= 2 && list.Count <= 12)
			{
				return list.ToArray();
			}
			if (list.Count > 12)
			{
				Log.LogInfo((object)$"[选项过滤] 可点击候选过多({list.Count})，疑似包含后台页面，改用短文本筛选");
			}
			string[] array3 = new string[34]
			{
				"威望", "好感", "返回", "版本", "Version", "Demo", "进度", "章节", "第", "章",
				"节", "回", "设置", "选项", "音量", "画质", "分辨率", "全屏", "语言", "字幕",
				"确定", "取消", "确认", "关闭", "打开", "上一页", "下一页", "上一页", "下一页", "+",
				"-", "％", "%", "/"
			};
			List<OptionItem> list2 = new List<OptionItem>();
			array = allVisibleTextsWithPosition;
			OptionItem[] array4 = array;
			foreach (OptionItem optionItem2 in array4)
			{
				string text = optionItem2.Text.Trim();
				if (string.IsNullOrEmpty(text) || text.Length >= 20)
				{
					continue;
				}
				bool flag = false;
				string[] array5 = array3;
				foreach (string value in array5)
				{
					if (text.Contains(value))
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					continue;
				}
				bool flag2 = false;
				string text2 = text;
				foreach (char c in text2)
				{
					if ((c >= '一' && c <= '鿿') || char.IsLetter(c))
					{
						flag2 = true;
						break;
					}
				}
				if (flag2 && text.Length > 2)
				{
					list2.Add(optionItem2);
				}
			}
			List<OptionItem> list3 = new List<OptionItem>();
			list3.AddRange(list);
			int val;
			if (list.Count > 0)
			{
				val = list.Count * 2;
			}
			else
			{
				val = 8;
				if (list2.Count < 3)
				{
					return new OptionItem[0];
				}
			}
			for (int m = 0; m < Math.Min(list2.Count, val); m++)
			{
				bool flag3 = false;
				foreach (OptionItem item in list3)
				{
					if (item.Text == list2[m].Text)
					{
						flag3 = true;
						break;
					}
				}
				if (!flag3)
				{
					list3.Add(list2[m]);
				}
			}
			if (list3.Count >= 2 && list3.Count <= 12)
			{
				return list3.ToArray();
			}
			return new OptionItem[0];
		}
		catch (Exception ex)
		{
			Log.LogError((object)("获取可点击选项失败: " + ex.Message));
			return new OptionItem[0];
		}
	}

	private static bool IsInSettingsPage()
	{
		try
		{
			ResolveSettingsTypes();
			if (_settingsType != null)
			{
				Array array = FindObjectsOfType(_settingsType);
				if (array != null && array.Length > 0)
				{
					foreach (object item in array)
					{
						try
						{
							PropertyInfo property = _settingsType.GetProperty("isActiveAndEnabled");
							if (property != null && (bool)property.GetValue(item))
							{
								return true;
							}
						}
						catch
						{
						}
					}
				}
			}
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			if (allVisibleTextsWithPosition == null || allVisibleTextsWithPosition.Length == 0)
			{
				return false;
			}
			string[] array2 = new string[8] { "设置", "选项", "音量", "画质", "分辨率", "全屏", "语言", "字幕" };
			int num = 0;
			OptionItem[] array3 = allVisibleTextsWithPosition;
			OptionItem[] array4 = array3;
			foreach (OptionItem optionItem in array4)
			{
				string[] array5 = array2;
				string[] array6 = array5;
				foreach (string value in array6)
				{
					if (optionItem.Text.Contains(value))
					{
						num++;
						break;
					}
				}
			}
			return num >= 3;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("检测设置界面失败: " + ex.Message));
			return false;
		}
	}

	private static string GetSettingsSignature()
	{
		try
		{
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			return $"settings_{((allVisibleTextsWithPosition != null) ? allVisibleTextsWithPosition.Length : 0)}";
		}
		catch
		{
			return "settings";
		}
	}

	private static void EnterSettingsMode()
	{
		try
		{
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			if (allVisibleTextsWithPosition == null || allVisibleTextsWithPosition.Length == 0)
			{
				TolkHelper.Speak("设置界面", interrupt: true);
				return;
			}
			List<SettingItem> list = new List<SettingItem>();
			AddVisibleSettingTexts(list, allVisibleTextsWithPosition);
			if (list.Count == 0)
			{
				list.AddRange(GetPreciseSettingsItems(allVisibleTextsWithPosition));
			}
			if (list.Count > 0)
			{
				SettingItem[] array2 = (_settings = list.OrderBy((SettingItem s) => s.ScreenY).ToArray());
				LogSettingsList(array2);
				_currentSettingIndex = 0;
				_inSettingsMode = true;
				_inOptionsMode = false;
				TolkHelper.Speak($"设置界面，共 {array2.Length} 个选项，按上下光标切换，按左右调整数值，按回车确认", interrupt: true);
				if (array2.Length != 0)
				{
					SpeakCurrentSetting();
				}
			}
			else
			{
				TolkHelper.Speak("设置界面", interrupt: true);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("进入设置模式失败: " + ex.Message));
			TolkHelper.Speak("设置界面", interrupt: true);
		}
	}

	private static void ToggleSubtitleSpeak()
	{
		_subtitleSpeakEnabled = !_subtitleSpeakEnabled;
		string text = (_subtitleSpeakEnabled ? "已开启" : "已关闭");
		Log.LogInfo((object)("字幕朗读: " + text));
		TolkHelper.Speak("字幕朗读 " + text, interrupt: true);
	}

	private static void PlayGameSound(string soundName)
	{
		try
		{
			Type type = Type.GetType("GlobalButtonSoundListener, Assembly-CSharp");
			if (type == null)
			{
				return;
			}
			PropertyInfo property = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
			object obj = property?.GetValue(null);
			if (obj == null)
			{
				return;
			}
			Type nestedType = type.GetNestedType("SoundType", BindingFlags.Public);
			if (nestedType == null)
			{
				return;
			}
			object obj2 = Enum.Parse(nestedType, soundName);
			MethodInfo method = type.GetMethod("PlaySound", BindingFlags.Instance | BindingFlags.Public);
			method?.Invoke(obj, new object[1] { obj2 });
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("播放游戏音效失败: " + ex.Message));
		}
	}

	private static void LogSettingsList(SettingItem[] items)
	{
		if (items == null)
		{
			return;
		}
		for (int i = 0; i < items.Length; i++)
		{
			SettingItem settingItem = items[i];
			if (settingItem != null)
			{
				Log.LogInfo((object)$"[设置列表] {i + 1}/{items.Length}: {settingItem.Name}, type={settingItem.Type}, y={settingItem.ScreenY}, component={(settingItem.Component?.GetType().Name ?? "null")}");
			}
		}
	}

	private static void ResolveSettingsTypes()
	{
		if (_settingsTypesResolved)
		{
			return;
		}
		_settingsTypesResolved = true;
		try
		{
			_settingsType = Type.GetType("Settings, Assembly-CSharp");
			if (_settingsType != null)
			{
				Log.LogInfo((object)"找到 Settings 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 Settings 类型，设置精准检测不可用");
			}
			_audioManagerType = Type.GetType("AudioManager, Assembly-CSharp");
			if (_audioManagerType != null)
			{
				Log.LogInfo((object)"找到 AudioManager 类型");
			}
			_subtitleManagerType = Type.GetType("SubtitleManager, Assembly-CSharp");
			if (_subtitleManagerType != null)
			{
				Log.LogInfo((object)"找到 SubtitleManager 类型");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析设置类型失败: " + ex.Message));
		}
	}

	private static SettingItem CreateSettingItemFromComponent(object component, string name, float screenY)
	{
		if (component == null)
		{
			return null;
		}
		try
		{
			SettingItem settingItem = new SettingItem();
			settingItem.Name = CleanSettingName(name);
			settingItem.ClickComponent = component;
			settingItem.ScreenY = screenY;
			Type type2 = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI");
			Type type3 = Type.GetType("UnityEngine.UI.Toggle, UnityEngine.UI");
			Type type4 = Type.GetType("UnityEngine.UI.Dropdown, UnityEngine.UI");
			Type type5 = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
			if ((type5 != null && type5.IsInstanceOfType(component)) || (type4 != null && type4.IsInstanceOfType(component)))
			{
				return CreateDropdownSettingItem(settingItem, component);
			}
			if (type3 != null && type3.IsInstanceOfType(component))
			{
				return CreateToggleSettingItem(settingItem, component, type3);
			}
			if (type2 != null && type2.IsInstanceOfType(component))
			{
				return CreateSliderSettingItem(settingItem, component, type2);
			}
			object obj = FindComponentNear(component, type2);
			if (type2 != null && obj != null)
			{
				return CreateSliderSettingItem(settingItem, obj, type2);
			}
			object obj2 = FindComponentNear(component, type3);
			if (type3 != null && obj2 != null)
			{
				return CreateToggleSettingItem(settingItem, obj2, type3);
			}
			object obj3 = FindComponentNear(component, type5);
			if (obj3 == null)
			{
				obj3 = FindComponentNear(component, type4);
			}
			if (obj3 != null)
			{
				return CreateDropdownSettingItem(settingItem, obj3);
			}
			return null;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("从组件创建设置项失败: " + ex.Message));
			return null;
		}
	}

	private static SettingItem CreateSliderSettingItem(SettingItem settingItem, object component, Type sliderType)
	{
		settingItem.Type = SettingItem.SettingType.Slider;
		settingItem.Component = component;
		PropertyInfo property = sliderType.GetProperty("value");
		if (property != null)
		{
			settingItem.Value = Convert.ToSingle(property.GetValue(component));
		}
		PropertyInfo property2 = sliderType.GetProperty("minValue");
		if (property2 != null)
		{
			settingItem.MinValue = Convert.ToSingle(property2.GetValue(component));
		}
		PropertyInfo property3 = sliderType.GetProperty("maxValue");
		if (property3 != null)
		{
			settingItem.MaxValue = Convert.ToSingle(property3.GetValue(component));
		}
		return settingItem;
	}

	private static SettingItem CreateToggleSettingItem(SettingItem settingItem, object component, Type toggleType)
	{
		settingItem.Type = SettingItem.SettingType.Toggle;
		settingItem.Component = component;
		PropertyInfo property = toggleType.GetProperty("isOn");
		if (property != null)
		{
			settingItem.IsOn = (bool)property.GetValue(component);
		}
		return settingItem;
	}

	private static SettingItem CreateDropdownSettingItem(SettingItem settingItem, object component)
	{
		settingItem.Type = SettingItem.SettingType.Dropdown;
		settingItem.Component = component;
		Type type = component.GetType();
		PropertyInfo property = type.GetProperty("value");
		if (property != null)
		{
			settingItem.SelectedIndex = (int)property.GetValue(component);
		}
		PropertyInfo property2 = type.GetProperty("options");
		if (property2 != null)
		{
			object value = property2.GetValue(component);
			if (value != null)
			{
				PropertyInfo property3 = value.GetType().GetProperty("Count");
				if (property3 != null)
				{
					int num = (int)property3.GetValue(value);
					settingItem.Options = new string[num];
					for (int i = 0; i < num; i++)
					{
						settingItem.Options[i] = GetDropdownOptionText(value, i);
					}
				}
			}
		}
		return settingItem;
	}

	private static SettingItem[] GetPreciseSettingsItems(OptionItem[] visibleTexts)
	{
		List<SettingItem> list = new List<SettingItem>();
		try
		{
			ResolveSettingsTypes();
			object activeObject = GetActiveObject(_settingsType);
			float order = 0f;
			object obj = GetFieldValue(activeObject, "languageDropdownController");
			AddSettingFromField(list, obj, "languageDropdown", "语言", ref order);
			object obj2 = GetFieldValue(activeObject, "volumeController") ?? GetActiveObject(_audioManagerType);
			AddSettingFromField(list, obj2, "volumeSlider", "主音量", ref order);
			AddSettingFromField(list, obj2, "soundEffectSlider", "音效音量", ref order);
			AddSettingFromValue(list, GetFieldValue(activeObject, "heroVoiceDropdown"), "男主声音", ref order);
			object obj3 = GetFieldValue(activeObject, "resolutionSettingsController") ?? GetActiveObject(Type.GetType("ResolutionSettingsController, Assembly-CSharp"));
			AddSettingFromField(list, obj3, "resolutionDropdown", "分辨率", ref order);
			AddSettingFromField(list, obj3, "displayModeDropdown", "显示模式", ref order);
			object activeObject2 = GetActiveObject(Type.GetType("AudioTrackSettingsController, Assembly-CSharp"));
			AddSettingFromField(list, activeObject2, "audioTrackDropdown", "外置音轨", ref order);
			AddVisibleSettingControls(list, visibleTexts, ref order);
			SettingItem settingItem = FindReturnButtonSetting(visibleTexts, ref order);
			if (settingItem != null && !ContainsSettingComponent(list, settingItem.Component))
			{
				list.Add(settingItem);
			}
			Log.LogInfo((object)$"[设置精准采集] 获取到 {list.Count} 个设置项");
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("精准采集设置项失败: " + ex.Message));
		}
		return list.ToArray();
	}

	private static void AddVisibleSettingTexts(List<SettingItem> list, OptionItem[] visibleTexts)
	{
		if (list == null || visibleTexts == null)
		{
			return;
		}
		string[] array = new string[19]
		{
			"系统设置", "语言", "主音量", "音效音量", "男主声音", "分辨率", "显示模式", "外置音轨", "字幕", "字幕开关",
			"敏感词消音", "暂停视频", "选择选项", "从左到右", "从上到下", "返回", "重置", "全屏", "窗口"
		};
		foreach (OptionItem optionItem in visibleTexts)
		{
			if (optionItem == null || string.IsNullOrWhiteSpace(optionItem.Text))
			{
				continue;
			}
			string text = optionItem.Text.Trim();
			if (text.Length > 30 || IsSettingTextAlreadyCovered(list, text))
			{
				continue;
			}
			if (IsStandaloneSettingValue(text))
			{
				continue;
			}
			bool flag = false;
			foreach (string value in array)
			{
				if (text.Contains(value))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				continue;
			}
			if (text == "系统设置")
			{
				continue;
			}
			SettingItem settingItem;
			if ((text == "返回" || text == "关闭" || text == "Back") && optionItem.ClickableComponent != null)
			{
				settingItem = new SettingItem
				{
					Name = text,
					Type = SettingItem.SettingType.Button,
					Component = optionItem.ClickableComponent,
					ClickComponent = optionItem.ClickableComponent
				};
			}
			else
			{
				settingItem = CreateKnownFallbackSetting(optionItem);
				if (settingItem == null)
				{
					string settingTextWithValue = BuildSettingTextWithValue(optionItem, visibleTexts);
					settingItem = new SettingItem
					{
						Name = settingTextWithValue,
						Type = SettingItem.SettingType.Text
					};
				}
			}
			settingItem.ScreenX = optionItem.ScreenX;
			settingItem.ScreenY = optionItem.ScreenY;
			settingItem.HasScreenPosition = optionItem.HasScreenPosition;
			list.Add(settingItem);
			Log.LogInfo((object)$"[设置文本兜底] {text}: {settingItem.Type}");
		}
	}

	private static SettingItem CreateKnownFallbackSetting(OptionItem label)
	{
		string text = (label?.Text ?? "").Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			ResolveSettingsTypes();
			object activeObject4 = GetActiveObject(_settingsType);
			object activeObject5 = GetActiveObject(_audioManagerType);
			object obj = GetFieldValue(activeObject4, "volumeController") ?? activeObject5;
			object obj2 = GetFieldValue(activeObject4, "resolutionSettingsController") ?? GetActiveObject(Type.GetType("ResolutionSettingsController, Assembly-CSharp"));
			if (text.Contains("主音量"))
			{
				return CreateNamedSettingFromComponent(GetFieldValue(obj, "volumeSlider"), "主音量", label);
			}
			if (text.Contains("音效音量"))
			{
				return CreateNamedSettingFromComponent(GetFieldValue(obj, "soundEffectSlider"), "音效音量", label);
			}
			if (text.Contains("语言"))
			{
				object fieldValue = GetFieldValue(GetFieldValue(activeObject4, "languageDropdownController"), "languageDropdown");
				return CreateNamedSettingFromComponent(fieldValue, "语言", label);
			}
			if (text.Contains("分辨率"))
			{
				return CreateNamedSettingFromComponent(GetFieldValue(obj2, "resolutionDropdown"), "分辨率", label);
			}
			if (text.Contains("显示模式"))
			{
				return CreateNamedSettingFromComponent(GetFieldValue(obj2, "displayModeDropdown"), "显示模式", label);
			}
			if (text.Contains("字幕"))
			{
				object activeObject = GetActiveObject(Type.GetType("SubtitleSettingsController, Assembly-CSharp"));
				object fieldValue = GetFieldValue(activeObject, "subtitleDropdown");
				SettingItem settingItem = CreateNamedSettingFromComponent(fieldValue, "字幕开关", label);
				if (settingItem != null)
				{
					Log.LogInfo((object)"[设置兜底控件] 字幕开关: Dropdown");
					return settingItem;
				}
			}
			if (text.Contains("敏感词消音") || text.Contains("外置音轨") || text.Contains("音轨"))
			{
				object activeObject2 = GetActiveObject(Type.GetType("AudioTrackSettingsController, Assembly-CSharp"));
				object fieldValue2 = GetFieldValue(activeObject2, "audioTrackDropdown");
				SettingItem settingItem2 = CreateNamedSettingFromComponent(fieldValue2, "敏感词消音", label);
				if (settingItem2 != null)
				{
					Log.LogInfo((object)"[设置兜底控件] 敏感词消音: Dropdown");
					return settingItem2;
				}
			}
			if (text.Contains("男主声音") || text.Contains("男主"))
			{
				object fieldValue3 = GetFieldValue(activeObject4, "heroVoiceDropdown");
				SettingItem settingItem3 = CreateNamedSettingFromComponent(fieldValue3, "男主声音", label);
				if (settingItem3 != null)
				{
					Log.LogInfo((object)"[设置兜底控件] 男主声音: Dropdown");
					return settingItem3;
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("创建已知兜底控件失败: " + ex.Message));
		}
		return null;
	}

	private static SettingItem CreateNamedSettingFromComponent(object component, string name, OptionItem label)
	{
		SettingItem settingItem = CreateSettingItemFromComponent(component, name, label != null ? label.ScreenY : 0f);
		if (settingItem == null)
		{
			return null;
		}
		settingItem.Name = name;
		if (label != null)
		{
			settingItem.ScreenX = label.ScreenX;
			settingItem.ScreenY = label.ScreenY;
			settingItem.HasScreenPosition = label.HasScreenPosition;
		}
		ApplyKnownSettingOptions(settingItem);
		Log.LogInfo((object)$"[设置可见绑定] {name}: {settingItem.Type}, component={component.GetType().Name}");
		return settingItem;
	}

	private static string BuildSettingTextWithValue(OptionItem label, OptionItem[] visibleTexts)
	{
		string text = (label?.Text ?? "").Trim();
		if (label == null || visibleTexts == null || string.IsNullOrEmpty(text))
		{
			return text;
		}
		string knownSettingValue = GetKnownSettingValue(text);
		if (!string.IsNullOrWhiteSpace(knownSettingValue))
		{
			Log.LogInfo((object)("[设置状态] " + text + " => " + knownSettingValue));
			return AppendSettingValue(text, knownSettingValue);
		}
		string nearbySettingValue = FindNearbySettingValue(label, visibleTexts);
		if (!string.IsNullOrWhiteSpace(nearbySettingValue) && !text.Contains(nearbySettingValue))
		{
			return text + " " + nearbySettingValue;
		}
		return text;
	}

	private static string AppendSettingValue(string text, string value)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
		{
			return text ?? "";
		}
		text = text.Trim();
		value = value.Trim();
		if (text.Contains(value))
		{
			return text;
		}
		return text + " " + value;
	}

	private static string GetKnownSettingValue(string label)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return "";
		}
		label = label.Trim();
		try
		{
			if (label.Contains("字幕"))
			{
				return PlayerPrefs.GetInt("SubtitleVisible", 1) != 0 ? "开启" : "关闭";
			}
			if (label.Contains("敏感词消音") || label.Contains("外置音轨") || label.Contains("音轨"))
			{
				return PlayerPrefs.GetInt("ExternalAudioTrack", 0) == 1 ? "开启" : "关闭";
			}
			if (label.Contains("男主声音") || label.Contains("男主"))
			{
				return PlayerPrefs.GetInt("HeroVoice", 1) == 1 ? "开启" : "关闭";
			}
			if (label.Contains("语言"))
			{
				return NormalizeLanguageName(PlayerPrefs.GetString("GameLanguage", "Chinese"));
			}
			if (label.Contains("分辨率"))
			{
				int @int = PlayerPrefs.GetInt("ResolutionWidth", 0);
				int int2 = PlayerPrefs.GetInt("ResolutionHeight", 0);
				if (@int <= 0 || int2 <= 0)
				{
					@int = Screen.width;
					int2 = Screen.height;
				}
				if (@int > 0 && int2 > 0)
				{
					return $"{@int}x{int2}";
				}
			}
			if (label.Contains("显示模式") || label == "全屏" || label == "窗口")
			{
				return NormalizeDisplayModeName(PlayerPrefs.GetInt("DisplayMode", (int)Screen.fullScreenMode));
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("读取已知设置状态失败: " + ex.Message));
		}
		return "";
	}

	private static string NormalizeLanguageName(string language)
	{
		if (string.IsNullOrWhiteSpace(language))
		{
			return "中文";
		}
		switch (language.Trim())
		{
		case "Chinese":
			return "中文";
		case "Traditional":
			return "繁体中文";
		case "English":
			return "English";
		case "Japanese":
			return "日语";
		case "Korean":
			return "韩语";
		default:
			return language.Trim();
		}
	}

	private static string NormalizeDisplayModeName(int mode)
	{
		if (mode == (int)FullScreenMode.Windowed)
		{
			return "窗口";
		}
		return "全屏";
	}

	private static bool IsStandaloneSettingValue(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		return text == "开启" || text == "关闭" || text == "打开" || text == "全屏" || text == "窗口" || IsResolutionValue(text) || text.EndsWith("%") || text.EndsWith("％");
	}

	private static string FindNearbySettingValue(OptionItem label, OptionItem[] visibleTexts)
	{
		OptionItem optionItem = null;
		float num = float.MaxValue;
		string labelText = (label?.Text ?? "").Trim();
		foreach (OptionItem optionItem2 in visibleTexts)
		{
			if (optionItem2 == null || optionItem2 == label || string.IsNullOrWhiteSpace(optionItem2.Text))
			{
				continue;
			}
			string text = optionItem2.Text.Trim();
			if (!IsLikelySettingValueForLabel(labelText, text))
			{
				continue;
			}
			float num2 = Math.Abs(optionItem2.ScreenY - label.ScreenY);
			if (num2 > 80f)
			{
				continue;
			}
			float num3 = Math.Abs(optionItem2.ScreenX - label.ScreenX);
			float num4 = num2 * 4f + num3;
			if (num4 < num)
			{
				num = num4;
				optionItem = optionItem2;
			}
		}
		return optionItem?.Text?.Trim() ?? "";
	}

	private static bool IsLikelySettingValueForLabel(string label, string value)
	{
		if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		label = label.Trim();
		value = value.Trim();
		if (label.Contains("分辨率"))
		{
			return IsResolutionValue(value);
		}
		if (label.Contains("显示模式"))
		{
			return value == "全屏" || value == "窗口";
		}
		if (label.Contains("语言"))
		{
			return value == "中文" || value == "English" || value.Contains("日本") || value.Contains("한국") || value.Contains("繁");
		}
		if (label.Contains("音量"))
		{
			return value.EndsWith("%") || value.EndsWith("％");
		}
		if (label.Contains("字幕") || label.Contains("消音") || label.Contains("男主") || label.Contains("音轨"))
		{
			return value == "开启" || value == "关闭" || value == "打开" || value == "ON" || value == "OFF";
		}
		return false;
	}

	private static bool IsResolutionValue(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		bool flag = false;
		bool flag2 = false;
		foreach (char c in text)
		{
			if (char.IsDigit(c))
			{
				flag = true;
			}
			if (c == 'x' || c == 'X' || c == '*' || c == '×')
			{
				flag2 = true;
			}
		}
		return flag && flag2;
	}

	private static bool IsLikelySettingValue(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		if (text == "开启" || text == "关闭" || text == "打开" || text == "全屏" || text == "窗口" || text == "中文" || text == "English" || text == "ON" || text == "OFF")
		{
			return true;
		}
		if (text.EndsWith("%") || text.EndsWith("％"))
		{
			return true;
		}
		bool flag = false;
		bool flag2 = false;
		foreach (char c in text)
		{
			if (char.IsDigit(c))
			{
				flag = true;
			}
			if (c == 'x' || c == 'X' || c == '*' || c == '×')
			{
				flag2 = true;
			}
		}
		return flag && flag2;
	}

	private static bool IsSettingTextAlreadyCovered(List<SettingItem> list, string text)
	{
		if (list == null || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		foreach (SettingItem item in list)
		{
			if (item != null && !string.IsNullOrWhiteSpace(item.Name) && (item.Name == text || text.Contains(item.Name) || item.Name.Contains(text)))
			{
				return true;
			}
		}
		return false;
	}

	private static object GetActiveObject(Type type)
	{
		if (type == null)
		{
			return null;
		}
		try
		{
			Array array = FindObjectsOfType(type);
			if (array == null || array.Length == 0)
			{
				return null;
			}
			foreach (object item in array)
			{
				try
				{
					PropertyInfo property = type.GetProperty("isActiveAndEnabled", BindingFlags.Instance | BindingFlags.Public);
					if (property != null && (bool)property.GetValue(item))
					{
						return item;
					}
				}
				catch
				{
				}
			}
			return array.GetValue(0);
		}
		catch
		{
			return null;
		}
	}

	private static object GetFieldValue(object obj, string fieldName)
	{
		if (obj == null || string.IsNullOrEmpty(fieldName))
		{
			return null;
		}
		try
		{
			FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return field?.GetValue(obj);
		}
		catch
		{
			return null;
		}
	}

	private static void AddSettingFromField(List<SettingItem> list, object owner, string fieldName, string name, ref float order)
	{
		AddSettingFromValue(list, GetFieldValue(owner, fieldName), name, ref order);
	}

	private static void AddSettingFromValue(List<SettingItem> list, object component, string name, ref float order)
	{
		if (list == null || component == null)
		{
			return;
		}
		SettingItem settingItem = CreateSettingItemFromComponent(component, name, order);
		if (settingItem != null && !ContainsSettingComponent(list, settingItem.Component))
		{
			settingItem.Name = name;
			ApplyKnownSettingOptions(settingItem);
			settingItem.ScreenY = order;
			list.Add(settingItem);
			order += 100f;
			Log.LogInfo((object)$"[设置精准采集] {name}: {settingItem.Type}");
		}
	}

	private static void ApplyKnownSettingOptions(SettingItem item)
	{
		if (item == null || item.Type != SettingItem.SettingType.Dropdown)
		{
			return;
		}
		if (item.Name == "男主声音" || item.Name == "外置音轨" || item.Name == "敏感词消音" || item.Name == "字幕开关")
		{
			item.Options = new string[2] { "关闭", "开启" };
		}
		else if (item.Name == "显示模式")
		{
			item.Options = new string[2] { "全屏", "窗口" };
		}
	}

	private static SettingItem FindReturnButtonSetting(OptionItem[] visibleTexts, ref float order)
	{
		if (visibleTexts == null)
		{
			return null;
		}
		foreach (OptionItem optionItem in visibleTexts)
		{
			if (optionItem == null || optionItem.ClickableComponent == null)
			{
				continue;
			}
			string text = (optionItem.Text ?? "").Trim();
			if (text == "返回" || text == "关闭" || text == "Back")
			{
				SettingItem settingItem = new SettingItem();
				settingItem.Name = text;
				settingItem.Type = SettingItem.SettingType.Button;
				settingItem.Component = optionItem.ClickableComponent;
				settingItem.ClickComponent = optionItem.ClickableComponent;
				settingItem.ScreenY = order;
				settingItem.ScreenX = optionItem.ScreenX;
				settingItem.HasScreenPosition = optionItem.HasScreenPosition;
				order += 100f;
				Log.LogInfo((object)$"[设置精准采集] 返回按钮: screen=({optionItem.ScreenX},{optionItem.ScreenY}), hasPos={optionItem.HasScreenPosition}");
				return settingItem;
			}
		}
		return null;
	}

	private static void AddVisibleSettingControls(List<SettingItem> list, OptionItem[] visibleTexts, ref float order)
	{
		try
		{
			AddVisibleControlsOfType(list, Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI"), SettingItem.SettingType.Slider, visibleTexts, ref order);
			AddVisibleControlsOfType(list, Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro"), SettingItem.SettingType.Dropdown, visibleTexts, ref order);
			AddVisibleControlsOfType(list, Type.GetType("UnityEngine.UI.Dropdown, UnityEngine.UI"), SettingItem.SettingType.Dropdown, visibleTexts, ref order);
			AddVisibleControlsOfType(list, Type.GetType("UnityEngine.UI.Toggle, UnityEngine.UI"), SettingItem.SettingType.Toggle, visibleTexts, ref order);
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("扫描可见设置控件失败: " + ex.Message));
		}
	}

	private static void AddVisibleControlsOfType(List<SettingItem> list, Type type, SettingItem.SettingType settingType, OptionItem[] visibleTexts, ref float order)
	{
		if (type == null)
		{
			return;
		}
		Array array = FindObjectsOfType(type);
		if (array == null || array.Length == 0)
		{
			return;
		}
		foreach (object item in array)
		{
			if (!IsComponentActiveAndVisible(item) || ContainsSettingComponent(list, item))
			{
				continue;
			}
			SettingItem settingItem = CreateSettingItemFromComponent(item, "", order);
			if (settingItem == null || settingItem.Type != settingType || ContainsSettingComponent(list, settingItem.Component))
			{
				continue;
			}
			SetSettingScreenPosition(settingItem);
			settingItem.Name = GuessSettingNameFromPosition(settingItem, visibleTexts, settingType);
			if (IsWeakSettingName(settingItem.Name))
			{
				continue;
			}
			settingItem.ScreenY = order;
			ApplyKnownSettingOptions(settingItem);
			list.Add(settingItem);
			order += 100f;
			Log.LogInfo((object)$"[设置控件扫描] {settingItem.Name}: {settingItem.Type}");
		}
	}

	private static bool IsComponentActiveAndVisible(object component)
	{
		if (component == null)
		{
			return false;
		}
		try
		{
			PropertyInfo property = component.GetType().GetProperty("isActiveAndEnabled", BindingFlags.Instance | BindingFlags.Public);
			if (property != null && !(bool)property.GetValue(component))
			{
				return false;
			}
			object gameObjectFromComponent = GetGameObjectFromComponent(component);
			return IsGameObjectActiveInHierarchy(gameObjectFromComponent);
		}
		catch
		{
			return false;
		}
	}

	private static void SetSettingScreenPosition(SettingItem item)
	{
		if (item == null || item.Component == null)
		{
			return;
		}
		try
		{
			object gameObjectFromComponent = GetGameObjectFromComponent(item.Component);
			if (TryGetScreenPosition(gameObjectFromComponent, out var x, out var y))
			{
				item.ScreenX = x;
				item.ScreenY = y;
				item.HasScreenPosition = true;
			}
		}
		catch
		{
		}
	}

	private static string GuessSettingNameFromPosition(SettingItem item, OptionItem[] visibleTexts, SettingItem.SettingType settingType)
	{
		if (visibleTexts == null || item == null || !item.HasScreenPosition)
		{
			return "";
		}
		OptionItem optionItem = null;
		float num = float.MaxValue;
		foreach (OptionItem optionItem2 in visibleTexts)
		{
			if (optionItem2 == null || string.IsNullOrWhiteSpace(optionItem2.Text))
			{
				continue;
			}
			string text = CleanSettingName(optionItem2.Text);
			if (IsWeakSettingName(text) || text == "返回" || text == "系统设置")
			{
				continue;
			}
			float num2 = Math.Abs(optionItem2.ScreenY - item.ScreenY);
			float num3 = Math.Abs(optionItem2.ScreenX - item.ScreenX);
			float num4 = num2 * 3f + num3;
			if (num2 < 90f && num4 < num)
			{
				num = num4;
				optionItem = optionItem2;
			}
		}
		if (optionItem != null)
		{
			return CleanSettingName(optionItem.Text);
		}
		switch (settingType)
		{
		case SettingItem.SettingType.Slider:
			return "音量";
		case SettingItem.SettingType.Dropdown:
			return "选项";
		case SettingItem.SettingType.Toggle:
			return "开关";
		default:
			return "";
		}
	}

	private static bool ContainsSettingComponent(List<SettingItem> items, object component)
	{
		if (items == null || component == null)
		{
			return false;
		}
		foreach (SettingItem item in items)
		{
			if (item != null && item.Component == component)
			{
				return true;
			}
		}
		return false;
	}

	private static object FindComponentNear(object component, Type targetType)
	{
		if (component == null || targetType == null)
		{
			return null;
		}
		try
		{
			if (targetType.IsAssignableFrom(component.GetType()))
			{
				return component;
			}
			object gameObject = GetGameObjectFromComponent(component);
			if (gameObject != null)
			{
				object obj = InvokeComponentLookup(gameObject, "GetComponent", targetType);
				if (obj != null)
				{
					return obj;
				}
				obj = InvokeComponentLookup(gameObject, "GetComponentInChildren", targetType);
				if (obj != null)
				{
					return obj;
				}
			}
			object obj2 = component;
			for (int i = 0; i < 6; i++)
			{
				object parentGameObject = GetParentGameObject(obj2);
				if (parentGameObject == null)
				{
					break;
				}
				object obj3 = InvokeComponentLookup(parentGameObject, "GetComponent", targetType);
				if (obj3 != null)
				{
					return obj3;
				}
				obj3 = InvokeComponentLookup(parentGameObject, "GetComponentInChildren", targetType);
				if (obj3 != null)
				{
					return obj3;
				}
				obj2 = parentGameObject;
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("查找设置控件失败: " + ex.Message));
		}
		return null;
	}

	private static object InvokeComponentLookup(object gameObject, string methodName, Type targetType)
	{
		if (gameObject == null || targetType == null)
		{
			return null;
		}
		MethodInfo method = gameObject.GetType().GetMethod(methodName, new Type[1] { typeof(Type) });
		if (method == null)
		{
			return null;
		}
		return method.Invoke(gameObject, new object[1] { targetType });
	}

	private static object GetGameObjectFromComponent(object component)
	{
		if (component == null)
		{
			return null;
		}
		PropertyInfo property = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public);
		return property?.GetValue(component);
	}

	private static object GetParentGameObject(object componentOrGameObject)
	{
		if (componentOrGameObject == null)
		{
			return null;
		}
		try
		{
			object gameObject = GetGameObjectFromComponent(componentOrGameObject) ?? componentOrGameObject;
			PropertyInfo property = gameObject.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public);
			object value = property?.GetValue(gameObject);
			if (value == null)
			{
				return null;
			}
			PropertyInfo property2 = value.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public);
			object value2 = property2?.GetValue(value);
			if (value2 == null)
			{
				return null;
			}
			PropertyInfo property3 = value2.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public);
			return property3?.GetValue(value2);
		}
		catch
		{
			return null;
		}
	}

	private static bool TryGetScreenPosition(object gameObject, out float x, out float y)
	{
		x = 0f;
		y = 0f;
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			Type type = Type.GetType("UnityEngine.Camera, UnityEngine");
			object obj = null;
			if (type != null)
			{
				PropertyInfo property = type.GetProperty("main", BindingFlags.Static | BindingFlags.Public);
				obj = property?.GetValue(null);
			}
			PropertyInfo property2 = gameObject.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public);
			object value = property2?.GetValue(gameObject);
			if (value == null)
			{
				return false;
			}
			PropertyInfo property3 = value.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public);
			object value2 = property3?.GetValue(value);
			if (value2 == null || obj == null || type == null)
			{
				return false;
			}
			MethodInfo method = type.GetMethod("WorldToScreenPoint", new Type[1] { value2.GetType() });
			object obj2 = method?.Invoke(obj, new object[1] { value2 });
			if (obj2 == null)
			{
				return false;
			}
			PropertyInfo property4 = obj2.GetType().GetProperty("x");
			PropertyInfo property5 = obj2.GetType().GetProperty("y");
			if (property4 == null || property5 == null)
			{
				return false;
			}
			x = Convert.ToSingle(property4.GetValue(obj2));
			y = Convert.ToSingle(property5.GetValue(obj2));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string FindBestSettingName(SettingItem setting, OptionItem[] texts)
	{
		if (setting == null)
		{
			return "";
		}
		string text = CleanSettingName(setting.Name);
		if (!IsWeakSettingName(text))
		{
			return text;
		}
		if (texts != null)
		{
			OptionItem optionItem = null;
			float num = float.MaxValue;
			foreach (OptionItem optionItem2 in texts)
			{
				if (optionItem2 == null)
				{
					continue;
				}
				string text2 = CleanSettingName(optionItem2.Text);
				if (IsWeakSettingName(text2))
				{
					continue;
				}
				float num2 = Math.Abs(optionItem2.ScreenY - setting.ScreenY);
				if (num2 < num)
				{
					num = num2;
					optionItem = optionItem2;
				}
			}
			if (optionItem != null && num < 80f)
			{
				return CleanSettingName(optionItem.Text);
			}
		}
		switch (setting.Type)
		{
		case SettingItem.SettingType.Slider:
			return "滑块";
		case SettingItem.SettingType.Toggle:
			return "开关";
		case SettingItem.SettingType.Dropdown:
			return "选项";
		default:
			return "设置项";
		}
	}

	private static string CleanSettingName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "";
		}
		string text = name.Trim();
		string[] array = new string[12]
		{
			"开启", "关闭", "打开", "关", "开", "ON", "OFF", "On", "Off", "%", "％", "："
		};
		foreach (string oldValue in array)
		{
			text = text.Replace(oldValue, "");
		}
		return text.Trim();
	}

	private static bool IsWeakSettingName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return true;
		}
		string text = name.Trim();
		return text.Length <= 1 || text == "开启" || text == "关闭" || text == "打开" || text == "开" || text == "关" || text == "ON" || text == "OFF" || text.EndsWith("%") || text.EndsWith("％");
	}

	private static string GetDropdownOptionText(object options, int index)
	{
		try
		{
			PropertyInfo property = options.GetType().GetProperty("Item");
			object value = property?.GetValue(options, new object[1] { index });
			if (value != null)
			{
				PropertyInfo property2 = value.GetType().GetProperty("text");
				string text = property2?.GetValue(value) as string;
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text.Trim();
				}
			}
		}
		catch
		{
		}
		return $"选项 {index + 1}";
	}

	private static string GetSettingValueText(SettingItem item)
	{
		if (item == null)
		{
			return "";
		}
		RefreshSettingValue(item);
		switch (item.Type)
		{
		case SettingItem.SettingType.Slider:
			if (item.MaxValue > item.MinValue)
			{
				float num = (item.Value - item.MinValue) / (item.MaxValue - item.MinValue) * 100f;
				return $"{(int)Math.Round(num)}%";
			}
			return item.Value.ToString("0.00");
		case SettingItem.SettingType.Toggle:
			if (!item.IsOn)
			{
				return "关闭";
			}
			return "开启";
		case SettingItem.SettingType.Dropdown:
			if (item.Options != null && item.SelectedIndex >= 0 && item.SelectedIndex < item.Options.Length)
			{
				return item.Options[item.SelectedIndex];
			}
			return $"第 {item.SelectedIndex + 1} 项";
		case SettingItem.SettingType.Button:
			return "按钮";
		case SettingItem.SettingType.Text:
			return "";
		default:
			return "";
		}
	}

	private static void RefreshSettingValue(SettingItem item)
	{
		if (item == null || item.Component == null)
		{
			return;
		}
		try
		{
			switch (item.Type)
			{
			case SettingItem.SettingType.Slider:
			{
				PropertyInfo property = item.Component.GetType().GetProperty("value");
				if (property != null)
				{
					item.Value = Convert.ToSingle(property.GetValue(item.Component));
				}
				PropertyInfo property2 = item.Component.GetType().GetProperty("minValue");
				if (property2 != null)
				{
					item.MinValue = Convert.ToSingle(property2.GetValue(item.Component));
				}
				PropertyInfo property3 = item.Component.GetType().GetProperty("maxValue");
				if (property3 != null)
				{
					item.MaxValue = Convert.ToSingle(property3.GetValue(item.Component));
				}
				break;
			}
			case SettingItem.SettingType.Toggle:
			{
				PropertyInfo property5 = item.Component.GetType().GetProperty("isOn");
				if (property5 != null)
				{
					item.IsOn = (bool)property5.GetValue(item.Component);
				}
				break;
			}
			case SettingItem.SettingType.Dropdown:
			{
				PropertyInfo property4 = item.Component.GetType().GetProperty("value");
				if (property4 != null)
				{
					item.SelectedIndex = (int)property4.GetValue(item.Component);
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("刷新设置值失败: " + ex.Message));
		}
	}

	private static void SpeakCurrentSetting()
	{
		if (_settings == null || _settings.Length == 0)
		{
			TolkHelper.Speak("没有设置项", interrupt: true);
			return;
		}
		if (_currentSettingIndex < 0)
		{
			_currentSettingIndex = 0;
		}
		if (_currentSettingIndex >= _settings.Length)
		{
			_currentSettingIndex = _settings.Length - 1;
		}
		SettingItem settingItem = _settings[_currentSettingIndex];
		string settingValueText = GetSettingValueText(settingItem);
		string text = "";
		switch (settingItem.Type)
		{
		case SettingItem.SettingType.Toggle:
			text = "，回车切换";
			break;
		case SettingItem.SettingType.Slider:
			text = "，左右调整";
			break;
		case SettingItem.SettingType.Dropdown:
			text = "，左右切换";
			break;
		case SettingItem.SettingType.Button:
			text = "，回车确认";
			break;
		case SettingItem.SettingType.Text:
			text = "";
			break;
		}
		PlayGameSound("Highlight");
		string text2 = string.IsNullOrWhiteSpace(settingValueText) ? settingItem.Name : (settingItem.Name + " " + settingValueText);
		TolkHelper.Speak($"{_currentSettingIndex + 1} / {_settings.Length}: {text2}{text}", interrupt: true);
	}

	private static bool ActivateCurrentSetting()
	{
		if (_settings == null || _settings.Length == 0)
		{
			return false;
		}
		SettingItem settingItem = _settings[_currentSettingIndex];
		try
		{
			if (settingItem.Type == SettingItem.SettingType.Toggle)
			{
				PropertyInfo property = settingItem.Component.GetType().GetProperty("isOn");
				if (property != null && property.CanWrite)
				{
					PlayGameSound("Click");
					bool flag = !(bool)property.GetValue(settingItem.Component);
					property.SetValue(settingItem.Component, flag);
					settingItem.IsOn = flag;
					InvokeValueChanged(settingItem.Component, flag);
					SpeakCurrentSetting();
					return true;
				}
			}
			if (settingItem.Type == SettingItem.SettingType.Button || settingItem.Type == SettingItem.SettingType.Dropdown)
			{
				if (settingItem.Type == SettingItem.SettingType.Dropdown && ToggleDropdownIfBinary(settingItem))
				{
					PlayGameSound("Click");
					SpeakCurrentSetting();
					return true;
				}
				object component = settingItem.ClickComponent ?? settingItem.Component;
				if (component != null && ClickComponent(component))
				{
					PlayGameSound((settingItem.Type == SettingItem.SettingType.Button && (settingItem.Name == "返回" || settingItem.Name == "Back" || settingItem.Name == "关闭")) ? "Back" : "Click");
					Thread.Sleep(50);
					if (settingItem.Type == SettingItem.SettingType.Button)
					{
						if (settingItem.Name == "返回" || settingItem.Name == "Back" || settingItem.Name == "关闭")
						{
							ForceExitSettingsScene();
						}
						TolkHelper.Speak("返回", interrupt: true);
						MarkNeedDetect();
					}
					else
					{
						SpeakCurrentSetting();
					}
					return true;
				}
				if (settingItem.Type == SettingItem.SettingType.Button && settingItem.HasScreenPosition)
				{
					PlayGameSound((settingItem.Name == "返回" || settingItem.Name == "Back" || settingItem.Name == "关闭") ? "Back" : "Click");
					Log.LogInfo((object)$"[设置] 组件点击失败，使用坐标点击 {settingItem.Name}: ({settingItem.ScreenX},{settingItem.ScreenY})");
					TolkHelper.Speak(settingItem.Name, interrupt: true);
					ClickAt((int)settingItem.ScreenX, (int)settingItem.ScreenY);
					if (settingItem.Name == "返回" || settingItem.Name == "Back" || settingItem.Name == "关闭")
					{
						ForceExitSettingsScene();
					}
					MarkNeedDetect();
					return true;
				}
			}
			if (settingItem.Type == SettingItem.SettingType.Text)
			{
				SpeakCurrentSetting();
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("激活设置项失败: " + ex.Message));
			TolkHelper.Speak("操作失败", interrupt: true);
		}
		return false;
	}

	private static bool ToggleDropdownIfBinary(SettingItem item)
	{
		if (item == null || item.Type != SettingItem.SettingType.Dropdown || item.Component == null)
		{
			return false;
		}
		RefreshSettingValue(item);
		if (item.Options == null || item.Options.Length != 2)
		{
			return false;
		}
		try
		{
			int num = item.SelectedIndex == 0 ? 1 : 0;
			PropertyInfo property = item.Component.GetType().GetProperty("value");
			if (property == null || !property.CanWrite)
			{
				return false;
			}
			property.SetValue(item.Component, num);
			item.SelectedIndex = num;
			InvokeValueChanged(item.Component, num);
			return true;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("切换二值下拉框失败: " + ex.Message));
			return false;
		}
	}

	private static bool ActivateReturnSetting()
	{
		if (_settings == null || _settings.Length == 0)
		{
			return false;
		}
		for (int i = 0; i < _settings.Length; i++)
		{
			SettingItem settingItem = _settings[i];
			if (settingItem != null && settingItem.Type == SettingItem.SettingType.Button && (settingItem.Name == "返回" || settingItem.Name == "关闭" || settingItem.Name == "Back"))
			{
				_currentSettingIndex = i;
				if (ActivateCurrentSetting())
				{
					return true;
				}
			}
		}
		return false;
	}

	private static void ForceExitSettingsScene()
	{
		try
		{
			_ignoreSettingsUntilUtc = DateTime.UtcNow.AddSeconds(2.0);
			Type type = Type.GetType("UnityEngine.SceneManagement.SceneManager, UnityEngine.CoreModule");
			if (type != null)
			{
				MethodInfo method = type.GetMethod("UnloadSceneAsync", new Type[1] { typeof(string) });
				method?.Invoke(null, new object[1] { "Settings" });
				Log.LogInfo((object)"[设置] 已请求卸载 Settings 场景");
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("卸载 Settings 场景失败: " + ex.Message));
		}
		_inSettingsMode = false;
		_settings = new SettingItem[0];
		_currentSettingIndex = 0;
		_currentUIState = UIState.Unknown;
		_lastDetectedSignature = "";
		MarkNeedDetect();
	}

	private static void InvokeValueChanged(object component, object value)
	{
		if (component == null)
		{
			return;
		}
		try
		{
			PropertyInfo property = component.GetType().GetProperty("onValueChanged");
			object obj = property?.GetValue(component);
			MethodInfo method = obj?.GetType().GetMethod("Invoke");
			if (method != null)
			{
				method.Invoke(obj, new object[1] { value });
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("触发设置变更事件失败: " + ex.Message));
		}
	}

	private static void AdjustSettingValue(SettingItem item, int direction)
	{
		if (item == null || item.Component == null)
		{
			return;
		}
		try
		{
			switch (item.Type)
			{
			case SettingItem.SettingType.Slider:
			{
				Type type2 = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI");
				if (!(type2 != null))
				{
					break;
				}
				PropertyInfo property3 = type2.GetProperty("value");
				if (!(property3 != null))
				{
					break;
				}
				float num = (item.MaxValue - item.MinValue) / 10f;
				float val3 = item.Value + (float)direction * num;
				val3 = Math.Max(item.MinValue, Math.Min(val3, item.MaxValue));
				if (Math.Abs(val3 - item.Value) > 0.001f)
				{
					PlayGameSound("Highlight");
				}
				property3.SetValue(item.Component, val3);
				item.Value = val3;
				InvokeValueChanged(item.Component, val3);
				break;
			}
			case SettingItem.SettingType.Dropdown:
			{
				Type type = item.Component.GetType();
				PropertyInfo property = type.GetProperty("value");
				if (!(property != null))
				{
					break;
				}
				int val = item.SelectedIndex + direction;
				int val2 = ((item.Options != null) ? (item.Options.Length - 1) : 10);
				val = Math.Max(0, Math.Min(val, val2));
				if (val == item.SelectedIndex)
				{
					break;
				}
				PlayGameSound("Highlight");
				property.SetValue(item.Component, val);
				item.SelectedIndex = val;
				InvokeValueChanged(item.Component, val);
				break;
			}
			case SettingItem.SettingType.Toggle:
			case SettingItem.SettingType.Button:
				break;
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("调整设置值失败: " + ex.Message));
			TolkHelper.Speak("调整失败", interrupt: true);
		}
	}

	private static bool IsGameWindowActive()
	{
		try
		{
			IntPtr foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return false;
			}
			GetWindowThreadProcessId(foregroundWindow, out var lpdwProcessId);
			return lpdwProcessId == _gameProcessId;
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("检查窗口焦点失败: " + ex.Message));
			}
			return true;
		}
	}

	private static void ResolveStorylineTypes()
	{
		if (_storylineTypesResolved)
		{
			return;
		}
		_storylineTypesResolved = true;
		try
		{
			_chapterStorylineControllerType = Type.GetType("ChapterStorylineController, Assembly-CSharp");
			if (_chapterStorylineControllerType != null)
			{
				Log.LogInfo((object)"找到 ChapterStorylineController 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 ChapterStorylineController 类型，故事线精准支持不可用");
			}
			_storylineUIManagerType = Type.GetType("StorylineUIManager, Assembly-CSharp");
			if (_storylineUIManagerType != null)
			{
				Log.LogInfo((object)"找到 StorylineUIManager 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 StorylineUIManager 类型");
			}
			_gameControllerType = Type.GetType("GameController, Assembly-CSharp");
			if (_gameControllerType != null)
			{
				Log.LogInfo((object)"找到 GameController 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 GameController 类型");
			}
			_gameNodeType = Type.GetType("GameNode, Assembly-CSharp");
			if (_gameNodeType != null)
			{
				Log.LogInfo((object)"找到 GameNode 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 GameNode 类型");
			}
			_gameOptionType = Type.GetType("GameOption, Assembly-CSharp");
			if (_gameOptionType != null)
			{
				Log.LogInfo((object)"找到 GameOption 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 GameOption 类型");
			}
			_progressTreeNodeComponentType = Type.GetType("ProgressTreeNodeComponent, Assembly-CSharp");
			if (_progressTreeNodeComponentType != null)
			{
				Log.LogInfo((object)"找到 ProgressTreeNodeComponent 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 ProgressTreeNodeComponent 类型");
			}
			_progressTreeGraphControllerType = Type.GetType("ProgressTreeGraphController, Assembly-CSharp");
			if (_progressTreeGraphControllerType != null)
			{
				Log.LogInfo((object)"找到 ProgressTreeGraphController 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 ProgressTreeGraphController 类型");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析故事线类型失败: " + ex.Message));
		}
	}

	private static bool IsInStorylinePage()
	{
		try
		{
			ResolveStorylineTypes();
			if (_storylineUIManagerType == null)
			{
				return false;
			}
			Array array = FindObjectsOfType(_storylineUIManagerType);
			if (array == null || array.Length == 0)
			{
				return false;
			}
			PropertyInfo property = _storylineUIManagerType.GetProperty("IsStorylineDisplayActive", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo chapterPanelField = _storylineUIManagerType.GetField("chapterSelectionPanel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo displayPanelField = _storylineUIManagerType.GetField("storylineDisplayPanel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo mainMenuToggleField = _storylineUIManagerType.GetField("mainMenuToggleHide", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (object item in array)
			{
				try
				{
					if (IsToggleHideGameObjectActive(mainMenuToggleField?.GetValue(item)))
					{
						ManualLogSource log0 = Log;
						if (log0 != null)
						{
							log0.LogDebug((object)"[Storyline detection] main menu active");
						}
						return false;
					}
					if (property != null && (bool)property.GetValue(item))
					{
						ManualLogSource log = Log;
						if (log != null)
						{
							log.LogDebug((object)"[Storyline detection] display panel active");
						}
						return true;
					}
					if (IsGameObjectActiveInHierarchy(displayPanelField?.GetValue(item)))
					{
						ManualLogSource log2 = Log;
						if (log2 != null)
						{
							log2.LogDebug((object)"[Storyline detection] display object active");
						}
						return true;
					}
					if (IsGameObjectActiveInHierarchy(chapterPanelField?.GetValue(item)))
					{
						ManualLogSource log4 = Log;
						if (log4 != null)
						{
							log4.LogDebug((object)"[Storyline detection] chapter panel active");
						}
						return true;
					}
				}
				catch
				{
				}
			}
			ManualLogSource log3 = Log;
			if (log3 != null)
			{
				log3.LogDebug((object)"[Storyline detection] panels inactive");
			}
			return false;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("Storyline page detection failed: " + ex.Message));
			return false;
		}
	}

	private static bool IsGameObjectActiveInHierarchy(object gameObject)
	{
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			PropertyInfo property = gameObject.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public);
			return property != null && (bool)property.GetValue(gameObject);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsToggleHideGameObjectActive(object toggleHide)
	{
		if (toggleHide == null)
		{
			return false;
		}
		try
		{
			PropertyInfo gameObjectProperty = toggleHide.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public);
			return IsGameObjectActiveInHierarchy(gameObjectProperty?.GetValue(toggleHide));
		}
		catch
		{
			return false;
		}
	}
	private static ChapterInfo[] GetStorylineChapters()
	{
		try
		{
			ResolveStorylineTypes();
			if (_chapterStorylineControllerType == null)
			{
				return new ChapterInfo[0];
			}
			Array array = FindObjectsOfType(_chapterStorylineControllerType);
			if (array == null || array.Length == 0)
			{
				Log.LogInfo((object)"未找到 ChapterStorylineController 实例");
				return new ChapterInfo[0];
			}
			object obj = null;
			foreach (object item in array)
			{
				try
				{
					PropertyInfo property = _chapterStorylineControllerType.GetProperty("isActiveAndEnabled");
					if (property != null && (bool)property.GetValue(item))
					{
						obj = item;
						break;
					}
				}
				catch
				{
				}
			}
			if (obj == null)
			{
				obj = array.GetValue(0);
			}
			Log.LogInfo((object)"找到 ChapterStorylineController 实例");
			MethodInfo method = _chapterStorylineControllerType.GetMethod("GetChapterButtons", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				Log.LogWarning((object)"未找到 GetChapterButtons 方法");
				return new ChapterInfo[0];
			}
			Array array2 = (Array)method.Invoke(obj, null);
			if (array2 == null || array2.Length == 0)
			{
				Log.LogInfo((object)"没有章节按钮");
				return new ChapterInfo[0];
			}
			Log.LogInfo((object)$"找到 {array2.Length} 个章节按钮");
			List<ChapterInfo> list = new List<ChapterInfo>();
			Type type = array2.GetValue(0).GetType();
			FieldInfo field = type.GetField("chapterName", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field2 = type.GetField("button", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field3 = type.GetField("progressPercentageText", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field4 = type.GetField("lockedOverlayImage", BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < array2.Length; i++)
			{
				object value = array2.GetValue(i);
				ChapterInfo chapterInfo = new ChapterInfo();
				chapterInfo.Index = i;
				chapterInfo.ChapterNumber = i + 1;
				if (field != null)
				{
					chapterInfo.Name = (string)field.GetValue(value);
				}
				else
				{
					try
					{
						MethodInfo method2 = _chapterStorylineControllerType.GetMethod("GetChapterName", BindingFlags.Instance | BindingFlags.Public);
						if (method2 != null)
						{
							chapterInfo.Name = (string)method2.Invoke(obj, new object[1] { i + 1 });
						}
					}
					catch
					{
					}
				}
				if (string.IsNullOrEmpty(chapterInfo.Name))
				{
					chapterInfo.Name = $"第 {i + 1} 章";
				}
				if (field2 != null)
				{
					chapterInfo.ButtonComponent = field2.GetValue(value);
				}
				if (field3 != null)
				{
					object value2 = field3.GetValue(value);
					if (value2 != null)
					{
						PropertyInfo property2 = value2.GetType().GetProperty("text");
						if (property2 != null)
						{
							chapterInfo.ProgressText = (string)property2.GetValue(value2);
						}
					}
				}
				if (field4 != null)
				{
					object value3 = field4.GetValue(value);
					if (value3 != null)
					{
						PropertyInfo property3 = value3.GetType().GetProperty("isActiveAndEnabled");
						if (property3 != null)
						{
							chapterInfo.IsLocked = (bool)property3.GetValue(value3);
						}
					}
				}
				if (!chapterInfo.IsLocked)
				{
					try
					{
						MethodInfo method3 = _chapterStorylineControllerType.GetMethod("GetChapterUnlockStatus", BindingFlags.Instance | BindingFlags.Public);
						if (method3 != null)
						{
							chapterInfo.IsLocked = !(bool)method3.Invoke(obj, new object[1] { i + 1 });
						}
					}
					catch (Exception ex)
					{
						Log.LogDebug((object)("获取解锁状态失败: " + ex.Message));
					}
				}
				try
				{
					MethodInfo method4 = _chapterStorylineControllerType.GetMethod("GetChapterProgressPublic", BindingFlags.Instance | BindingFlags.Public);
					if (method4 != null)
					{
						object obj4 = method4.Invoke(obj, new object[1] { i + 1 });
						if (obj4 != null)
						{
							Type type2 = obj4.GetType();
							FieldInfo field5 = type2.GetField("Item1");
							FieldInfo field6 = type2.GetField("Item2");
							if (field5 != null && field6 != null)
							{
								int num = (int)field5.GetValue(obj4);
								int num2 = (int)field6.GetValue(obj4);
								chapterInfo.ProgressReached = num;
								chapterInfo.ProgressTotal = num2;
								if (string.IsNullOrEmpty(chapterInfo.ProgressText) && num2 > 0)
								{
									chapterInfo.ProgressText = $"{num}/{num2}";
								}
							}
						}
					}
				}
				catch (Exception ex2)
				{
					Log.LogDebug((object)("获取章节进度失败: " + ex2.Message));
				}
				list.Add(chapterInfo);
			}
			return list.ToArray();
		}
		catch (Exception ex3)
		{
			Log.LogError((object)("获取故事线章节失败: " + ex3.GetType().Name + " - " + ex3.Message));
			Log.LogError((object)("堆栈: " + ex3.StackTrace));
			return new ChapterInfo[0];
		}
	}

	private static void EnterStorylineMode()
	{
		ChapterInfo[] storylineChapters = GetStorylineChapters();
		if (storylineChapters.Length == 0)
		{
			TolkHelper.Speak("没有找到章节", interrupt: true);
			return;
		}
		List<OptionItem> list = new List<OptionItem>();
		ChapterInfo[] array = storylineChapters;
		ChapterInfo[] array2 = array;
			foreach (ChapterInfo chapterInfo in array2.OrderBy((ChapterInfo c) => c.ChapterNumber).ThenBy((ChapterInfo c) => c.Index))
		{
			OptionItem optionItem = new OptionItem();
			string text = chapterInfo.Name;
			if (chapterInfo.IsLocked)
			{
				text += "（已锁定）";
			}
			else if (!string.IsNullOrEmpty(chapterInfo.ProgressText))
			{
				text = text + "（进度 " + chapterInfo.ProgressText + "）";
			}
			optionItem.Text = text;
			optionItem.ClickableComponent = chapterInfo.ButtonComponent;
			optionItem.ChapterInfo = chapterInfo;
			list.Add(optionItem);
		}
		_inStorylineMode = true;
		SetOptions(list.ToArray());
		_isHorizontalLayout = true;
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"[故事线] 强制设置为横向排列");
		}
		TolkHelper.Speak($"故事线页面，共 {storylineChapters.Length} 个章节，已进入章节选择模式。按左右光标切换章节，按回车进入", interrupt: true);
	}

	private static void BackToPreviousNode()
	{
		try
		{
			ResolveStorylineTypes();
			if (_gameControllerType == null)
			{
				TolkHelper.Speak("快退功能不可用", interrupt: true);
				return;
			}
			Array array = FindObjectsOfType(_gameControllerType);
			if (array == null || array.Length == 0)
			{
				TolkHelper.Speak("未找到游戏控制器", interrupt: true);
				return;
			}
			object value = array.GetValue(0);
			MethodInfo method = _gameControllerType.GetMethod("Back", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				TolkHelper.Speak("未找到快退方法", interrupt: true);
				return;
			}
			method.Invoke(value, null);
			TolkHelper.Speak("快退", interrupt: true);
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)"[故事线] 执行快退操作");
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogError((object)("快退失败: " + ex.Message));
			}
			TolkHelper.Speak("快退失败", interrupt: true);
		}
	}

	private static void JumpToCurrentNode()
	{
		try
		{
			ResolveStorylineTypes();
			if (_progressTreeGraphControllerType == null)
			{
				TolkHelper.Speak("跳转功能不可用", interrupt: true);
				return;
			}
			Array array = FindObjectsOfType(_progressTreeGraphControllerType);
			if (array == null || array.Length == 0)
			{
				TolkHelper.Speak("未找到进度树控制器", interrupt: true);
				return;
			}
			object obj = null;
			foreach (object item in array)
			{
				try
				{
					PropertyInfo property = _progressTreeGraphControllerType.GetProperty("isActiveAndEnabled");
					if (property != null && (bool)property.GetValue(item))
					{
						obj = item;
						break;
					}
				}
				catch
				{
				}
			}
			if (obj == null)
			{
				obj = array.GetValue(0);
			}
			MethodInfo method = _progressTreeGraphControllerType.GetMethod("JumpToCurrentNode", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				TolkHelper.Speak("未找到跳转方法", interrupt: true);
				return;
			}
			method.Invoke(obj, null);
			TolkHelper.Speak("跳转到当前节点", interrupt: true);
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)"[故事线] 跳转到当前节点");
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogError((object)("跳转失败: " + ex.Message));
			}
			TolkHelper.Speak("跳转失败", interrupt: true);
		}
	}

	private static OptionItem[] GetStorylineNodes()
	{
		try
		{
			ResolveStorylineTypes();
			if (_progressTreeNodeComponentType == null)
			{
				return new OptionItem[0];
			}
			Array array = FindObjectsOfType(_progressTreeNodeComponentType);
			if (array == null || array.Length == 0)
			{
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogInfo((object)"未找到节点组件");
				}
				return new OptionItem[0];
			}
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogInfo((object)$"找到 {array.Length} 个节点组件");
			}
			List<OptionItem> list = new List<OptionItem>();
			for (int i = 0; i < array.Length; i++)
			{
				object value = array.GetValue(i);
				OptionItem optionItem = new OptionItem();
				optionItem.ClickableComponent = value;
				FieldInfo field = _progressTreeNodeComponentType.GetField("node", BindingFlags.Instance | BindingFlags.Public);
				if (field != null)
				{
					object value2 = field.GetValue(value);
					if (value2 != null)
					{
						Type type = value2.GetType();
						FieldInfo field2 = type.GetField("nodeId", BindingFlags.Instance | BindingFlags.Public);
						FieldInfo field3 = type.GetField("overall", BindingFlags.Instance | BindingFlags.Public);
						FieldInfo field4 = type.GetField("chapterNumber", BindingFlags.Instance | BindingFlags.Public);
						FieldInfo layoutLayerField = type.GetField("layoutLayer", BindingFlags.Instance | BindingFlags.Public);
						FieldInfo layoutOrderField = type.GetField("layoutOrder", BindingFlags.Instance | BindingFlags.Public);
						string text = ((field2 != null) ? ((string)field2.GetValue(value2)) : $"节点{i + 1}");
						string text2 = ((field3 != null) ? ((string)field3.GetValue(value2)) : "");
						int num = ((field4 != null) ? ((int)field4.GetValue(value2)) : 0);
						int num2 = ((layoutLayerField != null) ? ((int)layoutLayerField.GetValue(value2)) : 0);
						int num3 = ((layoutOrderField != null) ? ((int)layoutOrderField.GetValue(value2)) : 0);
						optionItem.Index = BuildStorylineNodeSortKey(num, num2, num3, i);
						if (!string.IsNullOrEmpty(text2))
						{
							optionItem.Text = text2;
						}
						else
						{
							optionItem.Text = text;
						}
						try
						{
							Type type2 = Type.GetType("UnityEngine.Component, UnityEngine");
							if (type2 != null)
							{
								PropertyInfo property = type2.GetProperty("transform");
								if (property != null)
								{
									object value3 = property.GetValue(value);
									if (value3 != null)
									{
										Type type3 = value3.GetType();
										PropertyInfo property2 = type3.GetProperty("localPosition") ?? type3.GetProperty("position");
										if (property2 != null)
										{
											object value4 = property2.GetValue(value3);
											if (value4 != null)
											{
												Type type4 = value4.GetType();
												FieldInfo field5 = type4.GetField("y");
												FieldInfo field6 = type4.GetField("x");
												if (field5 != null && field6 != null)
												{
													optionItem.ScreenY = (float)field5.GetValue(value4);
													optionItem.ScreenX = (float)field6.GetValue(value4);
													optionItem.HasScreenPosition = true;
												}
											}
										}
									}
								}
							}
						}
						catch
						{
						}
						Log.LogInfo((object)$"[故事线排序] 节点={text}, chapter={num}, layer={num2}, order={num3}, key={optionItem.Index}, pos=({optionItem.ScreenX:F1},{optionItem.ScreenY:F1}), hasPos={optionItem.HasScreenPosition}");
					}
				}
				if (string.IsNullOrEmpty(optionItem.Text))
				{
					optionItem.Text = $"节点 {i + 1}";
				}
				list.Add(optionItem);
			}
			bool hasRenderPosition = list.Any((OptionItem o) => o.HasScreenPosition);
			list.Sort(delegate(OptionItem a, OptionItem b)
			{
				if (hasRenderPosition)
				{
					int num2 = GetStorylineColumnSortKey(a.ScreenX).CompareTo(GetStorylineColumnSortKey(b.ScreenX));
					if (num2 != 0)
					{
						return num2;
					}
					int num3 = b.ScreenY.CompareTo(a.ScreenY);
					if (num3 != 0)
					{
						return num3;
					}
				}
				int num4 = a.Index.CompareTo(b.Index);
				if (num4 != 0)
				{
					return num4;
				}
				return string.Compare(a.Text, b.Text, StringComparison.Ordinal);
			});
			return list.ToArray();
		}
		catch (Exception ex)
		{
			ManualLogSource log3 = Log;
			if (log3 != null)
			{
				log3.LogError((object)("获取节点列表失败: " + ex.Message));
			}
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogError((object)("堆栈: " + ex.StackTrace));
			}
			return new OptionItem[0];
		}
	}

	private static int BuildStorylineNodeSortKey(int chapterNumber, int layoutLayer, int layoutOrder, int fallbackIndex)
	{
		int num = Math.Max(0, chapterNumber);
		int num2 = Math.Max(0, layoutLayer);
		int num3 = Math.Max(0, layoutOrder);
		int num4 = Math.Max(0, Math.Min(fallbackIndex, 999));
		return num * 100000000 + num2 * 100000 + num3 * 1000 + num4;
	}

	private static int GetStorylineColumnSortKey(float x)
	{
		return (int)Math.Round(x / 80f);
	}

	private static void EnterNodeMode()
	{
		OptionItem[] storylineNodes = GetStorylineNodes();
		if (storylineNodes.Length == 0)
		{
			TolkHelper.Speak("没有找到节点", interrupt: true);
			return;
		}
		_inNodeMode = true;
		_storylineNodes = storylineNodes;
		_currentNodeIndex = 0;
		TolkHelper.Speak($"进入节点浏览模式，共 {storylineNodes.Length} 个节点。按上下光标切换节点，按回车跳转到该节点，按F3跳转到当前进度节点", interrupt: true);
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)$"[故事线] 进入节点浏览模式，共 {storylineNodes.Length} 个节点");
		}
	}

	private static void SpeakCurrentNode()
	{
		if (_storylineNodes != null && _storylineNodes.Length != 0 && _currentNodeIndex >= 0 && _currentNodeIndex < _storylineNodes.Length)
		{
			OptionItem optionItem = _storylineNodes[_currentNodeIndex];
			PlayGameSound("Highlight");
			TolkHelper.Speak($"{_currentNodeIndex + 1}/{_storylineNodes.Length} {optionItem.Text}", interrupt: true);
		}
	}

	private static void JumpToSelectedNode()
	{
		try
		{
			if (_storylineNodes == null || _storylineNodes.Length == 0 || _currentNodeIndex < 0 || _currentNodeIndex >= _storylineNodes.Length)
			{
				return;
			}
			OptionItem optionItem = _storylineNodes[_currentNodeIndex];
			object clickableComponent = optionItem.ClickableComponent;
			if (clickableComponent == null)
			{
				TolkHelper.Speak("节点组件为空", interrupt: true);
				return;
			}
			MethodInfo method = _progressTreeNodeComponentType.GetMethod("Skip2CurrentNode", BindingFlags.Instance | BindingFlags.Public);
			if (method != null)
			{
				PlayGameSound("Click");
				method.Invoke(clickableComponent, null);
				TolkHelper.Speak("跳转到 " + optionItem.Text, interrupt: true);
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogInfo((object)("[故事线] 跳转到节点: " + optionItem.Text));
				}
				return;
			}
			MethodInfo method2 = _progressTreeNodeComponentType.GetMethod("Confirm", BindingFlags.Instance | BindingFlags.Public);
			if (method2 != null)
			{
				PlayGameSound("Click");
				method2.Invoke(clickableComponent, null);
				TolkHelper.Speak("跳转到 " + optionItem.Text, interrupt: true);
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogInfo((object)("[故事线] 跳转到节点(Confirm): " + optionItem.Text));
				}
			}
			else
			{
				TolkHelper.Speak("未找到跳转方法", interrupt: true);
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log3 = Log;
			if (log3 != null)
			{
				log3.LogError((object)("跳转节点失败: " + ex.Message));
			}
			TolkHelper.Speak("跳转失败", interrupt: true);
		}
	}

	private static void ResolveQTETypes()
	{
		if (_qteTypesResolved)
		{
			return;
		}
		_qteTypesResolved = true;
		try
		{
			_qteControllerType = Type.GetType("QTEController, Assembly-CSharp");
			if (_qteControllerType != null)
			{
				Log.LogInfo((object)"找到 QTEController 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 QTEController 类型，QTE 自动跳过不可用");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析 QTE 类型失败: " + ex.Message));
		}
	}

	private static void ResolveTriggerAreaTypes()
	{
		if (_triggerAreaTypesResolved)
		{
			return;
		}
		_triggerAreaTypesResolved = true;
		try
		{
			_triggerAreaType = Type.GetType("TriggerArea, Assembly-CSharp");
			if (_triggerAreaType != null)
			{
				Log.LogInfo((object)"找到 TriggerArea 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 TriggerArea 类型，探索场景一键跳过不可用");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析 TriggerArea 类型失败: " + ex.Message));
		}
	}

	private static void ResolveSubtitleTypes()
	{
		if (!_subtitleTypesResolved)
		{
			_subtitleTypesResolved = true;
			try
			{
				_subtitleManagerType = Type.GetType("SubtitleManager, Assembly-CSharp");
				if (!(_subtitleManagerType != null))
				{
					Log.LogWarning((object)"未找到 SubtitleManager 类型，字幕区分不可用");
					return;
				}
				Log.LogInfo((object)"找到 SubtitleManager 类型");
			}
			catch (Exception ex)
			{
				Log.LogError((object)("解析字幕类型失败: " + ex.Message));
				return;
			}
		}
		if (_subtitleTextComponent != null)
		{
			return;
		}
		try
		{
			Array array = FindObjectsOfType(_subtitleManagerType);
			if (array == null || array.Length <= 0)
			{
				return;
			}
			object value = array.GetValue(0);
			PropertyInfo property = _subtitleManagerType.GetProperty("SubtitleText", BindingFlags.Instance | BindingFlags.Public);
			if (property != null)
			{
				_subtitleTextComponent = property.GetValue(value);
				if (_subtitleTextComponent != null)
				{
					Log.LogInfo((object)"获取到字幕文本组件（通过属性）");
				}
				return;
			}
			FieldInfo field = _subtitleManagerType.GetField("subtitleText", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null)
			{
				_subtitleTextComponent = field.GetValue(value);
				if (_subtitleTextComponent != null)
				{
					Log.LogInfo((object)"获取到字幕文本组件（通过字段）");
				}
			}
		}
		catch (Exception ex2)
		{
			Log.LogDebug((object)("获取字幕文本组件失败（可能还未初始化）: " + ex2.Message));
		}
	}

	private static void ApplyQTEPatches(Harmony harmony)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Expected O, but got Unknown
		try
		{
			ResolveQTETypes();
			if (_qteControllerType == null)
			{
				Log.LogWarning((object)"QTE 类型未找到，跳过 QTE 补丁");
				return;
			}
			MethodInfo method = _qteControllerType.GetMethod("StartQTE", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				Log.LogWarning((object)"未找到 StartQTE 方法");
				return;
			}
			Log.LogInfo((object)("找到 StartQTE 方法: " + method.DeclaringType.Name + "." + method.Name));
			HarmonyMethod val = new HarmonyMethod(typeof(Plugin).GetMethod("StartQTEPostfix", BindingFlags.Static | BindingFlags.NonPublic));
			harmony.Patch((MethodBase)method, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Log.LogInfo((object)"QTE StartQTE 补丁已应用（仅 postfix）");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("应用 QTE 补丁失败: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
		}
	}

	private static bool StartQTEPrefix(object __instance)
	{
		try
		{
			if (_autoQTEEnabled)
			{
				Log.LogInfo((object)"【自动过 QTE】检测到 QTE 开始，将在启动完成后自动跳过");
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.LogError((object)("【自动过 QTE】prefix 异常: " + ex.GetType().Name + " - " + ex.Message));
			return true;
		}
	}

	private static void StartQTEPostfix(object __instance)
	{
		try
		{
			if (_autoQTEEnabled)
			{
				Log.LogInfo((object)"【自动过 QTE】QTE 已启动，开始跳过...");
				if (TrySkipQTE(__instance))
				{
					Log.LogInfo((object)"【自动过 QTE】跳过成功");
					TolkHelper.Speak("QTE 已自动跳过", interrupt: true);
				}
				else
				{
					Log.LogWarning((object)"【自动过 QTE】跳过失败，请尝试按 F10 手动跳过");
					TolkHelper.Speak("QTE 自动跳过失败，请按 F10 试试", interrupt: true);
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("【自动过 QTE】postfix 异常: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
		}
	}

	private static bool TrySkipQTE(object qteController)
	{
		try
		{
			bool flag = false;
			Log.LogInfo((object)"【跳过 QTE】方式 1: 调用 OnQTEFinished 成功回调");
			FieldInfo field = _qteControllerType.GetField("OnQTEFinished", BindingFlags.Instance | BindingFlags.Public);
			if (field != null)
			{
				object value = field.GetValue(qteController);
				if (value != null)
				{
					MethodInfo method = value.GetType().GetMethod("Invoke");
					if (method != null)
					{
						method.Invoke(value, new object[1] { true });
						Log.LogInfo((object)"【跳过 QTE】方式 1 成功: 已触发成功回调");
						flag = true;
					}
				}
				else
				{
					Log.LogWarning((object)"【跳过 QTE】方式 1: OnQTEFinished 回调为 null");
				}
			}
			else
			{
				Log.LogWarning((object)"【跳过 QTE】方式 1: 找不到 OnQTEFinished 字段");
			}
			Log.LogInfo((object)"【跳过 QTE】方式 2: 禁用 QTEController 组件");
			try
			{
				PropertyInfo property = _qteControllerType.GetProperty("enabled");
				if (property != null)
				{
					property.SetValue(qteController, false);
					Log.LogInfo((object)"【跳过 QTE】方式 2 成功: 已禁用组件");
				}
			}
			catch (Exception ex)
			{
				Log.LogWarning((object)("【跳过 QTE】方式 2 失败: " + ex.Message));
			}
			Log.LogInfo((object)"【跳过 QTE】方式 3: 禁用游戏对象");
			try
			{
				PropertyInfo property2 = _qteControllerType.GetProperty("gameObject");
				if (property2 != null)
				{
					object value2 = property2.GetValue(qteController);
					if (value2 != null)
					{
						MethodInfo method2 = value2.GetType().GetMethod("SetActive");
						if (method2 != null)
						{
							method2.Invoke(value2, new object[1] { false });
							Log.LogInfo((object)"【跳过 QTE】方式 3 成功: 已禁用游戏对象");
						}
					}
				}
			}
			catch (Exception ex2)
			{
				Log.LogWarning((object)("【跳过 QTE】方式 3 失败: " + ex2.Message));
			}
			if (!flag)
			{
				Log.LogInfo((object)"【跳过 QTE】方式 4: 调用 StopQTE（兜底方案）");
				try
				{
					MethodInfo method3 = _qteControllerType.GetMethod("StopQTE", BindingFlags.Instance | BindingFlags.Public);
					if (method3 != null)
					{
						method3.Invoke(qteController, null);
						Log.LogInfo((object)"【跳过 QTE】方式 4: 已调用 StopQTE");
					}
				}
				catch (Exception ex3)
				{
					Log.LogWarning((object)("【跳过 QTE】方式 4 失败: " + ex3.Message));
				}
			}
			return flag;
		}
		catch (Exception ex4)
		{
			Log.LogError((object)("【跳过 QTE】异常: " + ex4.GetType().Name + " - " + ex4.Message));
			Log.LogError((object)("堆栈: " + ex4.StackTrace));
			return false;
		}
	}

	private static void SkipCurrentQTE()
	{
		try
		{
			Log.LogInfo((object)"【一键过 QTE】精准版开始执行...");
			ResolveQTETypes();
			if (_qteControllerType == null)
			{
				Log.LogWarning((object)"QTE 类型未找到，回退到万能版");
				TryAutoQTE();
				return;
			}
			Array array = FindObjectsOfType(_qteControllerType);
			if (array == null || array.Length == 0)
			{
				Log.LogInfo((object)"没有找到 QTEController 实例，回退到万能版");
				TolkHelper.Speak("当前没有检测到 QTE", interrupt: true);
				return;
			}
			Log.LogInfo((object)$"找到 {array.Length} 个 QTEController 实例");
			bool flag = false;
			foreach (object item in array)
			{
				try
				{
					Log.LogInfo((object)"【一键过 QTE】尝试跳过一个 QTEController 实例");
					if (TrySkipQTE(item))
					{
						Log.LogInfo((object)"【一键过 QTE】成功跳过一个 QTE");
						flag = true;
					}
				}
				catch (Exception ex)
				{
					Log.LogWarning((object)("处理单个 QTEController 失败: " + ex.Message));
				}
			}
			if (flag)
			{
				TolkHelper.Speak("QTE 已跳过", interrupt: true);
				return;
			}
			Log.LogInfo((object)"精准版跳过失败，回退到万能版");
			TryAutoQTE();
		}
		catch (Exception ex2)
		{
			Log.LogError((object)("【一键过 QTE】精准版异常: " + ex2.GetType().Name + " - " + ex2.Message));
			Log.LogError((object)("堆栈: " + ex2.StackTrace));
			TolkHelper.Speak("跳过 QTE 时出错", interrupt: true);
		}
	}

	private static void ToggleAutoQTE()
	{
		_autoQTEEnabled = !_autoQTEEnabled;
		string text = (_autoQTEEnabled ? "已开启" : "已关闭");
		Log.LogInfo((object)("自动过 QTE 模式: " + text));
		TolkHelper.Speak("自动过 QTE " + text, interrupt: true);
	}

	private static void SkipExploreScene()
	{
		try
		{
			Log.LogInfo((object)"【一键过探索场景】开始执行...");
			TolkHelper.Speak("正在尝试一键过探索场景", interrupt: true);
			ResolveTriggerAreaTypes();
			if (_triggerAreaType != null)
			{
				Array array = FindObjectsOfType(_triggerAreaType);
				if (array != null && array.Length > 0)
				{
					Log.LogInfo((object)$"找到 {array.Length} 个 TriggerArea 实例");
					int num = 0;
					foreach (object item in array)
					{
						try
						{
							FieldInfo field = _triggerAreaType.GetField("onClick", BindingFlags.Instance | BindingFlags.Public);
							if (field == null)
							{
								field = _triggerAreaType.GetField("onClick", BindingFlags.Instance | BindingFlags.NonPublic);
							}
							if (!(field != null))
							{
								continue;
							}
							object value = field.GetValue(item);
							if (value != null)
							{
								MethodInfo method = value.GetType().GetMethod("Invoke");
								if (method != null)
								{
									method.Invoke(value, null);
									num++;
									Log.LogInfo((object)"成功触发一个 TriggerArea 的 onClick 事件");
									Thread.Sleep(100);
								}
							}
						}
						catch (Exception ex)
						{
							Log.LogWarning((object)("处理单个 TriggerArea 失败: " + ex.Message));
						}
					}
					if (num > 0)
					{
						Log.LogInfo((object)$"【一键过探索场景】成功触发 {num} 个交互点");
						TolkHelper.Speak($"已触发 {num} 个交互点", interrupt: true);
						return;
					}
					Log.LogInfo((object)"精准版触发失败，回退到万能版");
				}
				else
				{
					Log.LogInfo((object)"没有找到 TriggerArea 实例，回退到万能版");
				}
			}
			else
			{
				Log.LogInfo((object)"TriggerArea 类型未找到，回退到万能版");
			}
			Log.LogInfo((object)"【探索场景尝试】万能版：快速点击屏幕多个位置");
			int systemMetrics = GetSystemMetrics(0);
			int systemMetrics2 = GetSystemMetrics(1);
			int x = systemMetrics / 2;
			int y = systemMetrics2 / 2;
			ClickAt(x, y);
			Thread.Sleep(100);
			ClickAt(systemMetrics / 4, systemMetrics2 / 4);
			Thread.Sleep(100);
			ClickAt(systemMetrics * 3 / 4, systemMetrics2 / 4);
			Thread.Sleep(100);
			ClickAt(systemMetrics / 4, systemMetrics2 * 3 / 4);
			Thread.Sleep(100);
			ClickAt(systemMetrics * 3 / 4, systemMetrics2 * 3 / 4);
			Thread.Sleep(100);
			for (int i = 0; i < 10; i++)
			{
				ClickAt(x, y);
				Thread.Sleep(50);
			}
			Log.LogInfo((object)"【一键过探索场景】万能尝试执行完毕");
			TolkHelper.Speak("探索场景尝试完成", interrupt: true);
		}
		catch (Exception ex2)
		{
			Log.LogError((object)("【一键过探索场景】异常: " + ex2.GetType().Name + " - " + ex2.Message));
			Log.LogError((object)("堆栈: " + ex2.StackTrace));
			TolkHelper.Speak("跳过探索场景时出错", interrupt: true);
		}
	}

	private static void TryAutoQTE()
	{
		Log.LogInfo((object)"【一键过 QTE】开始执行万能 QTE 破解...");
		TolkHelper.Speak("正在尝试自动过 QTE", interrupt: true);
		Log.LogInfo((object)"【QTE 尝试】方案一：快速连按空格");
		for (int i = 0; i < 20; i++)
		{
			keybd_event(32, 0, 0u, UIntPtr.Zero);
			Thread.Sleep(10);
			keybd_event(32, 0, 2u, UIntPtr.Zero);
			Thread.Sleep(10);
		}
		Log.LogInfo((object)"【QTE 尝试】方案二：快速连按回车");
		for (int j = 0; j < 10; j++)
		{
			keybd_event(13, 0, 0u, UIntPtr.Zero);
			Thread.Sleep(20);
			keybd_event(13, 0, 2u, UIntPtr.Zero);
			Thread.Sleep(20);
		}
		Log.LogInfo((object)"【QTE 尝试】方案三：模拟鼠标快速左右拖动");
		int num = GetSystemMetrics(0) / 2;
		int num2 = GetSystemMetrics(1) / 2;
		SetCursorPos(num - 100, num2);
		Thread.Sleep(10);
		mouse_event(2u, 0u, 0u, 0u, 0u);
		Thread.Sleep(10);
		for (int k = 0; k < 5; k++)
		{
			SetCursorPos(num + 100, num2);
			Thread.Sleep(20);
			SetCursorPos(num - 100, num2);
			Thread.Sleep(20);
		}
		mouse_event(4u, 0u, 0u, 0u, 0u);
		Log.LogInfo((object)"【QTE 尝试】方案四：模拟鼠标快速上下拖动");
		SetCursorPos(num, num2 - 100);
		Thread.Sleep(10);
		mouse_event(2u, 0u, 0u, 0u, 0u);
		Thread.Sleep(10);
		for (int l = 0; l < 5; l++)
		{
			SetCursorPos(num, num2 + 100);
			Thread.Sleep(20);
			SetCursorPos(num, num2 - 100);
			Thread.Sleep(20);
		}
		mouse_event(4u, 0u, 0u, 0u, 0u);
		Log.LogInfo((object)"【QTE 尝试】方案五：快速连点鼠标左键");
		for (int m = 0; m < 20; m++)
		{
			mouse_event(2u, 0u, 0u, 0u, 0u);
			Thread.Sleep(10);
			mouse_event(4u, 0u, 0u, 0u, 0u);
			Thread.Sleep(10);
		}
		Log.LogInfo((object)"【一键过 QTE】万能尝试执行完毕");
		TolkHelper.Speak("QTE 尝试完成", interrupt: true);
	}

	private static void ExploreCodeForQTE()
	{
		Log.LogInfo((object)"========================================");
		Log.LogInfo((object)"【代码探索】开始扫描游戏代码...");
		Log.LogInfo((object)"========================================");
		TolkHelper.Speak("正在扫描游戏代码，请稍候...", interrupt: true);
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Log.LogInfo((object)$"当前加载的程序集总数: {assemblies.Length}");
			List<Type> list = new List<Type>();
			int num = 0;
			int num2 = 0;
			Assembly[] array = assemblies;
			Assembly[] array2 = array;
			foreach (Assembly assembly in array2)
			{
				try
				{
					Type[] types = assembly.GetTypes();
					num += types.Length;
					Type[] array3 = types;
					Type[] array4 = array3;
					foreach (Type type in array4)
					{
						if (IsInterestingType(type))
						{
							list.Add(type);
						}
					}
				}
				catch (ReflectionTypeLoadException ex)
				{
					num2++;
					ManualLogSource log = Log;
					string name = assembly.GetName().Name;
					Type[] types2 = ex.Types;
					object arg = ((types2 != null) ? types2.Length : 0);
					Exception[] loaderExceptions = ex.LoaderExceptions;
					log.LogDebug((object)$"程序集 {name} 加载部分类型失败: {arg} 个成功, {((loaderExceptions != null) ? loaderExceptions.Length : 0)} 个失败");
					if (ex.Types == null)
					{
						continue;
					}
					Type[] types3 = ex.Types;
					Type[] array5 = types3;
					foreach (Type type2 in array5)
					{
						if (type2 != null && IsInterestingType(type2))
						{
							list.Add(type2);
						}
					}
				}
				catch (Exception ex2)
				{
					num2++;
					Log.LogDebug((object)("跳过程序集 " + assembly.GetName().Name + ": " + ex2.Message));
				}
			}
			Log.LogInfo((object)$"总类型数: {num}");
			Log.LogInfo((object)$"跳过的程序集数: {num2}");
			Log.LogInfo((object)$"找到的相关类型数: {list.Count}");
			Log.LogInfo((object)"");
			Log.LogInfo((object)"========== 找到的相关类型 ==========");
			Log.LogInfo((object)"");
			List<Type> list2 = new List<Type>();
			List<Type> list3 = new List<Type>();
			Type type3 = Type.GetType("UnityEngine.MonoBehaviour, UnityEngine");
			foreach (Type item in list)
			{
				if (type3 != null && type3.IsAssignableFrom(item))
				{
					list2.Add(item);
				}
				else
				{
					list3.Add(item);
				}
			}
			Log.LogInfo((object)$"【MonoBehaviour 类型（共 {list2.Count} 个）】");
			Log.LogInfo((object)"这些是挂在游戏对象上的脚本，最有可能包含 QTE 逻辑");
			Log.LogInfo((object)"");
			foreach (Type item2 in list2.OrderBy((Type t) => t.FullName))
			{
				OutputTypeDetails(item2);
			}
			Log.LogInfo((object)"");
			Log.LogInfo((object)$"【其他类型（共 {list3.Count} 个）】");
			Log.LogInfo((object)"");
			foreach (Type item3 in list3.OrderBy((Type t) => t.FullName))
			{
				OutputTypeDetails(item3);
			}
			Log.LogInfo((object)"");
			Log.LogInfo((object)"========================================");
			Log.LogInfo((object)"【代码探索】扫描完成！");
			Log.LogInfo((object)"========================================");
			TolkHelper.Speak($"代码扫描完成，找到 {list.Count} 个相关类型，其中 {list2.Count} 个是 MonoBehaviour", interrupt: true);
		}
		catch (Exception ex3)
		{
			Log.LogError((object)("代码探索异常: " + ex3.GetType().Name + " - " + ex3.Message));
			Log.LogError((object)("堆栈: " + ex3.StackTrace));
			TolkHelper.Speak("代码扫描出错", interrupt: true);
		}
	}

	private static bool IsInterestingType(Type type)
	{
		if (type == null)
		{
			return false;
		}
		string name = type.Name;
		string text = type.FullName ?? "";
		if (text.StartsWith("System.") || text.StartsWith("Microsoft.") || text.StartsWith("UnityEngine.") || text.StartsWith("UnityEditor.") || text.StartsWith("BepInEx.") || text.StartsWith("Harmony") || text.StartsWith("Mono.") || text.StartsWith("mscorlib") || text.StartsWith("TMPro.") || text == "TMPro.TMP_Text")
		{
			return false;
		}
		string[] cODE_EXPLORE_KEYWORDS = CODE_EXPLORE_KEYWORDS;
		string[] array = cODE_EXPLORE_KEYWORDS;
		foreach (string value in array)
		{
			if (name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static void OutputTypeDetails(Type type)
	{
		Log.LogInfo((object)("--- " + type.FullName + " ---"));
		if (type.BaseType != null)
		{
			Log.LogInfo((object)("  基类: " + type.BaseType.FullName));
		}
		Type[] interfaces = type.GetInterfaces();
		if (interfaces.Length != 0)
		{
			string text = string.Join(", ", interfaces.Select((Type i) => i.Name));
			Log.LogInfo((object)("  接口: " + text));
		}
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
		if (fields.Length != 0)
		{
			Log.LogInfo((object)$"  公共字段 ({fields.Length} 个):");
			FieldInfo[] array = fields;
			FieldInfo[] array2 = array;
			foreach (FieldInfo fieldInfo in array2)
			{
				string text2 = (fieldInfo.IsStatic ? " static" : "");
				Log.LogInfo((object)("    - " + fieldInfo.FieldType.Name + " " + fieldInfo.Name + text2));
			}
		}
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
		if (properties.Length != 0)
		{
			Log.LogInfo((object)$"  公共属性 ({properties.Length} 个):");
			PropertyInfo[] array3 = properties;
			PropertyInfo[] array4 = array3;
			foreach (PropertyInfo propertyInfo in array4)
			{
				string text3 = "";
				if (propertyInfo.CanRead && propertyInfo.CanWrite)
				{
					text3 = " get/set";
				}
				else if (propertyInfo.CanRead)
				{
					text3 = " get";
				}
				else if (propertyInfo.CanWrite)
				{
					text3 = " set";
				}
				Log.LogInfo((object)("    - " + propertyInfo.PropertyType.Name + " " + propertyInfo.Name + text3));
			}
		}
		MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
		if (methods.Length != 0)
		{
			Log.LogInfo((object)$"  公共方法 ({methods.Length} 个):");
			MethodInfo[] array5 = methods;
			MethodInfo[] array6 = array5;
			foreach (MethodInfo methodInfo in array6)
			{
				if (!methodInfo.IsSpecialName)
				{
					ParameterInfo[] parameters = methodInfo.GetParameters();
					string text4 = string.Join(", ", parameters.Select((ParameterInfo p) => p.ParameterType.Name + " " + p.Name));
					string text5 = (methodInfo.IsStatic ? " static" : "");
					Log.LogInfo((object)("    - " + methodInfo.ReturnType.Name + " " + methodInfo.Name + "(" + text4 + ")" + text5));
				}
			}
		}
		Log.LogInfo((object)"");
	}

	private void InstallKeyboardHook()
	{
		Log.LogInfo((object)"正在安装系统键盘钩子...");
		_keyboardProc = KeyboardHookCallback;
		IntPtr moduleHandle = GetModuleHandle(null);
		Log.LogInfo((object)$"模块句柄: {moduleHandle}");
		_hookId = SetWindowsHookEx(13, _keyboardProc, moduleHandle, 0u);
		if (_hookId != IntPtr.Zero)
		{
			Log.LogInfo((object)"系统键盘钩子安装成功！");
			return;
		}
		int lastWin32Error = Marshal.GetLastWin32Error();
		Log.LogError((object)$"系统键盘钩子安装失败，错误码: {lastWin32Error}");
	}

	private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
	{
		try
		{
			if (nCode >= 0 && (wParam == (IntPtr)256 || wParam == (IntPtr)260))
			{
				int num = Marshal.ReadInt32(lParam);
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogDebug((object)$"[键盘钩子] 按键: 0x{num:X2}");
				}
				if (!IsGameWindowActive())
				{
					ManualLogSource log2 = Log;
					if (log2 != null)
					{
						log2.LogDebug((object)"[键盘钩子] 游戏窗口不在前台，忽略按键");
					}
					return CallNextHookEx(_hookId, nCode, wParam, lParam);
				}
				_suppressCurrentKey = false;
				HandleKey(num);
				if (_suppressCurrentKey)
				{
					ManualLogSource log3 = Log;
					if (log3 != null)
					{
						log3.LogInfo((object)"[键盘钩子] 拦截按键，不传递给游戏");
					}
					return new IntPtr(1);
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogError((object)("键盘钩子回调异常: " + ex.GetType().Name + " - " + ex.Message));
			}
		}
		return CallNextHookEx(_hookId, nCode, wParam, lParam);
	}

	private static void HandleKey(int vkCode)
	{
		try
		{
			if (vkCode == 13 || vkCode == 37 || vkCode == 38 || vkCode == 39 || vkCode == 40 || IsDigitShortcut(vkCode))
			{
				LogInputState("Before key " + vkCode);
			}
			if (HandleDigitShortcut(vkCode))
			{
				LogInputState("After key " + vkCode);
				return;
			}
			switch (vkCode)
			{
			case 116:
			{
				ManualLogSource log14 = Log;
				if (log14 != null)
				{
					log14.LogInfo((object)"[快捷键] F5 按下 - 重复朗读");
				}
				if (!string.IsNullOrEmpty(_lastSpokenText))
				{
					TolkHelper.Speak(_lastSpokenText, interrupt: true);
				}
				else
				{
					TolkHelper.Speak("还没有朗读过文本", interrupt: true);
				}
				break;
			}
			case 117:
			{
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogInfo((object)"[快捷键] F6 按下 - 停止朗读");
				}
				TolkHelper.Stop();
				break;
			}
			case 121:
			{
				if (_inSettingsMode)
				{
					LogInputState("F10 ignored in settings");
					_suppressCurrentKey = true;
					break;
				}
				ManualLogSource log3 = Log;
				if (log3 != null)
				{
					log3.LogInfo((object)"[快捷键] F10 按下 - 一键过 QTE（精准版）");
				}
				SkipCurrentQTE();
				break;
			}
			case 122:
			{
				if (_inSettingsMode)
				{
					LogInputState("F11 ignored in settings");
					_suppressCurrentKey = true;
					break;
				}
				ManualLogSource log6 = Log;
				if (log6 != null)
				{
					log6.LogInfo((object)"[快捷键] F11 按下 - 切换自动过 QTE 模式");
				}
				ToggleAutoQTE();
				break;
			}
			case 115:
			{
				if (_inSettingsMode)
				{
					LogInputState("F4 ignored in settings");
					_suppressCurrentKey = true;
					break;
				}
				ManualLogSource log4 = Log;
				if (log4 != null)
				{
					log4.LogInfo((object)"[快捷键] F4 按下 - 一键过探索场景");
				}
				SkipExploreScene();
				break;
			}
			case 113:
			{
				ManualLogSource log10 = Log;
				if (log10 != null)
				{
					log10.LogInfo((object)"[快捷键] F2 按下 - 切换节点浏览模式");
				}
				if (_inNodeMode)
				{
					_inNodeMode = false;
					_storylineNodes = new OptionItem[0];
					_currentNodeIndex = 0;
					TolkHelper.Speak("退出节点浏览模式", interrupt: true);
				}
				else if (_currentUIState != UIState.Storyline)
				{
					LogInputState("F2 ignored outside storyline");
				}
				else
				{
					EnterNodeMode();
				}
				break;
			}
			case 114:
			{
				ManualLogSource log12 = Log;
				if (log12 != null)
				{
					log12.LogInfo((object)"[快捷键] F3 按下 - 跳转到当前节点");
				}
				if (_currentUIState == UIState.Storyline)
				{
					JumpToCurrentNode();
				}
				else
				{
					LogInputState("F3 ignored outside storyline");
				}
				break;
			}
			case 8:
			{
				ManualLogSource log8 = Log;
				if (log8 != null)
				{
					log8.LogInfo((object)"[快捷键] 退格键 按下 - 快退");
				}
				if (_currentUIState == UIState.Storyline || _inNodeMode)
				{
					BackToPreviousNode();
				}
				else
				{
					LogInputState("Backspace ignored outside storyline");
				}
				break;
			}
			case 27:
			{
				ManualLogSource log18 = Log;
				if (log18 != null)
				{
					log18.LogInfo((object)"[快捷键] ESC 按下");
				}
				if (_inSettingsMode && _settings.Length != 0 && ActivateReturnSetting())
				{
					_suppressCurrentKey = true;
				}
				else if (_inSettingsMode)
				{
					ForceExitSettingsScene();
					_suppressCurrentKey = true;
				}
				break;
			}
			case 32:
			{
				if (_currentUIState == UIState.QTE)
				{
					ManualLogSource log15 = Log;
					if (log15 != null)
					{
						log15.LogInfo((object)"[快捷键] 空格 按下");
					}
					ManualLogSource log16 = Log;
					if (log16 != null)
					{
						log16.LogInfo((object)"[QTE] 检测到空格，跳过当前 QTE");
					}
					SkipCurrentQTE();
					_suppressCurrentKey = true;
				}
				break;
			}
			case 68:
			{
				ManualLogSource log9 = Log;
				if (log9 != null)
				{
					log9.LogInfo((object)"[快捷键] D 键按下 - 切换字幕朗读开关");
				}
				ToggleSubtitleSpeak();
				break;
			}
			case 13:
			{
				ManualLogSource log5 = Log;
				if (log5 != null)
				{
					log5.LogInfo((object)"[快捷键] 回车 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					JumpToSelectedNode();
				}
				else if (_inSettingsMode && _settings.Length != 0)
				{
					ActivateCurrentSetting();
					_suppressCurrentKey = true;
				}
				else
				{
					HandleEnter();
				}
				break;
			}
			case 38:
			{
				ManualLogSource log13 = Log;
				if (log13 != null)
				{
					log13.LogInfo((object)"[快捷键] 上光标 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					_currentNodeIndex--;
					if (_currentNodeIndex < 0)
					{
						_currentNodeIndex = _storylineNodes.Length - 1;
					}
					SpeakCurrentNode();
				}
				else if (_inSettingsMode && _settings.Length != 0)
				{
					_currentSettingIndex--;
					if (_currentSettingIndex < 0)
					{
						_currentSettingIndex = _settings.Length - 1;
					}
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					_currentOptionIndex--;
					if (_currentOptionIndex < 0)
					{
						_currentOptionIndex = _options.Length - 1;
					}
					SpeakCurrentOption();
				}
				else
				{
					LogInputState("Up ignored");
				}
				break;
			}
			case 40:
			{
				ManualLogSource log7 = Log;
				if (log7 != null)
				{
					log7.LogInfo((object)"[快捷键] 下光标 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					_currentNodeIndex++;
					if (_currentNodeIndex >= _storylineNodes.Length)
					{
						_currentNodeIndex = 0;
					}
					SpeakCurrentNode();
				}
				else if (_inSettingsMode && _settings.Length != 0)
				{
					_currentSettingIndex++;
					if (_currentSettingIndex >= _settings.Length)
					{
						_currentSettingIndex = 0;
					}
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					_currentOptionIndex++;
					if (_currentOptionIndex >= _options.Length)
					{
						_currentOptionIndex = 0;
					}
					SpeakCurrentOption();
				}
				else
				{
					LogInputState("Down ignored");
				}
				break;
			}
			case 37:
			{
				ManualLogSource log11 = Log;
				if (log11 != null)
				{
					log11.LogInfo((object)"[快捷键] 左光标 按下");
				}
				if (_inSettingsMode && _settings.Length != 0)
				{
					SettingItem settingItem3 = _settings[_currentSettingIndex];
					AdjustSettingValue(settingItem3, -1);
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					_currentOptionIndex--;
					if (_currentOptionIndex < 0)
					{
						_currentOptionIndex = _options.Length - 1;
					}
					SpeakCurrentOption();
				}
				else
				{
					LogInputState("Left ignored");
				}
				break;
			}
			case 39:
			{
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogInfo((object)"[快捷键] 右光标 按下");
				}
				if (_inSettingsMode && _settings.Length != 0)
				{
					SettingItem settingItem = _settings[_currentSettingIndex];
					AdjustSettingValue(settingItem, 1);
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					_currentOptionIndex++;
					if (_currentOptionIndex >= _options.Length)
					{
						_currentOptionIndex = 0;
					}
					SpeakCurrentOption();
				}
				else
				{
					LogInputState("Right ignored");
				}
				break;
			}
			}
			if (vkCode == 13 || vkCode == 37 || vkCode == 38 || vkCode == 39 || vkCode == 40 || IsDigitShortcut(vkCode))
			{
				LogInputState("After key " + vkCode);
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log17 = Log;
			if (log17 != null)
			{
				log17.LogError((object)("处理按键异常: " + ex.GetType().Name + " - " + ex.Message));
			}
		}
	}

	private static void LogInputState(string context)
	{
		ManualLogSource log = Log;
		if (log == null)
		{
			return;
		}
		string text = (_options == null) ? "null" : _options.Length.ToString();
		string text2 = (_settings == null) ? "null" : _settings.Length.ToString();
		string text3 = (_storylineNodes == null) ? "null" : _storylineNodes.Length.ToString();
		log.LogInfo((object)$"[诊断状态] {context}; UI={_currentUIState}; inOptions={_inOptionsMode}; options={text}; optionIndex={_currentOptionIndex}; inSettings={_inSettingsMode}; settings={text2}; settingIndex={_currentSettingIndex}; inNode={_inNodeMode}; nodes={text3}; nodeIndex={_currentNodeIndex}; signature={_lastDetectedSignature}");
	}

	private static bool IsDigitShortcut(int vkCode)
	{
		return (vkCode >= 49 && vkCode <= 57) || (vkCode >= 97 && vkCode <= 105);
	}

	private static int DigitShortcutIndex(int vkCode)
	{
		if (vkCode >= 49 && vkCode <= 57)
		{
			return vkCode - 49;
		}
		if (vkCode >= 97 && vkCode <= 105)
		{
			return vkCode - 97;
		}
		return -1;
	}

	private static bool HandleDigitShortcut(int vkCode)
	{
		int num = DigitShortcutIndex(vkCode);
		if (num < 0)
		{
			return false;
		}
		if (_inNodeMode && _storylineNodes != null && num < _storylineNodes.Length)
		{
			_currentNodeIndex = num;
			JumpToSelectedNode();
			_suppressCurrentKey = true;
			return true;
		}
		if (_inSettingsMode && _settings != null && num < _settings.Length)
		{
			_currentSettingIndex = num;
			PlayGameSound("Highlight");
			SpeakCurrentSetting();
			_suppressCurrentKey = true;
			return true;
		}
		if (_inOptionsMode && _options != null && num < _options.Length)
		{
			_currentOptionIndex = num;
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)$"[快捷键] 数字 {num + 1} 选择选项");
			}
			HandleEnter();
			_suppressCurrentKey = true;
			return true;
		}
		if (_inOptionsMode || _inSettingsMode || _inNodeMode)
		{
			_suppressCurrentKey = true;
			TolkHelper.Speak($"没有第 {num + 1} 项", interrupt: true);
			return true;
		}
		return false;
	}

	private static void HandleEnter()
	{
		if (_inOptionsMode && _options.Length != 0)
		{
			OptionItem optionItem = _options[_currentOptionIndex];
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)$"点击选项 {_currentOptionIndex + 1}: {optionItem.Text}");
			}
			bool flag = !string.IsNullOrEmpty(optionItem.Text) && (optionItem.Text.Contains("返回") || optionItem.Text.Contains("关闭") || optionItem.Text.Equals("Back", StringComparison.OrdinalIgnoreCase));
			PlayGameSound(flag ? "Back" : "Click");
			TolkHelper.Speak("点击 " + optionItem.Text, interrupt: true);
			if (optionItem.Index >= 0 && optionItem.ClickableComponent != null && _gameControllerType != null && optionItem.ClickableComponent.GetType() == _gameControllerType)
			{
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogInfo((object)"通过GameController.OnOptionButtonClick点击选项");
				}
				try
				{
					MethodInfo method = _gameControllerType.GetMethod("OnOptionButtonClick", BindingFlags.Instance | BindingFlags.NonPublic);
					if (method != null)
					{
						method.Invoke(optionItem.ClickableComponent, new object[1] { optionItem.Index });
						ManualLogSource log3 = Log;
						if (log3 != null)
						{
							log3.LogInfo((object)"OnOptionButtonClick调用成功");
						}
						return;
					}
				}
				catch (Exception ex)
				{
					ManualLogSource log4 = Log;
					if (log4 != null)
					{
						log4.LogWarning((object)("OnOptionButtonClick调用失败: " + ex.Message));
					}
				}
			}
			if (TryActivateKnownMainMenuOption(optionItem))
			{
				return;
			}
			if (optionItem.ClickableComponent != null)
			{
				ManualLogSource log5 = Log;
				if (log5 != null)
				{
					log5.LogInfo((object)"尝试直接调用可点击组件的点击事件");
				}
				if (ClickComponent(optionItem.ClickableComponent))
				{
					ManualLogSource log6 = Log;
					if (log6 != null)
					{
						log6.LogInfo((object)"组件点击成功");
					}
					return;
				}
				ManualLogSource log7 = Log;
				if (log7 != null)
				{
					log7.LogWarning((object)"组件点击失败，回退到模拟鼠标点击");
				}
			}
			if (optionItem.HasScreenPosition)
			{
				ClickAt((int)optionItem.ScreenX, (int)optionItem.ScreenY);
			}
			else
			{
				ClickScreenCenter();
			}
		}
		else
		{
			ManualLogSource log8 = Log;
			if (log8 != null)
			{
				log8.LogInfo((object)"点击屏幕中心");
			}
			PlayGameSound("Click");
			TolkHelper.Speak("点击", interrupt: true);
			ClickScreenCenter();
		}
	}

	private static bool TryActivateKnownMainMenuOption(OptionItem optionItem)
	{
		string text = optionItem?.Text;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		try
		{
			if (text.Contains("点击开始"))
			{
				return false;
			}
			if (text.Contains("开始游戏") || text.Contains("新游戏") || text.Contains("继续游戏"))
			{
				if (TryStartGameFromMenu(text))
				{
					return true;
				}
				Log.LogWarning((object)("[主菜单] 直接启动失败，回退到按钮点击: " + text));
				return false;
			}
			if (text.Contains("故事线"))
			{
				bool flag = false;
				if (optionItem != null && optionItem.ClickableComponent != null)
				{
					Log.LogInfo((object)"[主菜单] 先触发故事线按钮自身事件");
					flag = ClickComponent(optionItem.ClickableComponent);
				}
				if (TryOpenStorylineFromMenu())
				{
					return true;
				}
				if (flag)
				{
					MarkNeedDetect();
					return true;
				}
				Log.LogWarning((object)"[主菜单] 故事线直接入口失败，回退到坐标点击");
				return false;
			}
			Type type = Type.GetType("MainMenuManager, Assembly-CSharp");
			object activeObject = GetActiveObject(type);
			if (activeObject == null)
			{
				return false;
			}
			string methodName = null;
			if (text.Contains("系统设置") || text.Contains("设置"))
			{
				methodName = "LoadSettings";
			}
			else if (text.Contains("档案"))
			{
				methodName = "OpenArchives";
			}
			else if (text.Contains("排行榜") || text.Contains("投票"))
			{
				methodName = "OpenVotePage";
			}
			else if (text.Contains("退出"))
			{
				methodName = "ExitGame";
			}
			if (methodName == null)
			{
				return false;
			}
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			Log.LogInfo((object)("[主菜单] 直接调用 MainMenuManager." + methodName + " 处理: " + text));
			method.Invoke(activeObject, null);
			MarkNeedDetect();
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[主菜单] 直接调用入口失败: " + ex.Message));
			return false;
		}
	}

	private static bool TryStartGameFromMenu(string text)
	{
		bool flag = false;
		try
		{
			Type type = Type.GetType("IntroAnimationController, Assembly-CSharp");
			object activeObject = GetActiveObject(type);
			if (activeObject != null)
			{
				bool flag2 = GetBoolProperty(activeObject, "IsPlaying");
				bool flag3 = GetBoolProperty(activeObject, "HasPlayed");
				Log.LogInfo((object)$"[主菜单] Intro 状态 IsPlaying={flag2}, HasPlayed={flag3}");
				if (flag2)
				{
					MethodInfo method = activeObject.GetType().GetMethod("EndIntro", BindingFlags.Instance | BindingFlags.NonPublic);
					if (method != null)
					{
						method.Invoke(activeObject, null);
						Log.LogInfo((object)"[主菜单] 已结束开场动画阻塞");
						flag = true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[主菜单] 处理开场动画失败: " + ex.Message));
		}
		try
		{
			object activeObject2 = GetActiveObject(_gameControllerType);
			if (activeObject2 == null)
			{
				Log.LogWarning((object)"[主菜单] 未找到 GameController，不能直接启动");
				return flag;
			}
			string text2 = text.Contains("继续") ? "ContinuePlay" : "StartPlay";
			MethodInfo method2 = activeObject2.GetType().GetMethod(text2, BindingFlags.Instance | BindingFlags.Public);
			if (method2 == null)
			{
				Log.LogWarning((object)("[主菜单] GameController 未找到方法: " + text2));
				return flag;
			}
			Log.LogInfo((object)("[主菜单] 直接调用 GameController." + text2 + " 处理: " + text));
			method2.Invoke(activeObject2, null);
			MarkNeedDetect();
			return true;
		}
		catch (Exception ex2)
		{
			Log.LogWarning((object)("[主菜单] 直接启动游戏失败: " + ex2.Message));
			return flag;
		}
	}

	private static bool TryOpenStorylineFromMenu()
	{
		bool flag = false;
		try
		{
			Type type = Type.GetType("MainMenuManager, Assembly-CSharp");
			object activeObject = GetActiveObject(type);
			if (activeObject != null)
			{
				MethodInfo method = type.GetMethod("OpenStoryLine", BindingFlags.Instance | BindingFlags.Public);
				if (method != null)
				{
					Log.LogInfo((object)"[主菜单] 调用 MainMenuManager.OpenStoryLine");
					method.Invoke(activeObject, null);
					flag = true;
				}
			}
			object activeObject2 = GetActiveObject(_gameControllerType);
			object obj = GetFieldValue(activeObject, "storyLinePageToggle") ?? GetFieldValue(activeObject2, "storyLinePageToggle");
			if (InvokeNoArg(obj, "PerformShow"))
			{
				Log.LogInfo((object)"[主菜单] storyLinePageToggle.PerformShow 成功");
				flag = true;
			}
			object obj2 = GetFieldValue(activeObject, "chapterStorylineController") ?? GetFieldValue(activeObject2, "chapterStorylineController") ?? GetActiveObject(_chapterStorylineControllerType);
			if (InvokeNoArg(obj2, "ShowChapterSelection"))
			{
				Log.LogInfo((object)"[主菜单] ChapterStorylineController.ShowChapterSelection 成功");
				flag = true;
			}
			object obj3 = GetFieldValue(activeObject2, "storylineUIManager") ?? GetActiveObject(_storylineUIManagerType);
			if (InvokeNoArg(obj3, "ShowChapterSelection"))
			{
				Log.LogInfo((object)"[主菜单] StorylineUIManager.ShowChapterSelection 成功");
				flag = true;
			}
			if (flag)
			{
				_currentUIState = UIState.Storyline;
				_lastDetectedSignature = GetStorylineSignature();
				EnterStorylineMode();
				MarkNeedDetect();
			}
			return flag;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[主菜单] 直接打开故事线失败: " + ex.Message));
			return flag;
		}
	}

	private static bool GetBoolProperty(object obj, string propertyName)
	{
		if (obj == null)
		{
			return false;
		}
		try
		{
			PropertyInfo property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return property != null && (bool)property.GetValue(obj);
		}
		catch
		{
			return false;
		}
	}

	private static bool InvokeNoArg(object obj, string methodName)
	{
		if (obj == null || string.IsNullOrEmpty(methodName))
		{
			return false;
		}
		try
		{
			MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				return false;
			}
			method.Invoke(obj, null);
			return true;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("调用 " + methodName + " 失败: " + ex.Message));
			return false;
		}
	}

	private static bool ClickComponent(object component)
	{
		try
		{
			if (component == null)
			{
				return false;
			}
			Type type = component.GetType();
			PropertyInfo property = type.GetProperty("onClick", BindingFlags.Instance | BindingFlags.Public);
			if (property != null)
			{
				object value = property.GetValue(component);
				if (value != null)
				{
					MethodInfo method = value.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
					if (method != null)
					{
						method.Invoke(value, null);
						Log.LogInfo((object)(type.Name + ".onClick.Invoke() 调用成功"));
						return true;
					}
				}
			}
			if (type.GetProperty("onValueChanged", BindingFlags.Instance | BindingFlags.Public) != null)
			{
				Log.LogDebug((object)("组件有 onValueChanged 事件: " + type.Name));
			}
			if (type.GetMethod("Select", BindingFlags.Instance | BindingFlags.Public) != null)
			{
				Log.LogDebug((object)("可以调用 Select 方法: " + type.Name));
			}
			Log.LogWarning((object)("无法直接触发 " + type.Name + " 的点击事件"));
			return false;
		}
		catch (Exception ex)
		{
			Log.LogError((object)("调用组件点击事件失败: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
			return false;
		}
	}

	private static void ClickScreenCenter()
	{
		int systemMetrics = GetSystemMetrics(0);
		int y = GetSystemMetrics(1) / 2;
		ClickAt(systemMetrics / 2, y);
	}

	private static void ClickAt(int x, int y)
	{
		try
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)$"移动鼠标到 ({x}, {y}) 并点击");
			}
			SetCursorPos(x, y);
			Thread.Sleep(20);
			mouse_event(2u, 0u, 0u, 0u, 0u);
			Thread.Sleep(20);
			mouse_event(4u, 0u, 0u, 0u, 0u);
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogError((object)("模拟鼠标点击失败: " + ex.Message));
			}
		}
	}

	private static void SpeakCurrentOption()
	{
		if (_options.Length != 0)
		{
			OptionItem optionItem = _options[_currentOptionIndex];
			string text = "";
			if (optionItem.ClickableComponent != null)
			{
				text = "（可点击）";
			}
			string text2 = (string.IsNullOrEmpty(optionItem.Text) ? "（无文字）" : optionItem.Text);
			PlayGameSound("Highlight");
			TolkHelper.Speak($"{_currentOptionIndex + 1} / {_options.Length}: {text2}{text}", interrupt: true);
		}
	}

	public static void SetOptions(OptionItem[] options)
	{
		if (options == null || options.Length == 0)
		{
			LogInputState("SetOptions ignored empty");
			return;
		}
		_options = options;
		_currentOptionIndex = 0;
		_inOptionsMode = true;
		_inSettingsMode = false;
		_settings = new SettingItem[0];
		_currentSettingIndex = 0;
		_isHorizontalLayout = DetectLayout(options);
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)string.Format("进入选项模式，共 {0} 个选项，排列方式: {1}", options.Length, _isHorizontalLayout ? "横向" : "纵向"));
		}
		int num = 0;
		for (int i = 0; i < options.Length; i++)
		{
			if (options[i].ClickableComponent != null)
			{
				num++;
			}
		}
		ManualLogSource log2 = Log;
		if (log2 != null)
		{
			log2.LogInfo((object)$"其中 {num} 个是可点击组件");
		}
		SpeakCurrentOption();
		LogInputState("SetOptions done");
	}

	private static bool DetectLayout(OptionItem[] options)
	{
		if (options == null || options.Length < 2)
		{
			return false;
		}
		float num = float.MaxValue;
		float num2 = float.MinValue;
		float num3 = float.MaxValue;
		float num4 = float.MinValue;
		int num5 = 0;
		foreach (OptionItem optionItem in options)
		{
			if (optionItem.HasScreenPosition)
			{
				num5++;
				if (optionItem.ScreenX < num)
				{
					num = optionItem.ScreenX;
				}
				if (optionItem.ScreenX > num2)
				{
					num2 = optionItem.ScreenX;
				}
				if (optionItem.ScreenY < num3)
				{
					num3 = optionItem.ScreenY;
				}
				if (optionItem.ScreenY > num4)
				{
					num4 = optionItem.ScreenY;
				}
			}
		}
		if (num5 < 2)
		{
			return false;
		}
		float num6 = num2 - num;
		float num7 = num4 - num3;
		return num6 > num7;
	}

	public static void LeaveOptions()
	{
		if (true)
		{
			_inOptionsMode = false;
			_options = new OptionItem[0];
			_currentOptionIndex = 0;
			_inSettingsMode = false;
			_settings = new SettingItem[0];
			_currentSettingIndex = 0;
			_inStorylineMode = false;
			_inNodeMode = false;
			_storylineNodes = new OptionItem[0];
			_currentNodeIndex = 0;
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)"离开选项/设置/节点模式");
			}
			LogInputState("LeaveOptions done");
		}
	}

	public static void RequestSpeak(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		text = text.Trim();
		if (string.IsNullOrEmpty(text) || text == _lastSpokenText)
		{
			return;
		}
		if (!_subtitleSpeakEnabled)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("字幕朗读已关闭，跳过自动朗读: " + text));
			}
			_lastSpokenText = text;
			return;
		}
		_lastSpokenText = text;
		TolkHelper.Speak(text);
		ManualLogSource log2 = Log;
		if (log2 != null)
		{
			log2.LogDebug((object)("朗读: " + text));
		}
		MarkNeedDetect();
	}

	private static void DetectOptions()
	{
		try
		{
			Log.LogInfo((object)"DetectOptions 被调用");
			if (IsInStorylinePage())
			{
				Log.LogInfo((object)"检测到故事线页面，使用精准章节列表");
				EnterStorylineMode();
				return;
			}
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			if (allVisibleTextsWithPosition == null || allVisibleTextsWithPosition.Length == 0)
			{
				TolkHelper.Speak("屏幕上没有找到文字", interrupt: true);
				return;
			}
			Log.LogInfo((object)$"共找到 {allVisibleTextsWithPosition.Length} 段文字");
			List<OptionItem> list = new List<OptionItem>();
			OptionItem[] array = allVisibleTextsWithPosition;
			OptionItem[] array2 = array;
			foreach (OptionItem optionItem in array2)
			{
				if (optionItem.ClickableComponent != null)
				{
					list.Add(optionItem);
				}
			}
			if (list.Count >= 1)
			{
				Log.LogInfo((object)$"找到 {list.Count} 个可点击组件");
				SetOptions(SortOptions(list.ToArray()));
				string arg = (_isHorizontalLayout ? "横向" : "纵向");
				TolkHelper.Speak($"检测到 {list.Count} 个可点击元素，{arg}排列，已进入选项模式。按上下左右光标切换，按回车点击", interrupt: true);
				return;
			}
			Log.LogInfo((object)"没有找到可点击组件，尝试用短文本猜测");
			array = allVisibleTextsWithPosition;
			OptionItem[] array3 = array;
			foreach (OptionItem optionItem2 in array3)
			{
				string text = optionItem2.Text.Trim();
				if (!string.IsNullOrEmpty(text) && text.Length < 20 && text.Length > 0)
				{
					list.Add(optionItem2);
				}
			}
			if (list.Count >= 2 && list.Count <= 12)
			{
				Log.LogInfo((object)$"检测到可能的选项 {list.Count} 个");
				SetOptions(SortOptions(list.ToArray()));
				string arg2 = (_isHorizontalLayout ? "横向" : "纵向");
				TolkHelper.Speak($"检测到 {list.Count} 个可能的选项，{arg2}排列，已进入选项模式。按上下左右光标切换，按回车点击", interrupt: true);
			}
			else if (list.Count > 12)
			{
				Log.LogInfo((object)$"候选选项太多（{list.Count}个）");
				TolkHelper.Speak($"找到 {list.Count} 段短文本，无法确定是否为选项。按 F8 朗读全部，按回车点击屏幕中间", interrupt: true);
			}
			else
			{
				Log.LogInfo((object)$"候选选项太少（{list.Count}个）");
				TolkHelper.Speak("没有检测到明显的选项，按回车点击屏幕中间", interrupt: true);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("DetectOptions 异常: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
			TolkHelper.Speak("探测选项时出错", interrupt: true);
		}
	}

	private static OptionItem[] SortOptions(OptionItem[] options)
	{
		if (options == null || options.Length <= 1)
		{
			return options;
		}
		if (DetectLayout(options))
		{
			Log.LogInfo((object)"横向排列，按 X 坐标从左到右排序");
			return options.OrderBy((OptionItem o) => o.ScreenX).ToArray();
		}
		Log.LogInfo((object)"纵向排列，按 Y 坐标从上到下排序");
		return options.OrderBy((OptionItem o) => o.ScreenY).ToArray();
	}

	private static object FindClickableComponent(object gameObject)
	{
		if (gameObject == null)
		{
			return null;
		}
		try
		{
			Type type = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI");
			if (type == null)
			{
				Log.LogDebug((object)"未找到 UnityEngine.UI.Selectable 类型");
				return null;
			}
			MethodInfo method = gameObject.GetType().GetMethod("GetComponent", new Type[1] { typeof(Type) });
			if (method == null)
			{
				Log.LogDebug((object)"未找到 GetComponent 方法");
				return null;
			}
			object obj = method.Invoke(gameObject, new object[1] { type });
			if (obj != null)
			{
				Log.LogDebug((object)("在当前对象上找到可点击组件: " + obj.GetType().Name));
				return obj;
			}
			object obj2 = gameObject;
			for (int i = 0; i < 10; i++)
			{
				PropertyInfo property = obj2.GetType().GetProperty("transform");
				if (property == null)
				{
					break;
				}
				object value = property.GetValue(obj2);
				if (value == null)
				{
					break;
				}
				PropertyInfo property2 = value.GetType().GetProperty("parent");
				if (property2 == null)
				{
					break;
				}
				object value2 = property2.GetValue(value);
				if (value2 == null)
				{
					break;
				}
				PropertyInfo property3 = value2.GetType().GetProperty("gameObject");
				if (property3 == null)
				{
					break;
				}
				object value3 = property3.GetValue(value2);
				if (value3 == null)
				{
					break;
				}
				obj = method.Invoke(value3, new object[1] { type });
				if (obj != null)
				{
					Log.LogDebug((object)$"在第 {i + 1} 级父对象上找到可点击组件: {obj.GetType().Name}");
					return obj;
				}
				obj2 = value3;
			}
			try
			{
				MethodInfo method2 = gameObject.GetType().GetMethod("GetComponentInChildren", new Type[1] { typeof(Type) });
				if (method2 != null)
				{
					obj = method2.Invoke(gameObject, new object[1] { type });
					if (obj != null)
					{
						Log.LogDebug((object)("在子对象中找到可点击组件: " + obj.GetType().Name));
						return obj;
					}
				}
			}
			catch (Exception ex)
			{
				Log.LogDebug((object)("在子对象中查找可点击组件失败: " + ex.Message));
			}
			return null;
		}
		catch (Exception ex2)
		{
			Log.LogDebug((object)("查找可点击组件时出错: " + ex2.Message));
			return null;
		}
	}

	private static OptionItem[] GetAllVisibleTextsWithPosition()
	{
		try
		{
			ResolveSubtitleTypes();
			Type type = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
			if (type == null)
			{
				Log.LogWarning((object)"未找到 TMP_Text 类型");
				return new OptionItem[0];
			}
			Array array = FindObjectsOfType(type);
			if (array == null || array.Length == 0)
			{
				Log.LogInfo((object)"没有找到任何 TMP_Text 组件");
				return new OptionItem[0];
			}
			Log.LogInfo((object)$"找到 {array.Length} 个 TMP_Text 组件");
			List<OptionItem> list = new List<OptionItem>();
			Type type2 = Type.GetType("UnityEngine.Camera, UnityEngine");
			object obj = null;
			if (type2 != null)
			{
				try
				{
					PropertyInfo property = type2.GetProperty("main", BindingFlags.Static | BindingFlags.Public);
					if (property != null)
					{
						obj = property.GetValue(null);
						Log.LogDebug((object)$"主相机: {obj}");
					}
				}
				catch (Exception ex)
				{
					Log.LogDebug((object)("获取主相机失败: " + ex.Message));
				}
			}
			foreach (object item in array)
			{
				try
				{
					PropertyInfo property2 = type.GetProperty("enabled");
					if (property2 != null && !(bool)property2.GetValue(item))
					{
						continue;
					}
					PropertyInfo property3 = type.GetProperty("isActiveAndEnabled");
					if (property3 != null && !(bool)property3.GetValue(item))
					{
						continue;
					}
					PropertyInfo property4 = type.GetProperty("text");
					if (property4 == null)
					{
						continue;
					}
					string text = (string)property4.GetValue(item);
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					text = text.Trim();
					if (string.IsNullOrEmpty(text))
					{
						continue;
					}
					if (_subtitleTextComponent != null && item == _subtitleTextComponent)
					{
						Log.LogDebug((object)("跳过字幕文本: " + text));
						continue;
					}
					object obj2 = null;
					try
					{
						PropertyInfo property5 = type.GetProperty("gameObject");
						if (property5 != null)
						{
							obj2 = property5.GetValue(item);
						}
					}
					catch (Exception ex2)
					{
						Log.LogDebug((object)("获取 gameObject 失败: " + ex2.Message));
					}
					object clickableComponent = null;
					if (obj2 != null)
					{
						clickableComponent = FindClickableComponent(obj2);
					}
					float num = 0f;
					float num2 = 0f;
					bool hasScreenPosition = false;
					try
					{
						if (obj2 != null)
						{
							PropertyInfo property6 = obj2.GetType().GetProperty("transform");
							if (property6 != null)
							{
								object value = property6.GetValue(obj2);
								if (value != null)
								{
									PropertyInfo property7 = value.GetType().GetProperty("position");
									if (property7 != null)
									{
										object value2 = property7.GetValue(value);
										if (value2 != null && obj != null)
										{
											MethodInfo method = type2.GetMethod("WorldToScreenPoint", new Type[1] { value2.GetType() });
											if (method != null)
											{
												object obj3 = method.Invoke(obj, new object[1] { value2 });
												if (obj3 != null)
												{
													PropertyInfo property8 = obj3.GetType().GetProperty("x");
													PropertyInfo property9 = obj3.GetType().GetProperty("y");
													if (property8 != null && property9 != null)
													{
														num = (float)property8.GetValue(obj3);
														num2 = (float)GetSystemMetrics(1) - (float)property9.GetValue(obj3);
														hasScreenPosition = true;
														Log.LogDebug((object)$"文字 '{text}' 的屏幕位置: ({num}, {num2})");
													}
												}
											}
										}
									}
								}
							}
						}
					}
					catch (Exception ex3)
					{
						Log.LogDebug((object)("获取位置失败: " + ex3.Message));
					}
					OptionItem optionItem = new OptionItem();
					optionItem.Text = text;
					optionItem.ScreenX = num;
					optionItem.ScreenY = num2;
					optionItem.HasScreenPosition = hasScreenPosition;
					optionItem.ClickableComponent = clickableComponent;
					list.Add(optionItem);
				}
				catch (Exception ex4)
				{
					Log.LogDebug((object)("处理单个 TMP_Text 组件时出错: " + ex4.Message));
				}
			}
			Log.LogInfo((object)$"处理后剩余 {list.Count} 段有效文字");
			return list.ToArray();
		}
		catch (Exception ex5)
		{
			Log.LogError((object)("GetAllVisibleTextsWithPosition 异常: " + ex5.GetType().Name + " - " + ex5.Message));
			Log.LogError((object)("堆栈: " + ex5.StackTrace));
			return new OptionItem[0];
		}
	}

	private static Array FindObjectsOfType(Type type)
	{
		Array array = null;
		try
		{
			Type type2 = Type.GetType("UnityEngine.Object, UnityEngine");
			if (type2 != null)
			{
				MethodInfo[] methods = type2.GetMethods(BindingFlags.Static | BindingFlags.Public);
				MethodInfo[] array2 = methods;
				foreach (MethodInfo methodInfo in array2)
				{
					if (methodInfo.Name == "FindObjectsOfType" && methodInfo.IsGenericMethodDefinition)
					{
						ParameterInfo[] parameters = methodInfo.GetParameters();
						if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
						{
							array = (Array)methodInfo.MakeGenericMethod(type).Invoke(null, new object[1] { true });
							Log.LogInfo((object)"方法1成功: FindObjectsOfType<T>(bool)");
							return array;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("方法1失败: " + ex.Message));
		}
		if (array == null)
		{
			try
			{
				Type type3 = Type.GetType("UnityEngine.Resources, UnityEngine");
				if (type3 != null)
				{
					MethodInfo method = type3.GetMethod("FindObjectsOfTypeAll", BindingFlags.Static | BindingFlags.Public);
					if (method != null && method.IsGenericMethodDefinition)
					{
						array = (Array)method.MakeGenericMethod(type).Invoke(null, null);
						Log.LogInfo((object)"方法2成功: Resources.FindObjectsOfTypeAll<T>");
						return array;
					}
				}
			}
			catch (Exception ex2)
			{
				Log.LogWarning((object)("方法2失败: " + ex2.Message));
			}
		}
		if (array == null)
		{
			try
			{
				Type type4 = Type.GetType("UnityEngine.Object, UnityEngine");
				if (type4 != null)
				{
					MethodInfo method2 = type4.GetMethod("FindObjectsOfType", new Type[2]
					{
						typeof(Type),
						typeof(bool)
					});
					if (method2 != null)
					{
						array = (Array)method2.Invoke(null, new object[2] { type, true });
						Log.LogInfo((object)"方法3成功: FindObjectsOfType(Type, bool)");
						return array;
					}
				}
			}
			catch (Exception ex3)
			{
				Log.LogWarning((object)("方法3失败: " + ex3.Message));
			}
		}
		if (array == null)
		{
			try
			{
				Type type5 = Type.GetType("UnityEngine.Object, UnityEngine");
				if (type5 != null)
				{
					MethodInfo[] methods2 = type5.GetMethods(BindingFlags.Static | BindingFlags.Public);
					MethodInfo[] array3 = methods2;
					foreach (MethodInfo methodInfo2 in array3)
					{
						if (methodInfo2.Name == "FindObjectsByType" && methodInfo2.IsGenericMethodDefinition && methodInfo2.GetParameters().Length == 2)
						{
							MethodInfo methodInfo3 = methodInfo2.MakeGenericMethod(type);
							Type type6 = Type.GetType("UnityEngine.FindObjectsInactive, UnityEngine");
							Type type7 = Type.GetType("UnityEngine.FindObjectsSortMode, UnityEngine");
							if (type6 != null && type7 != null)
							{
								array = (Array)methodInfo3.Invoke(null, new object[2] { 1, 0 });
								Log.LogInfo((object)"方法4成功: FindObjectsByType<T>(FindObjectsInactive, FindObjectsSortMode)");
								return array;
							}
						}
					}
				}
			}
			catch (Exception ex4)
			{
				Log.LogWarning((object)("方法4失败: " + ex4.Message));
			}
		}
		Log.LogError((object)"所有查找对象的方法都失败了！");
		return null;
	}

	private static void SpeakAllVisibleText()
	{
		try
		{
			Log.LogInfo((object)"SpeakAllVisibleText 被调用");
			OptionItem[] allVisibleTextsWithPosition = GetAllVisibleTextsWithPosition();
			if (allVisibleTextsWithPosition == null || allVisibleTextsWithPosition.Length == 0)
			{
				TolkHelper.Speak("屏幕上没有找到文字", interrupt: true);
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			OptionItem[] array = allVisibleTextsWithPosition;
			OptionItem[] array2 = array;
			foreach (OptionItem optionItem in array2)
			{
				if (optionItem.ClickableComponent != null)
				{
					stringBuilder.AppendLine("[可点击] " + optionItem.Text);
					num++;
				}
				else
				{
					stringBuilder.AppendLine(optionItem.Text);
				}
			}
			TolkHelper.Speak($"屏幕上共有 {allVisibleTextsWithPosition.Length} 段文字，其中 {num} 个是可点击元素：" + stringBuilder.ToString(), interrupt: true);
		}
		catch (Exception ex)
		{
			Log.LogError((object)("SpeakAllVisibleText 异常: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
			TolkHelper.Speak("读取所有文字时出错", interrupt: true);
		}
	}
}

