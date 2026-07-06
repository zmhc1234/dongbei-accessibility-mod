using System;
using System.Collections;
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
		QTE,
		Archive
	}

	private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

	private delegate void TimerProc(IntPtr hWnd, uint uMsg, IntPtr nIDEvent, uint dwTime);

	private sealed class RevisitableNodeLink
	{
		public string ParentNodeId;

		public object ParentNode;

		public object ContinueNode;

		public int OptionIndex;
	}

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

	private static int _lastStorylineChapterNumber;

	private static bool _restoreStorylineNodeModeOnOpen;

	private static int _storylineMissCount = 0;

	private const int STORYLINE_MISS_THRESHOLD = 3;

	private static int _optionsMissCount = 0;

	private const int OPTIONS_MISS_THRESHOLD = 3;

	private static bool _autoQTEEnabled = false;

	private static bool _suppressCurrentKey = false;

	private static Type _qteControllerType;

	private static bool _qteTypesResolved = false;

	private static DateTime _lastQTESpeakUtc = DateTime.MinValue;

	private static DateTime _lastQTEStartedUtc = DateTime.MinValue;

	private static object _lastQTEController;

	private static DateTime _suppressSpaceUntilUtc = DateTime.MinValue;

	private static DateTime _lastQTESkipAttemptUtc = DateTime.MinValue;

	private static Type _triggerAreaType;

	private static bool _triggerAreaTypesResolved = false;

	private static Type _subtitleManagerType;

	private static object _subtitleTextComponent;

	private static bool _subtitleTypesResolved = false;

	private static Type _narrationManagerType;

	private static bool _narrationTypesResolved = false;

	private static string _lastNarrationSpeakText = "";

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

	private static Type _endingPageControllerType;

	private static bool _endingTypesResolved = false;

	private const int ENDING_ACTION_RETURN_STORYLINE = -9001;

	private const int ENDING_ACTION_GOTO_STORYLINE = -9002;

	private static bool _inSettingsMode;

	private static SettingItem[] _settings = new SettingItem[0];

	private static int _currentSettingIndex;

	private static DateTime _ignoreSettingsUntilUtc = DateTime.MinValue;

	private static DateTime _ignoreOptionsUntilUtc = DateTime.MinValue;

	private static Type _archiveHomePageControllerType;

	private static Type _characterDetailPageControllerType;

	private static Type _archiveContentPageControllerType;

	private static Type _characterCardType;

	private static Type _archiveListItemType;

	private static bool _archiveTypesResolved = false;

	private static bool _inArchiveMode;

	private static OptionItem[] _archiveItems = new OptionItem[0];

	private static int _currentArchiveIndex;

	private static string _archiveModeName = "";

	private const int WH_KEYBOARD_LL = 13;

	private const int WM_KEYDOWN = 256;

	private const int WM_SYSKEYDOWN = 260;

	private const int LLKHF_ALTDOWN = 32;

	private static LowLevelKeyboardProc _keyboardProc;

	private static IntPtr _hookId = IntPtr.Zero;

	private static uint _gameProcessId = 0u;

	private static readonly int[] POLLED_KEYS = new int[22]
	{
		114, 116, 117, 122, 68, 13, 27, 8, 38, 40,
		37, 39, 32, 49, 50, 51, 52, 53, 54, 55,
		56, 57
	};

	private static readonly Dictionary<int, bool> _keyWasDown = new Dictionary<int, bool>();

	private static readonly Dictionary<int, DateTime> _nextRepeatTimeUtc = new Dictionary<int, DateTime>();

	private static readonly Dictionary<string, object> _gameNodeCache = new Dictionary<string, object>();

	private static readonly Dictionary<string, RevisitableNodeLink> _revisitableChildLinks = new Dictionary<string, RevisitableNodeLink>(StringComparer.Ordinal);

	private static readonly HashSet<string> _revisitableContinueNodeIds = new HashSet<string>(StringComparer.Ordinal);

	private static bool _revisitableLinkCacheBuilt;

	private const int KEY_REPEAT_INITIAL_DELAY_MS = 350;

	private const int KEY_REPEAT_INTERVAL_MS = 120;

	private const int VK_F5 = 116;

	private const int VK_F6 = 117;

	private const int VK_F7 = 118;

	private const int VK_F8 = 119;

	private const int VK_F9 = 120;

	private const int VK_F11 = 122;

	private const int VK_F12 = 123;

	private const int VK_F3 = 114;

	private const int VK_D = 68;

	private const int VK_TAB = 9;

	private const int VK_RETURN = 13;

	private const int VK_ESCAPE = 27;

	private const int VK_BACK = 8;

	private const int VK_UP = 38;

	private const int VK_DOWN = 40;

	private const int VK_LEFT = 37;

	private const int VK_RIGHT = 39;

	private const int VK_SPACE = 32;

	private const int VK_SHIFT = 16;

	private const int VK_LSHIFT = 160;

	private const int VK_RSHIFT = 161;

	private const int VK_CONTROL = 17;

	private const int VK_LCONTROL = 162;

	private const int VK_RCONTROL = 163;

	private const int VK_MENU = 18;

	private const int VK_LMENU = 164;

	private const int VK_RMENU = 165;

	private const int VK_LWIN = 91;

	private const int VK_RWIN = 92;

	private const uint MOUSEEVENTF_LEFTDOWN = 2u;

	private const uint MOUSEEVENTF_LEFTUP = 4u;

	private const uint MOUSEEVENTF_RIGHTDOWN = 8u;

	private const uint MOUSEEVENTF_RIGHTUP = 16u;

	private const int SM_CXSCREEN = 0;

	private const int SM_CYSCREEN = 1;

	private const uint KEYEVENTF_KEYUP = 2u;

	private static IntPtr _timerId = IntPtr.Zero;

	private const uint AUTO_DETECT_INTERVAL = 500u;

	private const uint INPUT_POLL_INTERVAL = 60u;

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

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

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
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		Instance = this;
		Log = ((BaseUnityPlugin)this).Logger;
		Log.LogInfo((object)"========== 插件 东北往事 无障碍插件 v1.0.0 正在加载... ==========");
		Log.LogInfo((object)"[诊断] Awake 被调用，插件对象创建成功");
		try
		{
			ManualLogSource log = Log;
			GameObject gameObject = ((Component)this).gameObject;
			log.LogInfo((object)("[诊断] 插件对象名称: " + (((Object)(object)gameObject != (Object)null) ? ((Object)gameObject).name : null)));
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
			Log.LogInfo((object)"系统键盘钩子仅用于 QTE 空格拦截，其他按键继续由轮询处理");
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
				log3.LogInfo((object)$"[诊断] 对象是否激活: {(((Object)(object)gameObject != (Object)null) ? new bool?(gameObject.activeSelf) : null)}");
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
			StopNativeInputHandlers();
			try
			{
				if (_harmony != null)
				{
					_harmony.UnpatchSelf();
					ManualLogSource log9 = Log;
					if (log9 != null)
					{
						log9.LogInfo((object)"Harmony 补丁已卸载");
					}
				}
			}
			catch (Exception ex2)
			{
				ManualLogSource log10 = Log;
				if (log10 != null)
				{
					log10.LogError((object)("卸载 Harmony 补丁失败: " + ex2.Message));
				}
			}
			try
			{
				TolkHelper.Unload();
				ManualLogSource log11 = Log;
				if (log11 != null)
				{
					log11.LogInfo((object)"Tolk 已关闭");
				}
			}
			catch (Exception ex3)
			{
				ManualLogSource log12 = Log;
				if (log12 != null)
				{
					log12.LogError((object)("关闭 Tolk 失败: " + ex3.Message));
				}
			}
			ManualLogSource log13 = Log;
			if (log13 != null)
			{
				log13.LogInfo((object)"插件清理完成！");
			}
			Instance = null;
			_pluginInitialized = false;
		}
		else
		{
			ManualLogSource log14 = Log;
			if (log14 != null)
			{
				log14.LogInfo((object)"[修复] 对象被意外销毁，但插件功能继续运行！不执行清理");
			}
			ManualLogSource log15 = Log;
			if (log15 != null)
			{
				log15.LogInfo((object)"[修复] 键盘钩子、定时器、Harmony补丁将继续工作");
			}
		}
	}

	private static void StopNativeInputHandlers()
	{
		try
		{
			if (_timerId != IntPtr.Zero)
			{
				KillTimer(IntPtr.Zero, _timerId);
				_timerId = IntPtr.Zero;
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogInfo((object)"定时器已停止");
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogError((object)("停止定时器失败: " + ex.Message));
			}
		}
		try
		{
			if (_hookId != IntPtr.Zero)
			{
				UnhookWindowsHookEx(_hookId);
				_hookId = IntPtr.Zero;
				ManualLogSource log3 = Log;
				if (log3 != null)
				{
					log3.LogInfo((object)"键盘钩子已卸载");
				}
			}
		}
		catch (Exception ex2)
		{
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogError((object)("卸载键盘钩子失败: " + ex2.Message));
			}
		}
	}

	private static void StartAutoDetectTimer()
	{
		if (!(_timerId != IntPtr.Zero))
		{
			Log.LogInfo((object)$"启动自动检测定时器，间隔 {60u} 毫秒");
			TimerProc lpTimerFunc = AutoDetectTimerProc;
			_timerId = SetTimer(IntPtr.Zero, IntPtr.Zero, 60u, lpTimerFunc);
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
			if (IsGameWindowActive())
			{
				PollKeyboardInput();
				if (_needDetect)
				{
					CheckNarrationSpeak();
					_needDetect = false;
					DetectUIState();
				}
			}
			else
			{
				ResetPolledKeyStates();
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

	private static void PollKeyboardInput()
	{
		try
		{
			if (IsModifierKeyDown())
			{
				return;
			}
			for (int i = 0; i < POLLED_KEYS.Length; i++)
			{
				int num = POLLED_KEYS[i];
				bool flag = IsKeyDown(num);
				bool flag2 = _keyWasDown.ContainsKey(num) && _keyWasDown[num];
				_keyWasDown[num] = flag;
				if (!flag)
				{
					_nextRepeatTimeUtc.Remove(num);
					continue;
				}
				DateTime utcNow = DateTime.UtcNow;
				bool flag3 = !flag2;
				if (!flag3 && IsRepeatablePolledKey(num))
				{
					if (!_nextRepeatTimeUtc.TryGetValue(num, out var value))
					{
						value = utcNow.AddMilliseconds(350.0);
						_nextRepeatTimeUtc[num] = value;
					}
					if (utcNow >= value)
					{
						flag3 = true;
						_nextRepeatTimeUtc[num] = utcNow.AddMilliseconds(120.0);
					}
				}
				if (flag3 && ShouldPollHandleKey(num))
				{
					if (!flag2 && IsRepeatablePolledKey(num))
					{
						_nextRepeatTimeUtc[num] = utcNow.AddMilliseconds(350.0);
					}
					_suppressCurrentKey = false;
					HandleKey(num);
					_suppressCurrentKey = false;
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("轮询键盘输入失败: " + ex.Message));
			}
		}
	}

	private static void ResetPolledKeyStates()
	{
		if (_keyWasDown.Count > 0)
		{
			_keyWasDown.Clear();
		}
		if (_nextRepeatTimeUtc.Count > 0)
		{
			_nextRepeatTimeUtc.Clear();
		}
	}

	private static bool IsRepeatablePolledKey(int vkCode)
	{
		if (vkCode != 38 && vkCode != 40 && vkCode != 37)
		{
			return vkCode == 39;
		}
		return true;
	}

	private static bool ShouldPollHandleKey(int vkCode)
	{
		switch (vkCode)
		{
		case 68:
		case 116:
		case 117:
		case 122:
			return true;
		case 32:
			return IsQTEInputActive();
		default:
			if (IsDigitShortcut(vkCode))
			{
				if (_inOptionsMode && _options.Length != 0)
				{
					return AreCurrentOptionsFromGameController();
				}
				return false;
			}
			switch (vkCode)
			{
			case 13:
				if ((!_inOptionsMode || _options.Length == 0) && (!_inSettingsMode || _settings.Length == 0) && (!_inNodeMode || _storylineNodes.Length == 0))
				{
					if (_inArchiveMode)
					{
						return _archiveItems.Length != 0;
					}
					return false;
				}
				return true;
			case 8:
			case 27:
				if (!_inSettingsMode && !_inNodeMode && _currentUIState != UIState.Storyline && !_inArchiveMode)
				{
					return _currentUIState == UIState.Archive;
				}
				return true;
			case 37:
			case 38:
			case 39:
			case 40:
				if ((!_inOptionsMode || _options.Length <= 1) && (!_inSettingsMode || _settings.Length == 0) && (!_inNodeMode || _storylineNodes.Length <= 1) && !_inArchiveMode)
				{
					return _currentUIState == UIState.Archive;
				}
				return true;
			case 114:
				return _currentUIState == UIState.Storyline;
			default:
				return false;
			}
		}
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
				if (clickableOptions != null && clickableOptions.Length != 0)
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
			else if (IsInArchivePage())
			{
				uIState = UIState.Archive;
				text = GetArchiveSignature();
			}
			else if (DateTime.UtcNow < _ignoreOptionsUntilUtc)
			{
				uIState = UIState.Dialogue;
				text = "dialogue";
				LogInputState("DetectUIState options ignored after game selection");
			}
			else
			{
				OptionItem[] endingPageOptions = GetEndingPageOptions();
				if (endingPageOptions != null && endingPageOptions.Length != 0)
				{
					uIState = UIState.Options;
					text = GetOptionsSignature(endingPageOptions);
					LogInputState("DetectUIState ending options=" + endingPageOptions.Length);
				}
				else
				{
					OptionItem[] clickableOptions2 = GetClickableOptions();
					LogInputState("DetectUIState candidates=" + ((clickableOptions2 != null) ? clickableOptions2.Length.ToString() : "null"));
					if (clickableOptions2 != null && clickableOptions2.Length != 0)
					{
						uIState = UIState.Options;
						text = GetOptionsSignature(clickableOptions2);
					}
					else
					{
						OptionItem[] exploreInteractionOptions = GetExploreInteractionOptions();
						if (exploreInteractionOptions != null && exploreInteractionOptions.Length != 0)
						{
							uIState = UIState.Options;
							text = GetOptionsSignature(exploreInteractionOptions);
							LogInputState("DetectUIState explore options=" + exploreInteractionOptions.Length);
						}
						else
						{
							uIState = UIState.Dialogue;
							text = "dialogue";
						}
					}
				}
			}
			LogInputState("DetectUIState raw=" + uIState.ToString() + ", signature=" + text);
			if (_currentUIState == UIState.Storyline && uIState != UIState.Storyline)
			{
				if (uIState == UIState.Options || uIState == UIState.Settings || uIState == UIState.QTE || uIState == UIState.Archive || uIState == UIState.Dialogue)
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
				if (uIState == UIState.Settings || uIState == UIState.Storyline || uIState == UIState.QTE || uIState == UIState.Archive || uIState == UIState.Dialogue || AreCurrentOptionsFromGameController())
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
				EnterStorylineMode(allowNodeRestore: true);
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
			case UIState.Archive:
				Log.LogInfo((object)"进入档案界面");
				EnterArchiveMode();
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
					SpeakQTEPrompt(null, allowRecentRepeat: false);
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

	private static bool IsQTEInputActive()
	{
		if (_currentUIState == UIState.QTE)
		{
			return true;
		}
		if ((DateTime.UtcNow - _lastQTEStartedUtc).TotalSeconds < 2.0)
		{
			return true;
		}
		return IsQTEActive();
	}

	private static bool ShouldSuppressSpaceForQTE()
	{
		if (!(DateTime.UtcNow < _suppressSpaceUntilUtc))
		{
			return IsQTEInputActive();
		}
		return true;
	}

	private static bool ShouldTrySkipQTEFromSuppressedSpace()
	{
		if (_lastQTEController == null && !((DateTime.UtcNow - _lastQTEStartedUtc).TotalSeconds < 2.0))
		{
			return IsQTEActive();
		}
		return true;
	}

	private static void TrySkipQTEFromSpace()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (!((utcNow - _lastQTESkipAttemptUtc).TotalMilliseconds < 300.0))
		{
			_lastQTESkipAttemptUtc = utcNow;
			SkipCurrentQTE();
		}
	}

	private static void ResolveArchiveTypes()
	{
		if (_archiveTypesResolved)
		{
			return;
		}
		_archiveTypesResolved = true;
		try
		{
			_archiveHomePageControllerType = Type.GetType("ArchiveHomePageController, Assembly-CSharp");
			_characterDetailPageControllerType = Type.GetType("CharacterDetailPageController, Assembly-CSharp");
			_archiveContentPageControllerType = Type.GetType("ArchiveContentPageController, Assembly-CSharp");
			_characterCardType = Type.GetType("CharacterCard, Assembly-CSharp");
			_archiveListItemType = Type.GetType("ArchiveListItem, Assembly-CSharp");
			Log.LogInfo((object)$"档案类型解析: home={_archiveHomePageControllerType != null}, detail={_characterDetailPageControllerType != null}, content={_archiveContentPageControllerType != null}, card={_characterCardType != null}, item={_archiveListItemType != null}");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析档案类型失败: " + ex.Message));
		}
	}

	private static bool IsInArchivePage()
	{
		ResolveArchiveTypes();
		object activeArchiveContentController = GetActiveArchiveContentController();
		if (activeArchiveContentController != null && IsArchiveControllerPageVisible(activeArchiveContentController, "") && HasArchiveContentVisibleText(activeArchiveContentController))
		{
			return true;
		}
		object activeCharacterDetailController = GetActiveCharacterDetailController();
		if (activeCharacterDetailController != null && IsArchiveControllerPageVisible(activeCharacterDetailController, "") && GetArchiveDetailItems(activeCharacterDetailController).Length != 0)
		{
			return true;
		}
		object activeArchiveHomeController = GetActiveArchiveHomeController();
		if (activeArchiveHomeController != null && IsArchiveControllerPageVisible(activeArchiveHomeController, "archivePageToggle"))
		{
			return GetArchiveHomeItems(activeArchiveHomeController).Length != 0;
		}
		return false;
	}

	private static string GetArchiveSignature()
	{
		try
		{
			object activeArchiveContentController = GetActiveArchiveContentController();
			if (activeArchiveContentController != null)
			{
				string archiveContentTitle = GetArchiveContentTitle(activeArchiveContentController);
				return "archive_content_" + archiveContentTitle;
			}
			object activeCharacterDetailController = GetActiveCharacterDetailController();
			if (activeCharacterDetailController != null)
			{
				OptionItem[] archiveDetailItems = GetArchiveDetailItems(activeCharacterDetailController);
				return "archive_detail_" + GetTextComponentText(GetFieldValue(activeCharacterDetailController, "characterNameText")) + "_" + archiveDetailItems.Length;
			}
			object activeArchiveHomeController = GetActiveArchiveHomeController();
			if (activeArchiveHomeController != null)
			{
				OptionItem[] archiveHomeItems = GetArchiveHomeItems(activeArchiveHomeController);
				return "archive_home_" + archiveHomeItems.Length;
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("获取档案签名失败: " + ex.Message));
		}
		return "archive";
	}

	private static object GetActiveArchiveHomeController()
	{
		return GetActiveComponentObject(_archiveHomePageControllerType);
	}

	private static object GetActiveCharacterDetailController()
	{
		return GetActiveComponentObject(_characterDetailPageControllerType);
	}

	private static object GetActiveArchiveContentController()
	{
		return GetActiveComponentObject(_archiveContentPageControllerType);
	}

	private static bool IsArchiveControllerPageVisible(object controller, string pageToggleFieldName)
	{
		if (controller == null)
		{
			return false;
		}
		object obj = null;
		if (!string.IsNullOrEmpty(pageToggleFieldName))
		{
			obj = GetFieldValue(controller, pageToggleFieldName);
		}
		if (obj == null)
		{
			obj = GetComponentByType(controller, Type.GetType("ToggleHide, Assembly-CSharp"));
		}
		if (obj != null)
		{
			return IsToggleHideGameObjectActive(obj);
		}
		return IsComponentActiveInHierarchy(controller);
	}

	private static object GetComponentByType(object component, Type componentType)
	{
		if (component == null || componentType == null)
		{
			return null;
		}
		try
		{
			return component.GetType().GetMethod("GetComponent", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Type) }, null)?.Invoke(component, new object[1] { componentType });
		}
		catch
		{
			return null;
		}
	}

	private static object GetActiveComponentObject(Type type)
	{
		if (type == null)
		{
			return null;
		}
		try
		{
			Array array = FindObjectsOfType(type);
			if (array == null)
			{
				return null;
			}
			foreach (object item in array)
			{
				if (item != null && IsComponentActiveInHierarchy(item))
				{
					return item;
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[档案] 查找可见组件失败: " + ex.Message));
		}
		return null;
	}

	private static void EnterArchiveMode()
	{
		try
		{
			_inOptionsMode = false;
			_options = new OptionItem[0];
			_currentOptionIndex = 0;
			_inSettingsMode = false;
			_settings = new SettingItem[0];
			_currentSettingIndex = 0;
			ClearNodeMode("Enter archive");
			object activeArchiveContentController = GetActiveArchiveContentController();
			if (activeArchiveContentController != null)
			{
				_inArchiveMode = true;
				_archiveModeName = "Content";
				_archiveItems = new OptionItem[0];
				_currentArchiveIndex = 0;
				SpeakArchiveContent(activeArchiveContentController);
				return;
			}
			object activeCharacterDetailController = GetActiveCharacterDetailController();
			if (activeCharacterDetailController != null)
			{
				OptionItem[] archiveDetailItems = GetArchiveDetailItems(activeCharacterDetailController);
				SetArchiveItems("Detail", archiveDetailItems, "档案详情");
				return;
			}
			object activeArchiveHomeController = GetActiveArchiveHomeController();
			if (activeArchiveHomeController != null)
			{
				OptionItem[] archiveHomeItems = GetArchiveHomeItems(activeArchiveHomeController);
				SetArchiveItems("Home", archiveHomeItems, "档案首页");
			}
			else
			{
				LeaveArchiveMode();
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("进入档案模式失败: " + ex.Message));
			TolkHelper.Speak("没有找到档案项目", interrupt: true);
		}
	}

	private static void SetArchiveItems(string mode, OptionItem[] items, string label)
	{
		_inArchiveMode = true;
		_archiveModeName = mode;
		_archiveItems = items ?? new OptionItem[0];
		_currentArchiveIndex = 0;
		Log.LogInfo((object)$"进入{label}模式，共 {_archiveItems.Length} 项");
		if (_archiveItems.Length != 0)
		{
			SpeakCurrentArchiveItem();
		}
		else
		{
			TolkHelper.Speak(label + "，没有找到可读项目", interrupt: true);
		}
	}

	private static OptionItem[] GetArchiveHomeItems(object controller)
	{
		List<OptionItem> list = new List<OptionItem>();
		try
		{
			IEnumerable<object> enumerable = EnumerateObjects(GetFieldValue(controller, "characterCards"));
			int num = 0;
			foreach (object item in enumerable)
			{
				if (item != null && IsComponentActiveInHierarchy(item))
				{
					string text = InvokeString(item, "GetCharacterName");
					string text2 = InvokeString(GetFieldValue(item, "characterProfile"), "GetUnlockProgressText");
					if (!string.IsNullOrWhiteSpace(text2))
					{
						text = text + "，解锁 " + text2;
					}
					if (string.IsNullOrWhiteSpace(text))
					{
						text = "角色 " + (num + 1);
					}
					list.Add(new OptionItem
					{
						Text = text,
						ClickableComponent = item,
						Index = num
					});
					num++;
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[档案] 获取角色卡片失败: " + ex.Message));
		}
		return list.ToArray();
	}

	private static OptionItem[] GetArchiveDetailItems(object controller)
	{
		List<OptionItem> list = new List<OptionItem>();
		try
		{
			object fieldValue = GetFieldValue(controller, "archiveListItems");
			int num = 0;
			foreach (object item in EnumerateObjects(fieldValue))
			{
				if (item == null || !IsComponentActiveInHierarchy(item))
				{
					num++;
					continue;
				}
				string archiveListItemText = GetArchiveListItemText(item, num);
				list.Add(new OptionItem
				{
					Text = archiveListItemText,
					ClickableComponent = item,
					Index = num
				});
				num++;
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[档案] 获取档案条目失败: " + ex.Message));
		}
		return list.ToArray();
	}

	private static string GetArchiveListItemText(object item, int index)
	{
		string text = InvokeString(GetFieldValue(item, "archiveEntry"), "GetDisplayTitle");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = GetTextComponentText(GetFieldValue(item, "titleText"));
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "档案 " + (index + 1);
		}
		bool flag = false;
		try
		{
			object fieldValue = GetFieldValue(item, "clickButton");
			PropertyInfo propertyInfo = fieldValue?.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
			if (propertyInfo != null)
			{
				flag = (bool)propertyInfo.GetValue(fieldValue);
			}
		}
		catch
		{
		}
		if (!flag)
		{
			return text + "，未解锁";
		}
		return text;
	}

	private static void SpeakArchiveContent(object controller)
	{
		string archiveContentTitle = GetArchiveContentTitle(controller);
		string textComponentText = GetTextComponentText(GetFieldValue(controller, "characterNameText"));
		string textComponentText2 = GetTextComponentText(GetFieldValue(controller, "archiveContentText"));
		string text = "";
		if (!string.IsNullOrWhiteSpace(textComponentText))
		{
			text = text + textComponentText + "。";
		}
		if (!string.IsNullOrWhiteSpace(archiveContentTitle))
		{
			text = text + archiveContentTitle + "。";
		}
		if (!string.IsNullOrWhiteSpace(textComponentText2))
		{
			text += textComponentText2;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "档案内容页";
		}
		TolkHelper.Speak(text, interrupt: true);
	}

	private static string GetArchiveContentTitle(object controller)
	{
		return GetTextComponentText(GetFieldValue(controller, "archiveTitleText"));
	}

	private static bool HasArchiveContentVisibleText(object controller)
	{
		if (controller == null)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(GetArchiveContentTitle(controller)))
		{
			return !string.IsNullOrWhiteSpace(GetTextComponentText(GetFieldValue(controller, "archiveContentText")));
		}
		return true;
	}

	private static bool HandleArchiveKey(int vkCode)
	{
		if (!_inArchiveMode && _currentUIState != UIState.Archive)
		{
			return false;
		}
		if (!IsInArchivePage())
		{
			LeaveArchiveMode();
			return false;
		}
		switch (vkCode)
		{
		case 38:
			if (_archiveItems.Length != 0)
			{
				_currentArchiveIndex--;
				if (_currentArchiveIndex < 0)
				{
					_currentArchiveIndex = _archiveItems.Length - 1;
				}
				SpeakCurrentArchiveItem();
				_suppressCurrentKey = true;
			}
			return true;
		case 40:
			if (_archiveItems.Length != 0)
			{
				_currentArchiveIndex++;
				if (_currentArchiveIndex >= _archiveItems.Length)
				{
					_currentArchiveIndex = 0;
				}
				SpeakCurrentArchiveItem();
				_suppressCurrentKey = true;
			}
			return true;
		case 13:
			ActivateCurrentArchiveItem();
			_suppressCurrentKey = true;
			return true;
		case 37:
			if (TryClickArchiveButton("leftArrowButton"))
			{
				MarkNeedDetect();
				_suppressCurrentKey = true;
				return true;
			}
			return false;
		case 39:
			if (TryClickArchiveButton("rightArrowButton"))
			{
				MarkNeedDetect();
				_suppressCurrentKey = true;
				return true;
			}
			return false;
		case 8:
		case 27:
			if (TryClickArchiveButton("returnButton"))
			{
				LeaveArchiveMode();
				MarkNeedDetect();
				_suppressCurrentKey = true;
				return true;
			}
			return false;
		default:
			return false;
		}
	}

	private static void SpeakCurrentArchiveItem()
	{
		if (_archiveItems == null || _archiveItems.Length == 0)
		{
			TolkHelper.Speak("没有档案项目", interrupt: true);
			return;
		}
		if (_currentArchiveIndex < 0)
		{
			_currentArchiveIndex = 0;
		}
		if (_currentArchiveIndex >= _archiveItems.Length)
		{
			_currentArchiveIndex = _archiveItems.Length - 1;
		}
		OptionItem optionItem = _archiveItems[_currentArchiveIndex];
		string text = (string.IsNullOrWhiteSpace(optionItem.Text) ? ("第 " + (_currentArchiveIndex + 1) + " 项") : optionItem.Text);
		PlayGameSound("Highlight");
		TolkHelper.Speak(text, interrupt: true);
	}

	private static void ActivateCurrentArchiveItem()
	{
		if (_archiveItems == null || _archiveItems.Length == 0)
		{
			TolkHelper.Speak("没有可打开的档案项目", interrupt: true);
			return;
		}
		OptionItem optionItem = _archiveItems[Mathf.Clamp(_currentArchiveIndex, 0, _archiveItems.Length - 1)];
		if (optionItem == null || optionItem.ClickableComponent == null)
		{
			TolkHelper.Speak("当前档案项目不可打开", interrupt: true);
			return;
		}
		PlayGameSound("Click");
		TolkHelper.Speak("打开 " + optionItem.Text, interrupt: true);
		if (ClickArchiveItem(optionItem.ClickableComponent))
		{
			MarkNeedDetect();
		}
		else
		{
			TolkHelper.Speak("打开失败", interrupt: true);
		}
	}

	private static bool TryClickArchiveButton(string fieldName)
	{
		object activeArchiveContentController = GetActiveArchiveContentController();
		object activeCharacterDetailController = GetActiveCharacterDetailController();
		object activeArchiveHomeController = GetActiveArchiveHomeController();
		object obj = GetFieldValue(activeArchiveContentController, fieldName) ?? GetFieldValue(activeCharacterDetailController, fieldName) ?? GetFieldValue(activeArchiveHomeController, fieldName);
		if (obj == null)
		{
			return false;
		}
		PlayGameSound((fieldName == "returnButton") ? "Back" : "Highlight");
		return ClickComponent(obj);
	}

	private static bool ClickArchiveItem(object component)
	{
		if (component == null)
		{
			return false;
		}
		try
		{
			object obj = GetFieldValue(component, "cardButton") ?? GetFieldValue(component, "clickButton");
			if (obj != null && ClickComponent(obj))
			{
				Log.LogInfo((object)("[档案] 已点击内部按钮: " + component.GetType().Name));
				return true;
			}
			string text = null;
			Type type = component.GetType();
			if (_characterCardType != null && type == _characterCardType)
			{
				text = "OnCardButtonClicked";
			}
			else if (_archiveListItemType != null && type == _archiveListItemType)
			{
				text = "OnButtonClicked";
			}
			if (!string.IsNullOrEmpty(text) && InvokeNoArg(component, text))
			{
				Log.LogInfo((object)("[档案] 已调用内部点击方法: " + type.Name + "." + text));
				return true;
			}
			return ClickComponent(component);
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[档案] 点击档案项目失败: " + ex.Message));
			return false;
		}
	}

	private static void LeaveArchiveMode()
	{
		_inArchiveMode = false;
		_archiveItems = new OptionItem[0];
		_currentArchiveIndex = 0;
		_archiveModeName = "";
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
		OptionItem[] endingPageOptions = GetEndingPageOptions();
		if (endingPageOptions != null && endingPageOptions.Length != 0)
		{
			SetOptions(endingPageOptions);
			return;
		}
		OptionItem[] array = GetClickableOptions();
		if (array == null || array.Length == 0)
		{
			array = GetExploreInteractionOptions();
			if (array == null || array.Length == 0)
			{
				LeaveOptions();
				return;
			}
			Log.LogInfo((object)$"[探索] 进入探索交互模式，共 {array.Length} 个交互点");
		}
		SetOptions(SortOptions(array));
	}

	private static OptionItem[] GetOptionsFromGameController()
	{
		try
		{
			if (DateTime.UtcNow < _ignoreOptionsUntilUtc)
			{
				Log.LogDebug((object)"[精准检测] 刚处理过剧情选项，暂不从 GameController 获取旧选项");
				return new OptionItem[0];
			}
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
			foreach (OptionItem optionItem in array)
			{
				if (optionItem.ClickableComponent != null)
				{
					list.Add(optionItem);
				}
			}
			OptionItem[] confirmationDialogOptions = GetConfirmationDialogOptions(list);
			if (confirmationDialogOptions.Length >= 2)
			{
				Log.LogInfo((object)$"[选项过滤] 检测到确认弹窗，仅保留 {confirmationDialogOptions.Length} 个弹窗按钮");
				return confirmationDialogOptions;
			}
			if (list.Count >= 2 && list.Count <= 12)
			{
				return list.ToArray();
			}
			OptionItem[] singleStartOptions = GetSingleStartOptions(allVisibleTextsWithPosition);
			if (list.Count < 2 && singleStartOptions.Length != 0)
			{
				Log.LogInfo((object)("[选项过滤] 检测到单按钮启动页: " + singleStartOptions[0].Text));
				return singleStartOptions;
			}
			if (list.Count > 12)
			{
				Log.LogInfo((object)$"[选项过滤] 可点击候选过多({list.Count})，疑似包含后台页面，改用短文本筛选");
			}
			string[] array2 = new string[34]
			{
				"威望", "好感", "返回", "版本", "Version", "Demo", "进度", "章节", "第", "章",
				"节", "回", "设置", "选项", "音量", "画质", "分辨率", "全屏", "语言", "字幕",
				"确定", "取消", "确认", "关闭", "打开", "上一页", "下一页", "上一页", "下一页", "+",
				"-", "％", "%", "/"
			};
			List<OptionItem> list2 = new List<OptionItem>();
			array = allVisibleTextsWithPosition;
			foreach (OptionItem optionItem2 in array)
			{
				string text = optionItem2.Text.Trim();
				if (string.IsNullOrEmpty(text) || text.Length >= 20)
				{
					continue;
				}
				bool flag = false;
				string[] array3 = array2;
				foreach (string value in array3)
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
					if ((c >= '一' && c <= '\u9fff') || char.IsLetter(c))
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
			for (int k = 0; k < Math.Min(list2.Count, val); k++)
			{
				bool flag3 = false;
				foreach (OptionItem item in list3)
				{
					if (item.Text == list2[k].Text)
					{
						flag3 = true;
						break;
					}
				}
				if (!flag3)
				{
					list3.Add(list2[k]);
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

	private static OptionItem[] GetConfirmationDialogOptions(IEnumerable<OptionItem> candidates)
	{
		if (candidates == null)
		{
			return new OptionItem[0];
		}
		string[] values = new string[5] { "确定", "确认", "是", "退出", "离开" };
		string[] values2 = new string[5] { "取消", "否", "返回", "关闭", "不" };
		List<OptionItem> list = new List<OptionItem>();
		List<OptionItem> list2 = new List<OptionItem>();
		foreach (OptionItem candidate in candidates)
		{
			string text = candidate?.Text?.Trim();
			if (!string.IsNullOrEmpty(text) && text.Length <= 8)
			{
				if (MatchesAnyDialogButtonText(text, values))
				{
					list.Add(candidate);
				}
				else if (MatchesAnyDialogButtonText(text, values2))
				{
					list2.Add(candidate);
				}
			}
		}
		if (list.Count == 0 || list2.Count == 0)
		{
			return new OptionItem[0];
		}
		List<OptionItem> list3 = new List<OptionItem>();
		list3.AddRange(list);
		list3.AddRange(list2);
		return SortOptions(list3.ToArray());
	}

	private static bool MatchesAnyDialogButtonText(string text, string[] values)
	{
		if (string.IsNullOrWhiteSpace(text) || values == null)
		{
			return false;
		}
		foreach (string value in values)
		{
			if (text.Equals(value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (text.Length <= 4 && text.Contains(value))
			{
				return true;
			}
		}
		return false;
	}

	private static OptionItem[] GetSingleStartOptions(OptionItem[] visibleTexts)
	{
		if (visibleTexts == null || visibleTexts.Length == 0)
		{
			return new OptionItem[0];
		}
		string[] array = new string[6] { "点击开始", "开始游戏", "新游戏", "继续游戏", "开始", "继续" };
		List<OptionItem> list = new List<OptionItem>();
		foreach (OptionItem optionItem in visibleTexts)
		{
			string text = optionItem?.Text?.Trim();
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string value in array2)
			{
				if (text.Equals(value, StringComparison.OrdinalIgnoreCase) || text.Contains(value))
				{
					list.Add(optionItem);
					break;
				}
			}
		}
		if (list.Count == 0 || list.Count > 3)
		{
			return new OptionItem[0];
		}
		return (from o in list
			group o by o.Text into g
			select g.First()).ToArray();
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
			foreach (OptionItem optionItem in array3)
			{
				string[] array4 = array2;
				foreach (string value in array4)
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
				TolkHelper.Speak("没有设置项", interrupt: true);
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
				SettingItem[] array = (_settings = list.OrderBy((SettingItem s) => s.ScreenY).ToArray());
				LogSettingsList(array);
				_currentSettingIndex = 0;
				_inSettingsMode = true;
				_inOptionsMode = false;
				if (array.Length != 0)
				{
					SpeakCurrentSetting();
				}
			}
			else
			{
				TolkHelper.Speak("没有设置项", interrupt: true);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("进入设置模式失败: " + ex.Message));
			TolkHelper.Speak("没有设置项", interrupt: true);
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
			object obj = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			if (obj != null)
			{
				Type nestedType = type.GetNestedType("SoundType", BindingFlags.Public);
				if (!(nestedType == null))
				{
					object obj2 = Enum.Parse(nestedType, soundName);
					type.GetMethod("PlaySound", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj, new object[1] { obj2 });
				}
			}
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
				Log.LogInfo((object)string.Format("[设置列表] {0}/{1}: {2}, type={3}, y={4}, component={5}", i + 1, items.Length, settingItem.Name, settingItem.Type, settingItem.ScreenY, settingItem.Component?.GetType().Name ?? "null"));
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
			Type type = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI");
			Type type2 = Type.GetType("UnityEngine.UI.Toggle, UnityEngine.UI");
			Type type3 = Type.GetType("UnityEngine.UI.Dropdown, UnityEngine.UI");
			Type type4 = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
			if ((type4 != null && type4.IsInstanceOfType(component)) || (type3 != null && type3.IsInstanceOfType(component)))
			{
				return CreateDropdownSettingItem(settingItem, component);
			}
			if (type2 != null && type2.IsInstanceOfType(component))
			{
				return CreateToggleSettingItem(settingItem, component, type2);
			}
			if (type != null && type.IsInstanceOfType(component))
			{
				return CreateSliderSettingItem(settingItem, component, type);
			}
			object obj = FindComponentNear(component, type);
			if (type != null && obj != null)
			{
				return CreateSliderSettingItem(settingItem, obj, type);
			}
			object obj2 = FindComponentNear(component, type2);
			if (type2 != null && obj2 != null)
			{
				return CreateToggleSettingItem(settingItem, obj2, type2);
			}
			object obj3 = FindComponentNear(component, type4);
			if (obj3 == null)
			{
				obj3 = FindComponentNear(component, type3);
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
			object fieldValue = GetFieldValue(activeObject, "languageDropdownController");
			AddSettingFromField(list, fieldValue, "languageDropdown", "语言", ref order);
			object owner = GetFieldValue(activeObject, "volumeController") ?? GetActiveObject(_audioManagerType);
			AddSettingFromField(list, owner, "volumeSlider", "主音量", ref order);
			AddSettingFromField(list, owner, "soundEffectSlider", "音效音量", ref order);
			AddSettingFromValue(list, GetFieldValue(activeObject, "heroVoiceDropdown"), "男主声音", ref order);
			object owner2 = GetFieldValue(activeObject, "resolutionSettingsController") ?? GetActiveObject(Type.GetType("ResolutionSettingsController, Assembly-CSharp"));
			AddSettingFromField(list, owner2, "resolutionDropdown", "分辨率", ref order);
			AddSettingFromField(list, owner2, "displayModeDropdown", "显示模式", ref order);
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
			if (text.Length > 30 || IsSettingTextAlreadyCovered(list, text) || IsStandaloneSettingValue(text))
			{
				continue;
			}
			bool flag = false;
			string[] array2 = array;
			foreach (string value in array2)
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
			SettingItem settingItem;
			switch (text)
			{
			case "返回":
			case "关闭":
			case "Back":
				if (optionItem.ClickableComponent != null)
				{
					settingItem = new SettingItem
					{
						Name = text,
						Type = SettingItem.SettingType.Button,
						Component = optionItem.ClickableComponent,
						ClickComponent = optionItem.ClickableComponent
					};
					break;
				}
				goto default;
			default:
				settingItem = CreateKnownFallbackSetting(optionItem);
				if (settingItem == null)
				{
					string name = BuildSettingTextWithValue(optionItem, visibleTexts);
					settingItem = new SettingItem
					{
						Name = name,
						Type = SettingItem.SettingType.Text
					};
				}
				break;
			case "系统设置":
				continue;
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
			object activeObject = GetActiveObject(_settingsType);
			object activeObject2 = GetActiveObject(_audioManagerType);
			object obj = GetFieldValue(activeObject, "volumeController") ?? activeObject2;
			object obj2 = GetFieldValue(activeObject, "resolutionSettingsController") ?? GetActiveObject(Type.GetType("ResolutionSettingsController, Assembly-CSharp"));
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
				return CreateNamedSettingFromComponent(GetFieldValue(GetFieldValue(activeObject, "languageDropdownController"), "languageDropdown"), "语言", label);
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
				SettingItem settingItem = CreateNamedSettingFromComponent(GetFieldValue(GetActiveObject(Type.GetType("SubtitleSettingsController, Assembly-CSharp")), "subtitleDropdown"), "字幕开关", label);
				if (settingItem != null)
				{
					Log.LogInfo((object)"[设置兜底控件] 字幕开关: Dropdown");
					return settingItem;
				}
			}
			if (text.Contains("敏感词消音") || text.Contains("外置音轨") || text.Contains("音轨"))
			{
				SettingItem settingItem2 = CreateNamedSettingFromComponent(GetFieldValue(GetActiveObject(Type.GetType("AudioTrackSettingsController, Assembly-CSharp")), "audioTrackDropdown"), "敏感词消音", label);
				if (settingItem2 != null)
				{
					Log.LogInfo((object)"[设置兜底控件] 敏感词消音: Dropdown");
					return settingItem2;
				}
			}
			if (text.Contains("男主声音") || text.Contains("男主"))
			{
				SettingItem settingItem3 = CreateNamedSettingFromComponent(GetFieldValue(activeObject, "heroVoiceDropdown"), "男主声音", label);
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
		SettingItem settingItem = CreateSettingItemFromComponent(component, name, label?.ScreenY ?? 0f);
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
		string text2 = FindNearbySettingValue(label, visibleTexts);
		if (!string.IsNullOrWhiteSpace(text2) && !text.Contains(text2))
		{
			return text + " " + text2;
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
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Expected I4, but got Unknown
		if (string.IsNullOrWhiteSpace(label))
		{
			return "";
		}
		label = label.Trim();
		try
		{
			if (label.Contains("字幕"))
			{
				return (PlayerPrefs.GetInt("SubtitleVisible", 1) != 0) ? "开启" : "关闭";
			}
			if (label.Contains("敏感词消音") || label.Contains("外置音轨") || label.Contains("音轨"))
			{
				return (PlayerPrefs.GetInt("ExternalAudioTrack", 0) == 1) ? "开启" : "关闭";
			}
			if (label.Contains("男主声音") || label.Contains("男主"))
			{
				return (PlayerPrefs.GetInt("HeroVoice", 1) == 1) ? "开启" : "关闭";
			}
			if (label.Contains("语言"))
			{
				return NormalizeLanguageName(PlayerPrefs.GetString("GameLanguage", "Chinese"));
			}
			if (label.Contains("分辨率"))
			{
				int num = PlayerPrefs.GetInt("ResolutionWidth", 0);
				int num2 = PlayerPrefs.GetInt("ResolutionHeight", 0);
				if (num <= 0 || num2 <= 0)
				{
					num = Screen.width;
					num2 = Screen.height;
				}
				if (num > 0 && num2 > 0)
				{
					return $"{num}x{num2}";
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
		return language.Trim() switch
		{
			"Chinese" => "中文", 
			"Traditional" => "繁体中文", 
			"English" => "English", 
			"Japanese" => "日语", 
			"Korean" => "韩语", 
			_ => language.Trim(), 
		};
	}

	private static string NormalizeDisplayModeName(int mode)
	{
		if (mode == 3)
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
		switch (text)
		{
		default:
			if (!IsResolutionValue(text) && !text.EndsWith("%"))
			{
				return text.EndsWith("％");
			}
			break;
		case "开启":
		case "关闭":
		case "打开":
		case "全屏":
		case "窗口":
			break;
		}
		return true;
	}

	private static string FindNearbySettingValue(OptionItem label, OptionItem[] visibleTexts)
	{
		OptionItem optionItem = null;
		float num = float.MaxValue;
		string label2 = (label?.Text ?? "").Trim();
		foreach (OptionItem optionItem2 in visibleTexts)
		{
			if (optionItem2 == null || optionItem2 == label || string.IsNullOrWhiteSpace(optionItem2.Text))
			{
				continue;
			}
			string value = optionItem2.Text.Trim();
			if (!IsLikelySettingValueForLabel(label2, value))
			{
				continue;
			}
			float num2 = Math.Abs(optionItem2.ScreenY - label.ScreenY);
			if (!(num2 > 80f))
			{
				float num3 = Math.Abs(optionItem2.ScreenX - label.ScreenX);
				float num4 = num2 * 4f + num3;
				if (num4 < num)
				{
					num = num4;
					optionItem = optionItem2;
				}
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
			if (!(value == "全屏"))
			{
				return value == "窗口";
			}
			return true;
		}
		if (label.Contains("语言"))
		{
			if (!(value == "中文") && !(value == "English") && !value.Contains("日本") && !value.Contains("한국"))
			{
				return value.Contains("繁");
			}
			return true;
		}
		if (label.Contains("音量"))
		{
			if (!value.EndsWith("%"))
			{
				return value.EndsWith("％");
			}
			return true;
		}
		if (label.Contains("字幕") || label.Contains("消音") || label.Contains("男主") || label.Contains("音轨"))
		{
			switch (value)
			{
			default:
				return value == "OFF";
			case "开启":
			case "关闭":
			case "打开":
			case "ON":
				return true;
			}
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
		switch (text)
		{
		case "开启":
		case "关闭":
		case "打开":
		case "全屏":
		case "窗口":
		case "中文":
		case "English":
		case "ON":
		case "OFF":
			return true;
		default:
		{
			if (text.EndsWith("%") || text.EndsWith("％"))
			{
				return true;
			}
			bool flag = false;
			bool flag2 = false;
			string text2 = text;
			foreach (char c in text2)
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
		}
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
			return obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
		}
		catch
		{
			return null;
		}
	}

	private static bool SetFieldValue(object obj, string fieldName, object value)
	{
		if (obj == null || string.IsNullOrEmpty(fieldName))
		{
			return false;
		}
		try
		{
			FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return false;
			}
			field.SetValue(obj, value);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerable<object> EnumerateObjects(object value)
	{
		if (value == null)
		{
			yield break;
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			foreach (object item in enumerable)
			{
				yield return item;
			}
		}
		else
		{
			yield return value;
		}
	}

	private static string InvokeString(object obj, string methodName)
	{
		if (obj == null || string.IsNullOrEmpty(methodName))
		{
			return "";
		}
		try
		{
			MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method == null)
			{
				return "";
			}
			return method.Invoke(obj, null)?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static void AddSettingFromField(List<SettingItem> list, object owner, string fieldName, string name, ref float order)
	{
		AddSettingFromValue(list, GetFieldValue(owner, fieldName), name, ref order);
	}

	private static void AddSettingFromValue(List<SettingItem> list, object component, string name, ref float order)
	{
		if (list != null && component != null)
		{
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
	}

	private static void ApplyKnownSettingOptions(SettingItem item)
	{
		if (item != null && item.Type == SettingItem.SettingType.Dropdown)
		{
			if (item.Name == "男主声音" || item.Name == "外置音轨" || item.Name == "敏感词消音" || item.Name == "字幕开关")
			{
				item.Options = new string[2] { "关闭", "开启" };
			}
			else if (item.Name == "显示模式")
			{
				item.Options = new string[2] { "全屏", "窗口" };
			}
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
			if (optionItem != null && optionItem.ClickableComponent != null)
			{
				string text = (optionItem.Text ?? "").Trim();
				switch (text)
				{
				case "返回":
				case "关闭":
				case "Back":
				{
					SettingItem result = new SettingItem
					{
						Name = text,
						Type = SettingItem.SettingType.Button,
						Component = optionItem.ClickableComponent,
						ClickComponent = optionItem.ClickableComponent,
						ScreenY = order,
						ScreenX = optionItem.ScreenX,
						HasScreenPosition = optionItem.HasScreenPosition
					};
					order += 100f;
					Log.LogInfo((object)$"[设置精准采集] 返回按钮: screen=({optionItem.ScreenX},{optionItem.ScreenY}), hasPos={optionItem.HasScreenPosition}");
					return result;
				}
				}
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
			if (settingItem != null && settingItem.Type == settingType && !ContainsSettingComponent(list, settingItem.Component))
			{
				SetSettingScreenPosition(settingItem);
				settingItem.Name = GuessSettingNameFromPosition(settingItem, visibleTexts, settingType);
				if (!IsWeakSettingName(settingItem.Name))
				{
					settingItem.ScreenY = order;
					ApplyKnownSettingOptions(settingItem);
					list.Add(settingItem);
					order += 100f;
					Log.LogInfo((object)$"[设置控件扫描] {settingItem.Name}: {settingItem.Type}");
				}
			}
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
			return IsGameObjectActiveInHierarchy(GetGameObjectFromComponent(component));
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
			if (TryGetScreenPosition(GetGameObjectFromComponent(item.Component), out var x, out var y))
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
			if (!IsWeakSettingName(text) && !(text == "返回") && !(text == "系统设置"))
			{
				float num2 = Math.Abs(optionItem2.ScreenY - item.ScreenY);
				float num3 = Math.Abs(optionItem2.ScreenX - item.ScreenX);
				float num4 = num2 * 3f + num3;
				if (num2 < 90f && num4 < num)
				{
					num = num4;
					optionItem = optionItem2;
				}
			}
		}
		if (optionItem != null)
		{
			return CleanSettingName(optionItem.Text);
		}
		return settingType switch
		{
			SettingItem.SettingType.Slider => "音量", 
			SettingItem.SettingType.Dropdown => "选项", 
			SettingItem.SettingType.Toggle => "开关", 
			_ => "", 
		};
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
			object gameObjectFromComponent = GetGameObjectFromComponent(component);
			if (gameObjectFromComponent != null)
			{
				object obj = InvokeComponentLookup(gameObjectFromComponent, "GetComponent", targetType);
				if (obj != null)
				{
					return obj;
				}
				obj = InvokeComponentLookup(gameObjectFromComponent, "GetComponentInChildren", targetType);
				if (obj != null)
				{
					return obj;
				}
			}
			object componentOrGameObject = component;
			for (int i = 0; i < 6; i++)
			{
				object parentGameObject = GetParentGameObject(componentOrGameObject);
				if (parentGameObject != null)
				{
					object obj2 = InvokeComponentLookup(parentGameObject, "GetComponent", targetType);
					if (obj2 != null)
					{
						return obj2;
					}
					obj2 = InvokeComponentLookup(parentGameObject, "GetComponentInChildren", targetType);
					if (obj2 != null)
					{
						return obj2;
					}
					componentOrGameObject = parentGameObject;
					continue;
				}
				break;
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
		return component?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
	}

	private static object GetParentGameObject(object componentOrGameObject)
	{
		if (componentOrGameObject == null)
		{
			return null;
		}
		try
		{
			object obj = GetGameObjectFromComponent(componentOrGameObject) ?? componentOrGameObject;
			object obj2 = obj.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			if (obj2 == null)
			{
				return null;
			}
			object obj3 = obj2.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj2);
			return obj3?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj3);
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
				obj = type.GetProperty("main", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			}
			object obj2 = gameObject.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(gameObject);
			if (obj2 == null)
			{
				return false;
			}
			object obj3 = obj2.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj2);
			if (obj3 == null || obj == null || type == null)
			{
				return false;
			}
			object obj4 = type.GetMethod("WorldToScreenPoint", new Type[1] { obj3.GetType() })?.Invoke(obj, new object[1] { obj3 });
			if (obj4 == null)
			{
				return false;
			}
			PropertyInfo property = obj4.GetType().GetProperty("x");
			PropertyInfo property2 = obj4.GetType().GetProperty("y");
			if (property == null || property2 == null)
			{
				return false;
			}
			x = Convert.ToSingle(property.GetValue(obj4));
			y = Convert.ToSingle(property2.GetValue(obj4));
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
				if (optionItem2 != null && !IsWeakSettingName(CleanSettingName(optionItem2.Text)))
				{
					float num2 = Math.Abs(optionItem2.ScreenY - setting.ScreenY);
					if (num2 < num)
					{
						num = num2;
						optionItem = optionItem2;
					}
				}
			}
			if (optionItem != null && num < 80f)
			{
				return CleanSettingName(optionItem.Text);
			}
		}
		return setting.Type switch
		{
			SettingItem.SettingType.Slider => "滑块", 
			SettingItem.SettingType.Toggle => "开关", 
			SettingItem.SettingType.Dropdown => "选项", 
			_ => "设置项", 
		};
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
			"开启", "关闭", "打开", "关", "开", "ON", "OFF", "On", "Off", "%",
			"％", "："
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
		if (text.Length > 1)
		{
			switch (text)
			{
			default:
				if (!text.EndsWith("%"))
				{
					return text.EndsWith("％");
				}
				break;
			case "开启":
			case "关闭":
			case "打开":
			case "开":
			case "关":
			case "ON":
			case "OFF":
				break;
			}
		}
		return true;
	}

	private static string GetDropdownOptionText(object options, int index)
	{
		try
		{
			object obj = options.GetType().GetProperty("Item")?.GetValue(options, new object[1] { index });
			if (obj != null)
			{
				string text = obj.GetType().GetProperty("text")?.GetValue(obj) as string;
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
				PropertyInfo property2 = item.Component.GetType().GetProperty("value");
				if (property2 != null)
				{
					item.Value = Convert.ToSingle(property2.GetValue(item.Component));
				}
				PropertyInfo property3 = item.Component.GetType().GetProperty("minValue");
				if (property3 != null)
				{
					item.MinValue = Convert.ToSingle(property3.GetValue(item.Component));
				}
				PropertyInfo property4 = item.Component.GetType().GetProperty("maxValue");
				if (property4 != null)
				{
					item.MaxValue = Convert.ToSingle(property4.GetValue(item.Component));
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
				PropertyInfo property = item.Component.GetType().GetProperty("value");
				if (property != null)
				{
					item.SelectedIndex = (int)property.GetValue(item.Component);
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
		PlayGameSound("Highlight");
		TolkHelper.Speak(string.IsNullOrWhiteSpace(settingValueText) ? settingItem.Name : (settingItem.Name + " " + settingValueText), interrupt: true);
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
				object obj = settingItem.ClickComponent ?? settingItem.Component;
				if (obj != null && ClickComponent(obj))
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
			int num = ((item.SelectedIndex == 0) ? 1 : 0);
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
				type.GetMethod("UnloadSceneAsync", new Type[1] { typeof(string) })?.Invoke(null, new object[1] { "Settings" });
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
			object obj = component.GetType().GetProperty("onValueChanged")?.GetValue(component);
			MethodInfo methodInfo = obj?.GetType().GetMethod("Invoke");
			if (methodInfo != null)
			{
				methodInfo.Invoke(obj, new object[1] { value });
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
				Type type = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI");
				if (!(type != null))
				{
					break;
				}
				PropertyInfo property2 = type.GetProperty("value");
				if (property2 != null)
				{
					float num = (item.MaxValue - item.MinValue) / 10f;
					float val3 = item.Value + (float)direction * num;
					val3 = Math.Max(item.MinValue, Math.Min(val3, item.MaxValue));
					if (Math.Abs(val3 - item.Value) > 0.001f)
					{
						PlayGameSound("Highlight");
					}
					property2.SetValue(item.Component, val3);
					item.Value = val3;
					InvokeValueChanged(item.Component, val3);
				}
				break;
			}
			case SettingItem.SettingType.Dropdown:
			{
				PropertyInfo property = item.Component.GetType().GetProperty("value");
				if (property != null)
				{
					int val = item.SelectedIndex + direction;
					int val2 = ((item.Options != null) ? (item.Options.Length - 1) : 10);
					val = Math.Max(0, Math.Min(val, val2));
					if (val != item.SelectedIndex)
					{
						PlayGameSound("Highlight");
						property.SetValue(item.Component, val);
						item.SelectedIndex = val;
						InvokeValueChanged(item.Component, val);
					}
				}
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
			return _gameProcessId != 0 && lpdwProcessId == _gameProcessId;
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("检查窗口焦点失败: " + ex.Message));
			}
			return false;
		}
	}

	private static void ResolveEndingTypes()
	{
		if (_endingTypesResolved)
		{
			return;
		}
		_endingTypesResolved = true;
		try
		{
			_endingPageControllerType = Type.GetType("EndingPageController, Assembly-CSharp");
			if (_endingPageControllerType != null)
			{
				Log.LogInfo((object)"找到 EndingPageController 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 EndingPageController 类型，结尾页精准读取不可用");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析结尾页类型失败: " + ex.Message));
		}
	}

	private static OptionItem[] GetEndingPageOptions()
	{
		try
		{
			ResolveEndingTypes();
			if (_endingPageControllerType == null)
			{
				return new OptionItem[0];
			}
			Array array = FindObjectsOfType(_endingPageControllerType);
			if (array == null || array.Length == 0)
			{
				return new OptionItem[0];
			}
			foreach (object item in array)
			{
				if (IsComponentActiveInHierarchy(item))
				{
					List<OptionItem> list = new List<OptionItem>();
					string text = BuildEndingPageText(item);
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(new OptionItem
						{
							Text = "结尾提示：" + text,
							Index = -1
						});
					}
					AddEndingButtonOption(list, item, "returnToMainButton", "返回故事线", -9001);
					AddEndingButtonOption(list, item, "gotoStorylineButton", "前往故事线", -9002);
					AddFallbackEndingButtonOptions(list, item);
					if (list.Count > 0)
					{
						Log.LogInfo((object)$"[结尾页] 检测到 {list.Count} 个可读项");
						return list.ToArray();
					}
				}
			}
			return new OptionItem[0];
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("检测结尾页失败: " + ex.Message));
			return new OptionItem[0];
		}
	}

	private static string BuildEndingPageText(object endingController)
	{
		string textComponentText = GetTextComponentText(GetFieldValue(endingController, "endingTitleText"));
		string textComponentText2 = GetTextComponentText(GetFieldValue(endingController, "endingDescriptionText"));
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(textComponentText))
		{
			list.Add(NormalizeReadableText(textComponentText));
		}
		if (!string.IsNullOrWhiteSpace(textComponentText2) && !string.Equals(textComponentText.Trim(), textComponentText2.Trim(), StringComparison.Ordinal))
		{
			list.Add(NormalizeReadableText(textComponentText2));
		}
		return string.Join("。", list.Where((string s) => !string.IsNullOrWhiteSpace(s)));
	}

	private static void AddEndingButtonOption(List<OptionItem> list, object endingController, string fieldName, string fallbackText, int actionIndex)
	{
		if (list == null || endingController == null)
		{
			return;
		}
		object fieldValue = GetFieldValue(endingController, fieldName);
		if (fieldValue != null)
		{
			string text = GetButtonText(fieldValue);
			if (string.IsNullOrWhiteSpace(text))
			{
				text = fallbackText;
			}
			list.Add(new OptionItem
			{
				Text = NormalizeReadableText(text),
				ClickableComponent = endingController,
				Index = actionIndex
			});
		}
	}

	private static void AddFallbackEndingButtonOptions(List<OptionItem> list, object endingController)
	{
		if (list == null || endingController == null)
		{
			return;
		}
		try
		{
			FieldInfo[] fields = endingController.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (fieldInfo == null || fieldInfo.FieldType == null || fieldInfo.FieldType.Name != "Button")
				{
					continue;
				}
				object value = fieldInfo.GetValue(endingController);
				if (value != null)
				{
					string text = NormalizeReadableText(GetButtonText(value));
					if (string.IsNullOrWhiteSpace(text))
					{
						text = GuessEndingButtonName(fieldInfo.Name);
					}
					if (!string.IsNullOrWhiteSpace(text) && !ContainsOptionText(list, text))
					{
						list.Add(new OptionItem
						{
							Text = text,
							ClickableComponent = value,
							Index = list.Count
						});
						Log.LogInfo((object)("[结尾页] 兜底加入按钮: " + fieldInfo.Name + " -> " + text));
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[结尾页] 兜底扫描按钮失败: " + ex.Message));
		}
	}

	private static string GuessEndingButtonName(string fieldName)
	{
		if (string.IsNullOrWhiteSpace(fieldName))
		{
			return "";
		}
		if (fieldName.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0 || fieldName.Contains("Main"))
		{
			return "返回故事线";
		}
		if (fieldName.IndexOf("story", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "前往故事线";
		}
		return "";
	}

	private static bool ContainsOptionText(List<OptionItem> list, string text)
	{
		if (list == null || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		foreach (OptionItem item in list)
		{
			if (item != null && string.Equals(NormalizeReadableText(item.Text), text, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string GetTextComponentText(object textComponent)
	{
		try
		{
			if (textComponent == null)
			{
				return "";
			}
			return (textComponent.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(textComponent) as string) ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static string GetButtonText(object button)
	{
		try
		{
			if (button == null)
			{
				return "";
			}
			MethodInfo method = button.GetType().GetMethod("GetComponentInChildren", new Type[1] { typeof(Type) });
			Type type = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
			if (method != null && type != null)
			{
				string textComponentText = GetTextComponentText(method.Invoke(button, new object[1] { type }));
				if (!string.IsNullOrWhiteSpace(textComponentText))
				{
					return textComponentText;
				}
			}
			Type type2 = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
			if (method != null && type2 != null)
			{
				string textComponentText2 = GetTextComponentText(method.Invoke(button, new object[1] { type2 }));
				if (!string.IsNullOrWhiteSpace(textComponentText2))
				{
					return textComponentText2;
				}
			}
			return "";
		}
		catch
		{
			return "";
		}
	}

	private static string NormalizeReadableText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		return text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
			.Trim();
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
			FieldInfo field = _storylineUIManagerType.GetField("chapterSelectionPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo field2 = _storylineUIManagerType.GetField("storylineDisplayPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo field3 = _storylineUIManagerType.GetField("mainMenuToggleHide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (object item in array)
			{
				try
				{
					if (IsToggleHideGameObjectActive(field3?.GetValue(item)))
					{
						ManualLogSource log = Log;
						if (log != null)
						{
							log.LogDebug((object)"[Storyline detection] main menu active");
						}
						return false;
					}
					if (property != null && (bool)property.GetValue(item))
					{
						ManualLogSource log2 = Log;
						if (log2 != null)
						{
							log2.LogDebug((object)"[Storyline detection] display panel active");
						}
						return true;
					}
					if (IsGameObjectActiveInHierarchy(field2?.GetValue(item)))
					{
						ManualLogSource log3 = Log;
						if (log3 != null)
						{
							log3.LogDebug((object)"[Storyline detection] display object active");
						}
						return true;
					}
					if (IsGameObjectActiveInHierarchy(field?.GetValue(item)))
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
			ManualLogSource log5 = Log;
			if (log5 != null)
			{
				log5.LogDebug((object)"[Storyline detection] panels inactive");
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
			return IsGameObjectActiveInHierarchy(toggleHide.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(toggleHide));
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
			FieldInfo field5 = type.GetField("chapterNumberText", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field6 = type.GetField("chapterNameText", BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < array2.Length; i++)
			{
				object value = array2.GetValue(i);
				ChapterInfo chapterInfo = new ChapterInfo();
				chapterInfo.Index = i;
				chapterInfo.ChapterNumber = i + 1;
				chapterInfo.DisplayNumber = GetChapterDisplayNumber(value, field5, chapterInfo.ChapterNumber);
				if (field6 != null)
				{
					chapterInfo.Name = NormalizeReadableText(GetTextComponentText(field6.GetValue(value)));
				}
				if (field != null)
				{
					string text = field.GetValue(value) as string;
					if (string.IsNullOrWhiteSpace(chapterInfo.Name) && !string.IsNullOrWhiteSpace(text))
					{
						chapterInfo.Name = text;
					}
				}
				if (string.IsNullOrWhiteSpace(chapterInfo.Name))
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
					chapterInfo.Name = GetFallbackChapterName(chapterInfo.ChapterNumber);
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
							FieldInfo field7 = type2.GetField("Item1");
							FieldInfo field8 = type2.GetField("Item2");
							if (field7 != null && field8 != null)
							{
								int num = (int)field7.GetValue(obj4);
								int num2 = (int)field8.GetValue(obj4);
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

	private static string GetChapterDisplayNumber(object chapterButton, FieldInfo chapterNumberTextField, int chapterNumber)
	{
		string text = "";
		try
		{
			if (chapterButton != null && chapterNumberTextField != null)
			{
				text = NormalizeReadableText(GetTextComponentText(chapterNumberTextField.GetValue(chapterButton)));
			}
		}
		catch
		{
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return chapterNumber switch
		{
			1 => "序章", 
			6 => "终章", 
			_ => $"第{chapterNumber - 1}章", 
		};
	}

	private static string GetFallbackChapterName(int chapterNumber)
	{
		if (chapterNumber <= 0)
		{
			return "";
		}
		return GetChapterDisplayNumber(null, null, chapterNumber);
	}

	private static string FormatStorylineChapterOptionText(ChapterInfo chapterInfo)
	{
		if (chapterInfo == null)
		{
			return "";
		}
		string text = NormalizeReadableText(chapterInfo.DisplayNumber);
		string text2 = NormalizeReadableText(chapterInfo.Name);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = $"章节 {chapterInfo.ChapterNumber}";
		}
		if (string.IsNullOrWhiteSpace(text2) || string.Equals(text, text2, StringComparison.Ordinal))
		{
			return text;
		}
		return text + "，" + text2;
	}

	private static void EnterStorylineMode()
	{
		EnterStorylineMode(allowNodeRestore: false);
	}

	private static void EnterStorylineMode(bool allowNodeRestore)
	{
		if (allowNodeRestore && TryRestoreStorylineNodeModeOnOpen())
		{
			return;
		}
		ChapterInfo[] storylineChapters = GetStorylineChapters();
		if (storylineChapters.Length == 0)
		{
			TolkHelper.Speak("没有找到章节", interrupt: true);
			return;
		}
		ClearNodeMode("Enter storyline chapter selection");
		List<OptionItem> list = new List<OptionItem>();
		foreach (ChapterInfo item in from c in storylineChapters
			orderby c.ChapterNumber, c.Index
			select c)
		{
			OptionItem optionItem = new OptionItem();
			string text = FormatStorylineChapterOptionText(item);
			if (item.IsLocked)
			{
				text += "（已锁定）";
			}
			else if (!string.IsNullOrEmpty(item.ProgressText))
			{
				text = text + "（进度 " + item.ProgressText + "）";
			}
			optionItem.Text = text;
			optionItem.ClickableComponent = item.ButtonComponent;
			optionItem.ChapterInfo = item;
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
	}

	private static bool TryRestoreStorylineNodeModeOnOpen()
	{
		if (!_restoreStorylineNodeModeOnOpen || _lastStorylineChapterNumber <= 0)
		{
			return false;
		}
		int lastStorylineChapterNumber = _lastStorylineChapterNumber;
		_restoreStorylineNodeModeOnOpen = false;
		try
		{
			if (!TryEnterStorylineChapterDirect(lastStorylineChapterNumber))
			{
				Log.LogInfo((object)$"[故事线] 无法恢复到上次章节 {lastStorylineChapterNumber}，回退到章节选择");
				return false;
			}
			_inStorylineMode = true;
			_inOptionsMode = false;
			_options = new OptionItem[0];
			_currentOptionIndex = 0;
			_isHorizontalLayout = false;
			EnterNodeModeAfterChapterClick(lastStorylineChapterNumber);
			Log.LogInfo((object)$"[故事线] 从剧情打开故事线，恢复到上次章节节点列表: {lastStorylineChapterNumber}");
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[故事线] 恢复上次章节节点列表失败，回退到章节选择: " + ex.Message));
			return false;
		}
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
			int currentStorylineChapterFilter = GetCurrentStorylineChapterFilter();
			if (currentStorylineChapterFilter > 0)
			{
				Log.LogInfo((object)$"[故事线] 当前章节筛选: {currentStorylineChapterFilter}");
			}
			List<OptionItem> list = new List<OptionItem>();
			HashSet<string> hashSet = new HashSet<string>();
			Dictionary<int, int> dictionary = new Dictionary<int, int>();
			Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
			int num = 0;
			int num2 = 0;
			for (int i = 0; i < array.Length; i++)
			{
				object value = array.GetValue(i);
				if (!IsComponentActiveInHierarchy(value))
				{
					Log.LogDebug((object)$"[故事线过滤] 跳过隐藏节点组件 index={i}");
					continue;
				}
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
						FieldInfo field5 = type.GetField("layoutLayer", BindingFlags.Instance | BindingFlags.Public);
						FieldInfo field6 = type.GetField("layoutOrder", BindingFlags.Instance | BindingFlags.Public);
						string text = ((field2 != null) ? ((string)field2.GetValue(value2)) : $"节点{i + 1}");
						string text2 = ((field3 != null) ? ((string)field3.GetValue(value2)) : "");
						int num3 = ((field4 != null) ? ((int)field4.GetValue(value2)) : 0);
						if (!dictionary.ContainsKey(num3))
						{
							dictionary[num3] = 0;
						}
						dictionary[num3]++;
						if (currentStorylineChapterFilter > 0 && num3 > 0 && num3 != currentStorylineChapterFilter)
						{
							num++;
							Log.LogInfo((object)$"[故事线过滤] 跳过其他章节节点: {text}, chapter={num3}, current={currentStorylineChapterFilter}");
							continue;
						}
						string text3 = (string.IsNullOrWhiteSpace(text) ? $"component_{i}" : text.Trim());
						if (!hashSet.Add(text3))
						{
							num2++;
							Log.LogInfo((object)("[故事线过滤] 跳过重复节点: " + text3));
							continue;
						}
						if (!dictionary2.ContainsKey(num3))
						{
							dictionary2[num3] = 0;
						}
						dictionary2[num3]++;
						int num4 = ((field5 != null) ? ((int)field5.GetValue(value2)) : 0);
						int num5 = ((field6 != null) ? ((int)field6.GetValue(value2)) : 0);
						optionItem.NodeId = text;
						optionItem.Index = BuildStorylineNodeSortKey(num3, num4, num5, i);
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
										PropertyInfo propertyInfo = type3.GetProperty("localPosition") ?? type3.GetProperty("position");
										if (propertyInfo != null)
										{
											object value4 = propertyInfo.GetValue(value3);
											if (value4 != null)
											{
												Type type4 = value4.GetType();
												FieldInfo field7 = type4.GetField("y");
												FieldInfo field8 = type4.GetField("x");
												if (field7 != null && field8 != null)
												{
													optionItem.ScreenY = (float)field7.GetValue(value4);
													optionItem.ScreenX = (float)field8.GetValue(value4);
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
						Log.LogInfo((object)$"[故事线排序] 节点={text}, chapter={num3}, layer={num4}, order={num5}, key={optionItem.Index}, pos=({optionItem.ScreenX:F1},{optionItem.ScreenY:F1}), hasPos={optionItem.HasScreenPosition}");
					}
				}
				if (string.IsNullOrEmpty(optionItem.Text))
				{
					optionItem.Text = $"节点 {i + 1}";
				}
				if (!string.IsNullOrWhiteSpace(optionItem.Text))
				{
					list.Add(optionItem);
				}
			}
			LogStorylineNodeChapterSummary("原始", dictionary);
			LogStorylineNodeChapterSummary("过滤后", dictionary2);
			Log.LogInfo((object)$"[故事线过滤] 混入其他章节 {num} 个，重复节点 {num2} 个");
			Log.LogInfo((object)$"[故事线过滤] 过滤后剩余 {list.Count} 个节点");
			Dictionary<string, int> officialNodeOrder = GetStorylinePrecomputedNodeOrder(currentStorylineChapterFilter);
			Log.LogInfo((object)$"[故事线排序] 官方预计算顺序: {officialNodeOrder.Count} 个");
			bool hasRenderPosition = list.Any((OptionItem o) => o.HasScreenPosition);
			list.Sort(delegate(OptionItem a, OptionItem b)
			{
				int value5 = 0;
				int value6 = 0;
				bool flag = !string.IsNullOrWhiteSpace(a.NodeId) && officialNodeOrder.TryGetValue(a.NodeId.Trim(), out value5);
				bool flag2 = !string.IsNullOrWhiteSpace(b.NodeId) && officialNodeOrder.TryGetValue(b.NodeId.Trim(), out value6);
				if (flag != flag2)
				{
					if (!flag)
					{
						return 1;
					}
					return -1;
				}
				if (flag && flag2)
				{
					int num6 = value5.CompareTo(value6);
					if (num6 != 0)
					{
						return num6;
					}
				}
				int num7 = CompareStorylineNodeId(a.NodeId, b.NodeId);
				if (num7 != 0)
				{
					return num7;
				}
				if (hasRenderPosition)
				{
					if (a.HasScreenPosition != b.HasScreenPosition)
					{
						if (!a.HasScreenPosition)
						{
							return 1;
						}
						return -1;
					}
					int num8 = GetStorylineColumnSortKey(a.ScreenX).CompareTo(GetStorylineColumnSortKey(b.ScreenX));
					if (num8 != 0)
					{
						return num8;
					}
					int num9 = b.ScreenY.CompareTo(a.ScreenY);
					if (num9 != 0)
					{
						return num9;
					}
				}
				int num10 = a.Index.CompareTo(b.Index);
				return (num10 != 0) ? num10 : string.Compare(a.Text, b.Text, StringComparison.Ordinal);
			});
			LogStorylineFinalOrder(list, officialNodeOrder);
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

	private static int GetCurrentStorylineChapterFilter()
	{
		try
		{
			ResolveStorylineTypes();
			if (_progressTreeGraphControllerType == null)
			{
				return 0;
			}
			object activeObject = GetActiveObject(_progressTreeGraphControllerType);
			if (activeObject == null)
			{
				return 0;
			}
			MethodInfo method = _progressTreeGraphControllerType.GetMethod("GetCurrentChapterFilter", BindingFlags.Instance | BindingFlags.Public);
			if (method != null && method.Invoke(activeObject, null) is int result)
			{
				return result;
			}
			FieldInfo field = _progressTreeGraphControllerType.GetField("currentChapterFilter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.GetValue(activeObject) is int result2)
			{
				return result2;
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[故事线] 获取当前章节筛选失败: " + ex.Message));
		}
		return 0;
	}

	private static Dictionary<string, int> GetStorylinePrecomputedNodeOrder(int chapterNumber)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
		try
		{
			ResolveStorylineTypes();
			if (_progressTreeGraphControllerType == null)
			{
				return dictionary;
			}
			object activeObject = GetActiveObject(_progressTreeGraphControllerType);
			if (activeObject == null)
			{
				return dictionary;
			}
			object obj = _progressTreeGraphControllerType.GetField("precomputedData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(activeObject);
			if (obj == null)
			{
				Log.LogInfo((object)"[故事线排序] 未取得 precomputedData，使用节点编号排序");
				return dictionary;
			}
			MethodInfo method = obj.GetType().GetMethod("GetChapterNodeIds", BindingFlags.Instance | BindingFlags.Public);
			object nodeIdsObject = null;
			if (method != null && chapterNumber > 0)
			{
				nodeIdsObject = method.Invoke(obj, new object[1] { chapterNumber });
			}
			if (AddStorylineOrderEntries(dictionary, nodeIdsObject, 0) > 0)
			{
				return dictionary;
			}
			object nodePositionsObject = obj.GetType().GetProperty("NodePositions", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			AddStorylineNodePositionOrderEntries(dictionary, nodePositionsObject, chapterNumber);
		}
		catch (Exception ex)
		{
			Log.LogInfo((object)("[故事线排序] 读取官方预计算顺序失败，使用节点编号排序: " + ex.Message));
		}
		return dictionary;
	}

	private static int AddStorylineOrderEntries(Dictionary<string, int> order, object nodeIdsObject, int startIndex)
	{
		if (order == null || nodeIdsObject == null)
		{
			return 0;
		}
		int num = 0;
		foreach (object item in (IEnumerable)nodeIdsObject)
		{
			string text = item as string;
			if (!string.IsNullOrWhiteSpace(text))
			{
				text = text.Trim();
				if (!order.ContainsKey(text))
				{
					order[text] = startIndex + num;
					num++;
				}
			}
		}
		return num;
	}

	private static void AddStorylineNodePositionOrderEntries(Dictionary<string, int> order, object nodePositionsObject, int chapterNumber)
	{
		if (order == null || nodePositionsObject == null)
		{
			return;
		}
		int num = order.Count;
		foreach (object item in (IEnumerable)nodePositionsObject)
		{
			if (item == null)
			{
				continue;
			}
			Type type = item.GetType();
			FieldInfo field = type.GetField("chapterNumber", BindingFlags.Instance | BindingFlags.Public);
			if (chapterNumber > 0 && field != null && field.GetValue(item) is int num2 && num2 != chapterNumber)
			{
				continue;
			}
			string text = type.GetField("nodeId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item) as string;
			if (!string.IsNullOrWhiteSpace(text))
			{
				text = text.Trim();
				if (!order.ContainsKey(text))
				{
					order[text] = num;
					num++;
				}
			}
		}
	}

	private static void LogStorylineFinalOrder(List<OptionItem> nodes, Dictionary<string, int> officialNodeOrder)
	{
		try
		{
			if (nodes == null || nodes.Count == 0)
			{
				return;
			}
			int num = Math.Min(nodes.Count, 160);
			for (int i = 0; i < num; i++)
			{
				OptionItem optionItem = nodes[i];
				int value = -1;
				if (!string.IsNullOrWhiteSpace(optionItem.NodeId))
				{
					officialNodeOrder?.TryGetValue(optionItem.NodeId.Trim(), out value);
				}
				Log.LogInfo((object)$"[故事线最终排序] {i + 1}/{nodes.Count}: node={optionItem.NodeId}, official={value}, key={optionItem.Index}, pos=({optionItem.ScreenX:F1},{optionItem.ScreenY:F1}), text={optionItem.Text}");
			}
			if (nodes.Count > num)
			{
				Log.LogInfo((object)$"[故事线最终排序] 仅记录前 {num} 个，共 {nodes.Count} 个");
			}
		}
		catch
		{
		}
	}

	private static void LogStorylineNodeChapterSummary(string label, Dictionary<int, int> counts)
	{
		try
		{
			if (counts == null || counts.Count == 0)
			{
				Log.LogInfo((object)("[故事线统计] " + label + ": 无节点"));
				return;
			}
			string text = string.Join(", ", from p in counts
				orderby p.Key
				select $"第{p.Key}章={p.Value}");
			Log.LogInfo((object)("[故事线统计] " + label + ": " + text));
		}
		catch
		{
		}
	}

	private static bool IsComponentActiveInHierarchy(object component)
	{
		if (component == null)
		{
			return false;
		}
		try
		{
			object obj = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
			if (obj != null)
			{
				return IsGameObjectActiveInHierarchy(obj);
			}
		}
		catch
		{
		}
		return true;
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

	private static int CompareStorylineNodeId(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
		{
			return 0;
		}
		if (string.IsNullOrWhiteSpace(left))
		{
			return 1;
		}
		if (string.IsNullOrWhiteSpace(right))
		{
			return -1;
		}
		List<int> list = ExtractStorylineNodeNumbers(left);
		List<int> list2 = ExtractStorylineNodeNumbers(right);
		int num = Math.Max(list.Count, list2.Count);
		for (int i = 0; i < num; i++)
		{
			int num2 = ((i < list.Count) ? list[i] : (-1));
			int value = ((i < list2.Count) ? list2[i] : (-1));
			int num3 = num2.CompareTo(value);
			if (num3 != 0)
			{
				return num3;
			}
		}
		return string.Compare(left, right, StringComparison.Ordinal);
	}

	private static List<int> ExtractStorylineNodeNumbers(string nodeId)
	{
		List<int> list = new List<int>();
		if (string.IsNullOrWhiteSpace(nodeId))
		{
			return list;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char c in nodeId)
		{
			if (char.IsDigit(c))
			{
				stringBuilder.Append(c);
			}
			else if (stringBuilder.Length > 0)
			{
				if (int.TryParse(stringBuilder.ToString(), out var result))
				{
					list.Add(result);
				}
				stringBuilder.Length = 0;
			}
		}
		if (stringBuilder.Length > 0 && int.TryParse(stringBuilder.ToString(), out var result2))
		{
			list.Add(result2);
		}
		return list;
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
		SpeakCurrentNode();
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)$"[故事线] 进入节点浏览模式，共 {storylineNodes.Length} 个节点");
		}
	}

	private static void EnterNodeModeAfterChapterClick(int expectedChapterNumber)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			for (int i = 0; i < 8; i++)
			{
				Thread.Sleep(125);
				try
				{
					MarkNeedDetect();
					int currentStorylineChapterFilter = GetCurrentStorylineChapterFilter();
					if (expectedChapterNumber > 0 && currentStorylineChapterFilter != expectedChapterNumber)
					{
						ManualLogSource log = Log;
						if (log != null)
						{
							log.LogInfo((object)$"[故事线] 等待章节筛选同步: current={currentStorylineChapterFilter}, expected={expectedChapterNumber}, try={i + 1}/8");
						}
					}
					else
					{
						OptionItem[] storylineNodes = GetStorylineNodes();
						if (storylineNodes.Length != 0)
						{
							_inNodeMode = true;
							_storylineNodes = storylineNodes;
							_currentNodeIndex = 0;
							_currentUIState = UIState.Storyline;
							SpeakCurrentNode();
							ManualLogSource log2 = Log;
							if (log2 != null)
							{
								log2.LogInfo((object)$"[故事线] 点击章节后自动进入节点浏览模式，共 {storylineNodes.Length} 个节点，章节={currentStorylineChapterFilter}");
							}
							return;
						}
					}
				}
				catch (Exception ex)
				{
					ManualLogSource log3 = Log;
					if (log3 != null)
					{
						log3.LogDebug((object)("[故事线] 点击章节后自动进入节点浏览失败: " + ex.Message));
					}
				}
			}
			ManualLogSource log4 = Log;
			if (log4 != null)
			{
				log4.LogWarning((object)$"[故事线] 点击章节后没有等到目标章节节点，保持章节选择模式，expected={expectedChapterNumber}, current={GetCurrentStorylineChapterFilter()}");
			}
			TolkHelper.Speak("章节还没有加载完成，请稍后再试", interrupt: true);
		});
	}

	private static bool TryEnterStorylineChapterDirect(int chapterNumber)
	{
		if (chapterNumber <= 0)
		{
			return false;
		}
		try
		{
			ResolveStorylineTypes();
			object activeObject = GetActiveObject(_chapterStorylineControllerType);
			MethodInfo methodInfo = _chapterStorylineControllerType?.GetMethod("ShowChapterStoryline", BindingFlags.Instance | BindingFlags.Public);
			if (activeObject != null && methodInfo != null)
			{
				methodInfo.Invoke(activeObject, new object[1] { chapterNumber });
				Log.LogInfo((object)$"[故事线] 直接调用 ShowChapterStoryline({chapterNumber})");
			}
			else
			{
				object activeObject2 = GetActiveObject(_progressTreeGraphControllerType);
				if (activeObject2 == null)
				{
					return false;
				}
				MethodInfo? method = _progressTreeGraphControllerType.GetMethod("SetChapterFilter", BindingFlags.Instance | BindingFlags.Public);
				MethodInfo method2 = _progressTreeGraphControllerType.GetMethod("Redraw", BindingFlags.Instance | BindingFlags.Public);
				method?.Invoke(activeObject2, new object[1] { chapterNumber });
				method2?.Invoke(activeObject2, null);
				Log.LogInfo((object)$"[故事线] 直接设置章节筛选并重绘: {chapterNumber}");
			}
			object activeObject3 = GetActiveObject(_storylineUIManagerType);
			(_storylineUIManagerType?.GetMethod("SyncChapterContextAfterChapterChange", BindingFlags.Instance | BindingFlags.Public))?.Invoke(activeObject3, new object[1] { chapterNumber });
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[故事线] 直接进入章节失败，回退到按钮事件: " + ex.Message));
			return false;
		}
	}

	private static void ReturnToChapterSelectionFromNodeMode()
	{
		_restoreStorylineNodeModeOnOpen = false;
		ClearNodeMode("Return to chapter selection from node mode");
		EnterStorylineMode();
		TolkHelper.Speak("已返回章节选择", interrupt: true);
		MarkNeedDetect();
	}

	private static void CloseStorylineFromChapterSelection()
	{
		_restoreStorylineNodeModeOnOpen = false;
		ClearNodeMode("Close storyline from chapter selection");
		_inOptionsMode = false;
		_options = new OptionItem[0];
		_currentOptionIndex = 0;
		_inStorylineMode = false;
		HideStorylinePageOnly();
		_currentUIState = UIState.Unknown;
		_lastDetectedSignature = "";
		TolkHelper.Speak("已关闭故事线", interrupt: true);
		MarkNeedDetect();
		LogInputState("Close storyline from chapter selection");
	}

	private static void SpeakCurrentNode()
	{
		if (_storylineNodes != null && _storylineNodes.Length != 0 && _currentNodeIndex >= 0 && _currentNodeIndex < _storylineNodes.Length)
		{
			OptionItem obj = _storylineNodes[_currentNodeIndex];
			PlayGameSound("Highlight");
			TolkHelper.Speak(obj.Text, interrupt: true);
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
			}
			else
			{
				if (TryNavigateStorylineNodeDirect(optionItem))
				{
					return;
				}
				MethodInfo method = _progressTreeNodeComponentType.GetMethod("Skip2CurrentNode", BindingFlags.Instance | BindingFlags.Public);
				if (method != null)
				{
					PlayGameSound("Click");
					method.Invoke(clickableComponent, null);
					TolkHelper.Speak("跳转到 " + optionItem.Text, interrupt: true);
					AfterStorylineNodeJumpSuccess(null, "Storyline component Skip2CurrentNode");
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
					AfterStorylineNodeJumpSuccess(null, "Storyline component Confirm");
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

	private static bool TryNavigateStorylineNodeDirect(OptionItem optionItem)
	{
		if (optionItem == null || optionItem.ClickableComponent == null || _gameControllerType == null)
		{
			return false;
		}
		string storylineNodeId = GetStorylineNodeId(optionItem.ClickableComponent);
		if (string.IsNullOrWhiteSpace(storylineNodeId))
		{
			return false;
		}
		try
		{
			object activeObject = GetActiveObject(_gameControllerType);
			if (activeObject == null)
			{
				Log.LogWarning((object)"[故事线] 未找到 GameController，无法直接判断跳转结果");
				return false;
			}
			MethodInfo method = _gameControllerType.GetMethod("NavigateToNodeFromStoryline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Log.LogWarning((object)"[故事线] 未找到 NavigateToNodeFromStoryline，回退到节点组件跳转");
				return false;
			}
			PlayGameSound("Click");
			object obj = method.Invoke(activeObject, new object[1] { storylineNodeId });
			if (obj is bool)
			{
				if ((bool)obj)
				{
					TolkHelper.Speak("跳转到 " + optionItem.Text, interrupt: true);
					Log.LogInfo((object)("[故事线] 直接跳转成功: " + optionItem.Text + ", nodeId=" + storylineNodeId));
					AfterStorylineNodeJumpSuccess(activeObject, "Storyline direct jump success");
				}
				else
				{
					Log.LogWarning((object)("[故事线] 直接跳转失败: " + optionItem.Text + ", nodeId=" + storylineNodeId));
					ShowStorylineJumpFailureOption(optionItem.Text, storylineNodeId);
				}
				return true;
			}
			Log.LogInfo((object)("[故事线] 直接跳转已调用，返回值不是 bool: " + (obj?.GetType().Name ?? "null")));
			TolkHelper.Speak("跳转到 " + optionItem.Text, interrupt: true);
			AfterStorylineNodeJumpSuccess(activeObject, "Storyline direct jump invoked");
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[故事线] 直接跳转异常，回退到节点组件跳转: " + ex.GetType().Name + " - " + ex.Message));
			return false;
		}
	}

	private static string GetStorylineNodeId(object progressTreeNodeComponent)
	{
		try
		{
			if (progressTreeNodeComponent == null)
			{
				return "";
			}
			object obj = progressTreeNodeComponent.GetType().GetField("node", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(progressTreeNodeComponent);
			if (obj == null)
			{
				return "";
			}
			return (obj.GetType().GetField("nodeId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as string) ?? "";
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[故事线] 获取节点 ID 失败: " + ex.Message));
			return "";
		}
	}

	private static void AfterStorylineNodeJumpSuccess(object gameController, string reason)
	{
		RememberStorylineChapterContext(reason);
		ClearNodeMode(reason);
		_inOptionsMode = false;
		_options = new OptionItem[0];
		_currentOptionIndex = 0;
		_optionsMissCount = 0;
		_storylineMissCount = 0;
		_currentUIState = UIState.Unknown;
		_lastDetectedSignature = "";
		HideStorylinePageAfterNodeJump(gameController);
		MarkNeedDetect();
		LogInputState("After storyline node jump success: " + reason);
	}

	private static void RememberStorylineChapterContext(string reason)
	{
		int num = GetCurrentStorylineChapterFilter();
		if (num <= 0 && _lastStorylineChapterNumber > 0)
		{
			num = _lastStorylineChapterNumber;
		}
		if (num <= 0)
		{
			Log.LogInfo((object)("[故事线] 未能记录章节上下文: " + reason));
			return;
		}
		_lastStorylineChapterNumber = num;
		_restoreStorylineNodeModeOnOpen = true;
		Log.LogInfo((object)$"[故事线] 已记录剧情章节上下文: chapter={_lastStorylineChapterNumber}, reason={reason}");
	}

	private static void HideStorylinePageOnly()
	{
		try
		{
			object activeObject = GetActiveObject(Type.GetType("MainMenuManager, Assembly-CSharp"));
			object activeObject2 = GetActiveObject(_gameControllerType);
			object activeObject3 = GetActiveObject(_storylineUIManagerType);
			object obj = GetFieldValue(activeObject, "storyLinePageToggle") ?? GetFieldValue(activeObject2, "storyLinePageToggle");
			if (InvokeAnyNoArg(activeObject3, "BackToMainMenu", "TestBackToMainMenu"))
			{
				HideStorylineToggleIfStillVisible(obj);
				Log.LogInfo((object)"[故事线] 已调用 StorylineUIManager 返回主菜单流程");
				return;
			}
			if (InvokeAnyNoArg(obj, "PerformHide", "Hide", "Close", "ClosePanel"))
			{
				Log.LogInfo((object)"[故事线] 已调用 storyLinePageToggle 关闭方法");
				return;
			}
			if (InvokeAnyNoArg(activeObject3, "Hide", "Close", "CloseStoryline", "HideStoryline", "OnClose", "OnBack", "ReturnToMain"))
			{
				Log.LogInfo((object)"[故事线] 已调用 StorylineUIManager 关闭方法");
				return;
			}
			if (InvokeAnyNoArg(activeObject, "CloseStoryLine", "CloseStoryline", "HideStoryLine", "HideStoryline", "OnStoryLineBack", "OnStorylineBack", "Back"))
			{
				Log.LogInfo((object)"[故事线] 已调用 MainMenuManager 故事线关闭方法");
				return;
			}
			object componentGameObject = GetComponentGameObject(obj);
			if (componentGameObject != null && SetGameObjectActive(componentGameObject, active: false))
			{
				Log.LogWarning((object)"[故事线] 未找到原生关闭方法，兜底隐藏 storyLinePageToggle GameObject");
				return;
			}
			object componentGameObject2 = GetComponentGameObject(activeObject3);
			if (componentGameObject2 != null && SetGameObjectActive(componentGameObject2, active: false))
			{
				Log.LogWarning((object)"[故事线] 未找到原生关闭方法，兜底隐藏 StorylineUIManager GameObject");
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[故事线] 关闭故事线页面失败: " + ex.Message));
		}
	}

	private static void HideStorylineToggleIfStillVisible(object storyLinePageToggle)
	{
		if (storyLinePageToggle == null || !IsToggleHideGameObjectActive(storyLinePageToggle))
		{
			return;
		}
		if (InvokeAnyNoArg(storyLinePageToggle, "PerformHide"))
		{
			Log.LogInfo((object)"[故事线] 返回主菜单后 storyLinePageToggle 仍显示，已调用 PerformHide");
			return;
		}
		object componentGameObject = GetComponentGameObject(storyLinePageToggle);
		if (componentGameObject != null && SetGameObjectActive(componentGameObject, active: false))
		{
			Log.LogWarning((object)"[故事线] 返回主菜单后 storyLinePageToggle 仍显示，已兜底隐藏 GameObject");
		}
	}

	private static bool InvokeAnyNoArg(object obj, params string[] methodNames)
	{
		if (obj == null || methodNames == null)
		{
			return false;
		}
		foreach (string methodName in methodNames)
		{
			if (InvokeNoArg(obj, methodName))
			{
				return true;
			}
		}
		return false;
	}

	private static void HideStorylinePageAfterNodeJump(object gameController)
	{
		try
		{
			object fieldValue = GetFieldValue(gameController, "storyLinePageToggle");
			if (fieldValue == null)
			{
				fieldValue = GetFieldValue(GetActiveObject(_gameControllerType), "storyLinePageToggle");
			}
			object componentGameObject = GetComponentGameObject(fieldValue);
			if (componentGameObject != null && SetGameObjectActive(componentGameObject, active: false))
			{
				Log.LogInfo((object)"[故事线] 跳转节点后已直接隐藏 storyLinePageToggle GameObject");
				StopMainMenuBackgroundMusicAfterStoryJump();
				return;
			}
			object obj = GetFieldValue(gameController, "storylineUIManager") ?? GetActiveObject(_storylineUIManagerType);
			if (obj != null)
			{
				object componentGameObject2 = GetComponentGameObject(obj);
				if (componentGameObject2 != null && SetGameObjectActive(componentGameObject2, active: false))
				{
					Log.LogInfo((object)"[故事线] 跳转节点后已隐藏 StorylineUIManager GameObject");
					StopMainMenuBackgroundMusicAfterStoryJump();
					return;
				}
			}
			Log.LogWarning((object)"[故事线] 跳转节点后未找到可隐藏的故事线页面对象");
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[故事线] 隐藏故事线页面失败: " + ex.Message));
		}
	}

	private static void StopMainMenuBackgroundMusicAfterStoryJump()
	{
		try
		{
			object activeObject = GetActiveObject(Type.GetType("BackgroundMusicManager, Assembly-CSharp"));
			if (activeObject != null && InvokeNoArg(activeObject, "PauseBackgroundMusic"))
			{
				Log.LogInfo((object)"[音频] 故事线跳转后已暂停主页 BGM");
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[音频] 暂停主页 BGM 失败: " + ex.Message));
		}
	}

	private static object GetComponentGameObject(object component)
	{
		if (component == null)
		{
			return null;
		}
		try
		{
			return component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
		}
		catch
		{
			return null;
		}
	}

	private static bool SetGameObjectActive(object gameObject, bool active)
	{
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			MethodInfo method = gameObject.GetType().GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			method.Invoke(gameObject, new object[1] { active });
			return true;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("SetActive 调用失败: " + ex.Message));
			return false;
		}
	}

	private static void ShowStorylineJumpFailureOption(string nodeText, string nodeId)
	{
		string text = (string.IsNullOrWhiteSpace(nodeText) ? "这个节点" : nodeText.Trim());
		string text2 = "故事线跳转失败：" + text + " 当前进度不足，不能从现在的剧情路径连接到这个节点。请先继续剧情或到达前置结尾后再尝试。";
		ClearNodeMode("Storyline jump failed");
		_currentUIState = UIState.Options;
		SetOptions(new OptionItem[1]
		{
			new OptionItem
			{
				Text = text2,
				Index = -1
			}
		});
		Log.LogWarning((object)("[故事线] 已显示跳转失败提示: nodeId=" + nodeId + ", text=" + text));
	}

	private static bool IsStorylineFailureOption(OptionItem optionItem)
	{
		if (optionItem != null && !string.IsNullOrWhiteSpace(optionItem.Text))
		{
			if (!optionItem.Text.StartsWith("故事线跳转失败：", StringComparison.Ordinal))
			{
				return optionItem.Text.StartsWith("结尾提示：", StringComparison.Ordinal);
			}
			return true;
		}
		return false;
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
				Log.LogWarning((object)"未找到 TriggerArea 类型，探索交互点自动检测不可用");
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

	private static void ResolveNarrationTypes()
	{
		if (_narrationTypesResolved)
		{
			return;
		}
		_narrationTypesResolved = true;
		try
		{
			_narrationManagerType = Type.GetType("NarrationManager, Assembly-CSharp");
			if (_narrationManagerType != null)
			{
				Log.LogInfo((object)"找到 NarrationManager 类型");
			}
			else
			{
				Log.LogWarning((object)"未找到 NarrationManager 类型，剧情旁白强制朗读不可用");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("解析剧情旁白类型失败: " + ex.Message));
		}
	}

	private static void CheckNarrationSpeak()
	{
		try
		{
			ResolveNarrationTypes();
			if (_narrationManagerType == null)
			{
				return;
			}
			Array array = FindObjectsOfType(_narrationManagerType);
			if (array == null || array.Length == 0)
			{
				return;
			}
			foreach (object item in array)
			{
				SpeakCurrentNarration(item);
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("剧情旁白轮询失败: " + ex.Message));
			}
		}
	}

	private static void SpeakCurrentNarration(object manager)
	{
		if (manager == null)
		{
			return;
		}
		object fieldValue = GetFieldValue(manager, "isNarrationActive");
		if (fieldValue is bool && !(bool)fieldValue)
		{
			_lastNarrationSpeakText = "";
			return;
		}
		string text = GetTextFromTextComponent(GetFieldValue(manager, "specialText"));
		if (string.IsNullOrWhiteSpace(text))
		{
			string textFromTextComponent = GetTextFromTextComponent(GetFieldValue(manager, "titleText"));
			string textFromTextComponent2 = GetTextFromTextComponent(GetFieldValue(manager, "contentText"));
			text = JoinSpeechParts(textFromTextComponent, textFromTextComponent2);
		}
		text = NormalizeSpeechText(text);
		if (string.IsNullOrWhiteSpace(text))
		{
			_lastNarrationSpeakText = "";
		}
		else if (!(text == _lastNarrationSpeakText))
		{
			_lastNarrationSpeakText = text;
			_lastSpokenText = text;
			TolkHelper.Speak(text, interrupt: true);
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)("[剧情旁白] 强制朗读: " + text));
			}
			MarkNeedDetect();
		}
	}

	private static bool IsNarrationTextComponent(object textComponent)
	{
		if (textComponent == null)
		{
			return false;
		}
		try
		{
			ResolveNarrationTypes();
			if (_narrationManagerType == null)
			{
				return false;
			}
			Array array = FindObjectsOfType(_narrationManagerType);
			if (array == null || array.Length == 0)
			{
				return false;
			}
			foreach (object item in array)
			{
				if (item != null && (textComponent == GetFieldValue(item, "titleText") || textComponent == GetFieldValue(item, "contentText") || textComponent == GetFieldValue(item, "specialText")))
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("判断旁白文本组件失败: " + ex.Message));
			}
		}
		return false;
	}

	private static string GetTextFromTextComponent(object textComponent)
	{
		if (textComponent == null)
		{
			return "";
		}
		object obj = textComponent.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(textComponent);
		PropertyInfo propertyInfo = obj?.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public);
		if (propertyInfo != null && !(bool)propertyInfo.GetValue(obj))
		{
			return "";
		}
		return (textComponent.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(textComponent) as string) ?? "";
	}

	private static string JoinSpeechParts(params string[] parts)
	{
		List<string> list = new List<string>();
		for (int i = 0; i < parts.Length; i++)
		{
			string text = NormalizeSpeechText(parts[i]);
			if (!string.IsNullOrWhiteSpace(text))
			{
				list.Add(text);
			}
		}
		return string.Join("。", list.ToArray());
	}

	private static string NormalizeSpeechText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		return text.Replace("\r", " ").Replace("\n", " ").Trim();
	}

	private static void ApplyQTEPatches(Harmony harmony)
	{
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Expected O, but got Unknown
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

	private static OptionItem[] GetExploreInteractionOptions()
	{
		try
		{
			ResolveTriggerAreaTypes();
			if (_triggerAreaType == null)
			{
				return new OptionItem[0];
			}
			Array array = FindObjectsOfType(_triggerAreaType);
			if (array == null || array.Length == 0)
			{
				return new OptionItem[0];
			}
			List<OptionItem> list = new List<OptionItem>();
			foreach (object item in array)
			{
				if (IsComponentActiveInHierarchy(item) && HasInvokableOnClick(item))
				{
					OptionItem optionItem = new OptionItem();
					optionItem.Text = ((list.Count == 0) ? "继续" : ("探索交互点 " + (list.Count + 1)));
					optionItem.ClickableComponent = item;
					if (TryGetScreenPosition(GetGameObjectFromComponent(item), out var x, out var y))
					{
						optionItem.ScreenX = x;
						optionItem.ScreenY = y;
						optionItem.HasScreenPosition = true;
					}
					list.Add(optionItem);
					if (list.Count >= 20)
					{
						break;
					}
				}
			}
			if (list.Count > 0)
			{
				Log.LogInfo((object)$"[探索] 自动检测到 {list.Count} 个交互点");
			}
			return SortOptions(list.ToArray());
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[探索] 自动检测交互点失败: " + ex.Message));
			return new OptionItem[0];
		}
	}

	private static bool HasInvokableOnClick(object component)
	{
		if (component == null)
		{
			return false;
		}
		Type type = component.GetType();
		if (TryGetEventLikeMember(type, component, "onClick", out var value) && HasPublicInvoke(value))
		{
			return true;
		}
		if (!HasNoArgMethod(type, "OnClick") && !HasNoArgMethod(type, "Click") && !HasNoArgMethod(type, "OnMouseDown") && !HasNoArgMethod(type, "Trigger"))
		{
			return HasNoArgMethod(type, "OnTrigger");
		}
		return true;
	}

	private static void SpeakQTEPrompt(object qteController, bool allowRecentRepeat)
	{
		try
		{
			DateTime utcNow = DateTime.UtcNow;
			if (!allowRecentRepeat && (utcNow - _lastQTESpeakUtc).TotalMilliseconds < 900.0)
			{
				Log.LogDebug((object)"[QTE] 提示刚朗读过，跳过重复提示");
				return;
			}
			string qTEPrompt = GetQTEPrompt(qteController);
			_lastQTESpeakUtc = utcNow;
			TolkHelper.Speak(qTEPrompt, interrupt: true);
			Log.LogInfo((object)("[QTE] 朗读提示: " + qTEPrompt));
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[QTE] 朗读提示失败: " + ex.Message));
			TolkHelper.Speak("空格", interrupt: true);
		}
	}

	private static string GetQTEPrompt(object qteController)
	{
		string qTEDirectionText = GetQTEDirectionText(qteController);
		if (!string.IsNullOrWhiteSpace(qTEDirectionText))
		{
			return "空格，" + qTEDirectionText;
		}
		return "空格";
	}

	private static string GetQTEDirectionText(object qteController)
	{
		try
		{
			object obj = qteController ?? GetActiveObject(_qteControllerType);
			if (obj == null)
			{
				return "";
			}
			object fieldValue = GetFieldValue(obj, "config");
			if (fieldValue == null)
			{
				return "";
			}
			object fieldValue2 = GetFieldValue(fieldValue, "qteType");
			if (fieldValue2 == null || fieldValue2.ToString() != "SwipeGesture")
			{
				return "";
			}
			object fieldValue3 = GetFieldValue(fieldValue, "swipeDirection");
			if (fieldValue3 == null)
			{
				return "";
			}
			Type type = fieldValue3.GetType();
			FieldInfo field = type.GetField("x", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field2 = type.GetField("y", BindingFlags.Instance | BindingFlags.Public);
			float num = ((field != null) ? ((float)field.GetValue(fieldValue3)) : 0f);
			float num2 = ((field2 != null) ? ((float)field2.GetValue(fieldValue3)) : 0f);
			if (Math.Abs(num) >= Math.Abs(num2))
			{
				return (num < 0f) ? "向左闪避" : "向右闪避";
			}
			return (num2 < 0f) ? "向下闪避" : "向上闪避";
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[QTE] 获取方向提示失败: " + ex.Message));
			return "";
		}
	}

	private static void StartQTEPostfix(object __instance)
	{
		try
		{
			_lastQTEStartedUtc = DateTime.UtcNow;
			_lastQTEController = __instance;
			_suppressSpaceUntilUtc = DateTime.UtcNow.AddSeconds(3.0);
			_currentUIState = UIState.QTE;
			_lastDetectedSignature = "qte";
			SpeakQTEPrompt(__instance, allowRecentRepeat: false);
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
					Log.LogWarning((object)"【自动过 QTE】跳过失败");
					TolkHelper.Speak("QTE 自动跳过失败", interrupt: true);
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
			if (qteController == null)
			{
				return false;
			}
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
						_lastQTEStartedUtc = DateTime.MinValue;
						_lastQTEController = null;
						_suppressSpaceUntilUtc = DateTime.UtcNow.AddMilliseconds(350.0);
						_needDetect = true;
						return true;
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
			Log.LogWarning((object)"【跳过 QTE】未找到安全成功回调，取消兜底禁用/StopQTE，避免误暂停或误判失败");
			return false;
		}
		catch (Exception ex)
		{
			Log.LogError((object)("【跳过 QTE】异常: " + ex.GetType().Name + " - " + ex.Message));
			Log.LogError((object)("堆栈: " + ex.StackTrace));
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
				Log.LogWarning((object)"QTE 类型未找到，无法执行精准跳过");
				TolkHelper.Speak("QTE 精准跳过不可用", interrupt: true);
				return;
			}
			if (_lastQTEController != null && TrySkipQTE(_lastQTEController))
			{
				Log.LogInfo((object)"【一键过 QTE】已跳过最近启动的 QTE");
				TolkHelper.Speak("QTE 已跳过", interrupt: true);
				return;
			}
			Array array = FindObjectsOfType(_qteControllerType);
			if (array == null || array.Length == 0)
			{
				Log.LogInfo((object)"没有找到 QTEController 实例");
				TolkHelper.Speak("当前没有 QTE", interrupt: true);
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
			Log.LogInfo((object)"精准跳过失败，旧版乱试 QTE 逻辑已移除");
			TolkHelper.Speak("没有找到可精准跳过的 QTE", interrupt: true);
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
			foreach (Assembly assembly in array)
			{
				try
				{
					Type[] types = assembly.GetTypes();
					num += types.Length;
					Type[] array2 = types;
					foreach (Type type in array2)
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
					Type[] array2 = ex.Types;
					foreach (Type type2 in array2)
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
		foreach (string value in cODE_EXPLORE_KEYWORDS)
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
			foreach (FieldInfo fieldInfo in array)
			{
				string text2 = (fieldInfo.IsStatic ? " static" : "");
				Log.LogInfo((object)("    - " + fieldInfo.FieldType.Name + " " + fieldInfo.Name + text2));
			}
		}
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
		if (properties.Length != 0)
		{
			Log.LogInfo((object)$"  公共属性 ({properties.Length} 个):");
			PropertyInfo[] array2 = properties;
			foreach (PropertyInfo propertyInfo in array2)
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
			MethodInfo[] array3 = methods;
			foreach (MethodInfo methodInfo in array3)
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
			if (!_pluginInitialized || _hookId == IntPtr.Zero)
			{
				return CallNextHookEx(_hookId, nCode, wParam, lParam);
			}
			if (nCode >= 0 && (wParam == (IntPtr)256 || wParam == (IntPtr)260))
			{
				int num = Marshal.ReadInt32(lParam);
				int num2 = Marshal.ReadInt32(lParam, 8);
				if (!IsGameWindowActive())
				{
					return CallNextHookEx(_hookId, nCode, wParam, lParam);
				}
				if (wParam == (IntPtr)260 || ((uint)num2 & 0x20u) != 0)
				{
					ManualLogSource log = Log;
					if (log != null)
					{
						log.LogDebug((object)$"[键盘钩子] Alt/System 组合键 0x{num:X2} 放行");
					}
					return CallNextHookEx(_hookId, nCode, wParam, lParam);
				}
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogDebug((object)$"[键盘钩子] 按键: 0x{num:X2}");
				}
				if (IsModifierKeyDown() && !IsModifierKey(num) && !ShouldHandleKeyEvenWithModifier(num))
				{
					ManualLogSource log3 = Log;
					if (log3 != null)
					{
						log3.LogDebug((object)$"[键盘钩子] 组合键 0x{num:X2} 放行");
					}
					return CallNextHookEx(_hookId, nCode, wParam, lParam);
				}
				if (num == 32 && ShouldSuppressSpaceForQTE())
				{
					ManualLogSource log4 = Log;
					if (log4 != null)
					{
						log4.LogInfo((object)"[键盘钩子] QTE 期间拦截空格，避免传给游戏暂停");
					}
					if (ShouldTrySkipQTEFromSuppressedSpace())
					{
						TrySkipQTEFromSpace();
					}
					return new IntPtr(1);
				}
				return CallNextHookEx(_hookId, nCode, wParam, lParam);
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log5 = Log;
			if (log5 != null)
			{
				log5.LogError((object)("键盘钩子回调异常: " + ex.GetType().Name + " - " + ex.Message));
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
			if (HandleArchiveKey(vkCode))
			{
				LogInputState("After archive key " + vkCode);
				return;
			}
			switch (vkCode)
			{
			case 116:
			{
				ManualLogSource log8 = Log;
				if (log8 != null)
				{
					log8.LogInfo((object)"[快捷键] F5 按下 - 重复朗读");
				}
				if (!string.IsNullOrEmpty(_lastSpokenText))
				{
					TolkHelper.Speak(_lastSpokenText, interrupt: true);
				}
				else
				{
					TolkHelper.Speak("还没有朗读过文本", interrupt: true);
				}
				_suppressCurrentKey = true;
				break;
			}
			case 117:
			{
				ManualLogSource log9 = Log;
				if (log9 != null)
				{
					log9.LogInfo((object)"[快捷键] F6 按下 - 停止朗读");
				}
				TolkHelper.Stop();
				_suppressCurrentKey = true;
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
				ManualLogSource log7 = Log;
				if (log7 != null)
				{
					log7.LogInfo((object)"[快捷键] F11 按下 - 切换自动过 QTE 模式");
				}
				ToggleAutoQTE();
				_suppressCurrentKey = true;
				break;
			}
			case 114:
			{
				ManualLogSource log14 = Log;
				if (log14 != null)
				{
					log14.LogInfo((object)"[快捷键] F3 按下 - 跳转到当前节点");
				}
				if (_currentUIState == UIState.Storyline)
				{
					JumpToCurrentNode();
				}
				else
				{
					LogInputState("F3 ignored outside storyline");
				}
				_suppressCurrentKey = true;
				break;
			}
			case 8:
			{
				ManualLogSource log13 = Log;
				if (log13 != null)
				{
					log13.LogInfo((object)"[快捷键] 退格键 按下 - 快退");
				}
				if (_currentUIState == UIState.Storyline || _inNodeMode)
				{
					BackToPreviousNode();
				}
				else
				{
					LogInputState("Backspace ignored outside storyline");
				}
				_suppressCurrentKey = true;
				break;
			}
			case 27:
			{
				ManualLogSource log10 = Log;
				if (log10 != null)
				{
					log10.LogInfo((object)"[快捷键] ESC 按下");
				}
				if (_inNodeMode)
				{
					ReturnToChapterSelectionFromNodeMode();
					_suppressCurrentKey = true;
				}
				else if (_currentUIState == UIState.Storyline)
				{
					CloseStorylineFromChapterSelection();
					_suppressCurrentKey = true;
				}
				else if (_inSettingsMode && _settings.Length != 0 && ActivateReturnSetting())
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
				if (IsQTEInputActive())
				{
					ManualLogSource log5 = Log;
					if (log5 != null)
					{
						log5.LogInfo((object)"[快捷键] 空格 按下");
					}
					ManualLogSource log6 = Log;
					if (log6 != null)
					{
						log6.LogInfo((object)"[QTE] 检测到空格，跳过当前 QTE");
					}
					SkipCurrentQTE();
					_suppressCurrentKey = true;
				}
				break;
			case 68:
			{
				ManualLogSource log11 = Log;
				if (log11 != null)
				{
					log11.LogInfo((object)"[快捷键] D 键按下 - 切换字幕朗读开关");
				}
				ToggleSubtitleSpeak();
				_suppressCurrentKey = true;
				break;
			}
			case 13:
			{
				ManualLogSource log4 = Log;
				if (log4 != null)
				{
					log4.LogInfo((object)"[快捷键] 回车 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					JumpToSelectedNode();
					_suppressCurrentKey = true;
				}
				else if (_inSettingsMode && _settings.Length != 0)
				{
					ActivateCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					HandleEnter();
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
				ManualLogSource log12 = Log;
				if (log12 != null)
				{
					log12.LogInfo((object)"[快捷键] 上光标 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					_currentNodeIndex--;
					if (_currentNodeIndex < 0)
					{
						_currentNodeIndex = _storylineNodes.Length - 1;
					}
					SpeakCurrentNode();
					_suppressCurrentKey = true;
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
					if (_options.Length < 2)
					{
						LogInputState("Up released for single option");
						break;
					}
					_currentOptionIndex--;
					if (_currentOptionIndex < 0)
					{
						_currentOptionIndex = _options.Length - 1;
					}
					SpeakCurrentOption();
					_suppressCurrentKey = true;
				}
				else
				{
					LogInputState("Up ignored");
				}
				break;
			}
			case 40:
			{
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogInfo((object)"[快捷键] 下光标 按下");
				}
				if (_inNodeMode && _storylineNodes.Length != 0)
				{
					_currentNodeIndex++;
					if (_currentNodeIndex >= _storylineNodes.Length)
					{
						_currentNodeIndex = 0;
					}
					SpeakCurrentNode();
					_suppressCurrentKey = true;
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
					if (_options.Length < 2)
					{
						LogInputState("Down released for single option");
						break;
					}
					_currentOptionIndex++;
					if (_currentOptionIndex >= _options.Length)
					{
						_currentOptionIndex = 0;
					}
					SpeakCurrentOption();
					_suppressCurrentKey = true;
				}
				else
				{
					LogInputState("Down ignored");
				}
				break;
			}
			case 37:
			{
				ManualLogSource log3 = Log;
				if (log3 != null)
				{
					log3.LogInfo((object)"[快捷键] 左光标 按下");
				}
				if (_inSettingsMode && _settings.Length != 0)
				{
					AdjustSettingValue(_settings[_currentSettingIndex], -1);
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					if (_options.Length < 2)
					{
						LogInputState("Left released for single option");
						break;
					}
					_currentOptionIndex--;
					if (_currentOptionIndex < 0)
					{
						_currentOptionIndex = _options.Length - 1;
					}
					SpeakCurrentOption();
					_suppressCurrentKey = true;
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
					AdjustSettingValue(_settings[_currentSettingIndex], 1);
					SpeakCurrentSetting();
					_suppressCurrentKey = true;
				}
				else if (_inOptionsMode && _options.Length != 0)
				{
					if (_options.Length < 2)
					{
						LogInputState("Right released for single option");
						break;
					}
					_currentOptionIndex++;
					if (_currentOptionIndex >= _options.Length)
					{
						_currentOptionIndex = 0;
					}
					SpeakCurrentOption();
					_suppressCurrentKey = true;
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
			ManualLogSource log15 = Log;
			if (log15 != null)
			{
				log15.LogError((object)("处理按键异常: " + ex.GetType().Name + " - " + ex.Message));
			}
		}
	}

	private static bool IsModifierKeyDown()
	{
		if (!IsKeyDown(16) && !IsKeyDown(160) && !IsKeyDown(161) && !IsKeyDown(17) && !IsKeyDown(162) && !IsKeyDown(163) && !IsKeyDown(18) && !IsKeyDown(164) && !IsKeyDown(165) && !IsKeyDown(91))
		{
			return IsKeyDown(92);
		}
		return true;
	}

	private static bool IsModifierKey(int vkCode)
	{
		if (vkCode != 16 && vkCode != 160 && vkCode != 161 && vkCode != 17 && vkCode != 162 && vkCode != 163 && vkCode != 18 && vkCode != 164 && vkCode != 165 && vkCode != 91)
		{
			return vkCode == 92;
		}
		return true;
	}

	private static bool ShouldHandleKeyEvenWithModifier(int vkCode)
	{
		return false;
	}

	private static bool IsKeyDown(int vkCode)
	{
		return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
	}

	private static void LogInputState(string context)
	{
		ManualLogSource log = Log;
		if (log != null)
		{
			string text = ((_options == null) ? "null" : _options.Length.ToString());
			string text2 = ((_settings == null) ? "null" : _settings.Length.ToString());
			string text3 = ((_storylineNodes == null) ? "null" : _storylineNodes.Length.ToString());
			log.LogInfo((object)$"[诊断状态] {context}; UI={_currentUIState}; inOptions={_inOptionsMode}; options={text}; optionIndex={_currentOptionIndex}; inSettings={_inSettingsMode}; settings={text2}; settingIndex={_currentSettingIndex}; inNode={_inNodeMode}; nodes={text3}; nodeIndex={_currentNodeIndex}; signature={_lastDetectedSignature}");
		}
	}

	private static bool IsDigitShortcut(int vkCode)
	{
		if (vkCode < 49 || vkCode > 57)
		{
			if (vkCode >= 97)
			{
				return vkCode <= 105;
			}
			return false;
		}
		return true;
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
		if (IsModifierKeyDown())
		{
			return false;
		}
		int num = DigitShortcutIndex(vkCode);
		if (num < 0)
		{
			return false;
		}
		if (!_inOptionsMode || _options == null || num >= _options.Length)
		{
			return false;
		}
		if (!IsGameControllerStoryOption(_options[num]))
		{
			return false;
		}
		_currentOptionIndex = num;
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)$"[快捷键] 数字 {num + 1} 选择剧情选项");
		}
		HandleEnter();
		_suppressCurrentKey = true;
		return true;
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
			if (IsStorylineFailureOption(optionItem))
			{
				PlayGameSound("Highlight");
				TolkHelper.Speak(optionItem.Text, interrupt: true);
				Log.LogInfo((object)"[故事线] 重读跳转失败提示，不执行点击");
			}
			else
			{
				if (TryActivateEndingPageOption(optionItem))
				{
					return;
				}
				bool flag = IsCurrentOptionsConfirmationDialog() && IsConfirmationDialogOption(optionItem);
				bool num = !string.IsNullOrEmpty(optionItem.Text) && (optionItem.Text.Contains("返回") || optionItem.Text.Contains("关闭") || optionItem.Text.Equals("Back", StringComparison.OrdinalIgnoreCase));
				bool flag2 = optionItem.ChapterInfo != null;
				PlayGameSound(num ? "Back" : "Click");
				TolkHelper.Speak("点击 " + optionItem.Text, interrupt: true);
				if (flag2)
				{
					ClearNodeMode("Click storyline chapter");
					TryEnterStorylineChapterDirect(optionItem.ChapterInfo?.ChapterNumber ?? 0);
				}
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
							ClearCurrentOptionsAfterGameSelection();
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
				if (TryActivateRevisitableContinue(optionItem))
				{
					ClearCurrentOptionsAfterTransientClick("revisitable continue direct");
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
						if (flag2)
						{
							EnterNodeModeAfterChapterClick(optionItem.ChapterInfo?.ChapterNumber ?? 0);
						}
						if (flag)
						{
							ClearCurrentOptionsAfterTransientClick("confirmation dialog click");
						}
						if (IsExploreInteractionOption(optionItem))
						{
							ClearCurrentOptionsAfterTransientClick("explore interaction click");
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
				if (flag2)
				{
					EnterNodeModeAfterChapterClick(optionItem.ChapterInfo?.ChapterNumber ?? 0);
				}
				if (flag)
				{
					ClearCurrentOptionsAfterTransientClick("confirmation dialog mouse click");
				}
				if (IsExploreInteractionOption(optionItem))
				{
					ClearCurrentOptionsAfterTransientClick("explore interaction mouse click");
				}
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

	private static bool AreCurrentOptionsFromGameController()
	{
		if (_gameControllerType == null || _options == null || _options.Length == 0)
		{
			return false;
		}
		OptionItem[] options = _options;
		for (int i = 0; i < options.Length; i++)
		{
			if (IsGameControllerStoryOption(options[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsGameControllerStoryOption(OptionItem optionItem)
	{
		if (optionItem != null && optionItem.Index >= 0 && optionItem.ClickableComponent != null && _gameControllerType != null)
		{
			return optionItem.ClickableComponent.GetType() == _gameControllerType;
		}
		return false;
	}

	private static bool IsExploreInteractionOption(OptionItem optionItem)
	{
		return IsRevisitableContinueText(optionItem?.Text);
	}

	private static bool IsRevisitableContinueText(string text)
	{
		text = text?.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			if (!text.StartsWith("探索交互点", StringComparison.Ordinal) && !text.Contains("了解完毕") && !text.Contains("盘问完毕"))
			{
				return text.Equals("继续", StringComparison.Ordinal);
			}
			return true;
		}
		return false;
	}

	private static bool TryActivateRevisitableContinue(OptionItem optionItem)
	{
		if (_gameControllerType == null)
		{
			return false;
		}
		try
		{
			object activeObject = GetActiveObject(_gameControllerType);
			if (activeObject == null)
			{
				return false;
			}
			object fieldValue = GetFieldValue(activeObject, "currentNode");
			object fieldValue2 = GetFieldValue(fieldValue, "nextNodeAfterAllOptions");
			object fieldValue3 = GetFieldValue(activeObject, "parentRevisitableNode");
			Log.LogInfo((object)("[循环选项] 当前节点=" + GetGameNodeId(fieldValue) + ", nextNodeAfterAllOptions=" + GetGameNodeId(fieldValue2) + ", parentRevisitableNode=" + GetGameNodeId(fieldValue3)));
			if (!IsRevisitableContinueOption(optionItem, fieldValue))
			{
				return false;
			}
			if (fieldValue != null && fieldValue2 != null && IsRevisitableOptionComplete(activeObject, fieldValue))
			{
				return ActivateGameNode(activeObject, fieldValue2, "[循环选项] 已从父循环节点直接进入继续节点: " + GetGameNodeId(fieldValue2));
			}
			if (TryActivateMappedRevisitableContinue(activeObject, fieldValue))
			{
				return true;
			}
			if (TryActivateKnownBrokenRevisitableContinue(activeObject, fieldValue))
			{
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[循环选项] 直接继续失败: " + ex.GetType().Name + " - " + ex.Message));
			return false;
		}
	}

	private static bool IsRevisitableContinueOption(OptionItem optionItem, object currentNode)
	{
		string text = optionItem?.Text?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		if (!IsRevisitableContinueText(text))
		{
			return false;
		}
		string gameNodeId = GetGameNodeId(currentNode);
		if (!string.IsNullOrWhiteSpace(gameNodeId))
		{
			switch (gameNodeId)
			{
			case "<null>":
				break;
			case "03035":
			case "03036":
			case "03037":
			case "03038":
			case "03039":
				return true;
			default:
				EnsureRevisitableLinkCache();
				if (_revisitableChildLinks.ContainsKey(gameNodeId) || _revisitableContinueNodeIds.Contains(gameNodeId))
				{
					return true;
				}
				if (GetFieldValue(currentNode, "nextNodeAfterAllOptions") != null)
				{
					return GetFieldValue(currentNode, "options") != null;
				}
				return false;
			}
		}
		return false;
	}

	private static bool TryActivateMappedRevisitableContinue(object gameController, object currentNode)
	{
		string gameNodeId = GetGameNodeId(currentNode);
		if (gameController == null || string.IsNullOrWhiteSpace(gameNodeId) || gameNodeId == "<null>")
		{
			return false;
		}
		EnsureRevisitableLinkCache();
		if (_revisitableContinueNodeIds.Contains(gameNodeId))
		{
			object fieldValue = GetFieldValue(currentNode, "nextNode");
			if (fieldValue != null)
			{
				return ActivateGameNode(gameController, fieldValue, "[循环选项] 已从循环继续节点跳到后续节点: " + GetGameNodeId(fieldValue));
			}
		}
		if (!_revisitableChildLinks.TryGetValue(gameNodeId, out var value))
		{
			object obj = FindRevisitableParentNodeForChild(currentNode);
			if (obj != null)
			{
				value = CreateRevisitableLink(obj, currentNode);
				if (value != null)
				{
					_revisitableChildLinks[gameNodeId] = value;
				}
			}
		}
		if (value == null || value.ParentNode == null)
		{
			return false;
		}
		MarkRevisitableOptionVisited(gameController, value.ParentNode, value.OptionIndex);
		object obj2 = value.ContinueNode ?? GetFieldValue(value.ParentNode, "nextNodeAfterAllOptions");
		if (obj2 != null)
		{
			return ActivateGameNode(gameController, obj2, "[循环选项] 已从子预览节点进入循环继续节点: " + GetGameNodeId(obj2));
		}
		return ActivateGameNode(gameController, value.ParentNode, "[循环选项] 已从子预览节点返回循环选项父节点: " + GetGameNodeId(value.ParentNode));
	}

	private static void EnsureRevisitableLinkCache()
	{
		if (_revisitableLinkCacheBuilt)
		{
			return;
		}
		try
		{
			object gameNodeRegistry = GetGameNodeRegistry();
			object? value = (gameNodeRegistry?.GetType().GetMethod("GetAllNodes", BindingFlags.Instance | BindingFlags.Public))?.Invoke(gameNodeRegistry, null);
			int num = 0;
			foreach (object item in EnumerateRevisitableRegistryNodeIds(value))
			{
				string text = item?.ToString();
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				object gameNodeFromRegistry = GetGameNodeFromRegistry(text);
				object fieldValue = GetFieldValue(gameNodeFromRegistry, "nextNodeAfterAllOptions");
				object fieldValue2 = GetFieldValue(gameNodeFromRegistry, "options");
				if (gameNodeFromRegistry == null || fieldValue == null || fieldValue2 == null)
				{
					continue;
				}
				int num2 = 0;
				foreach (object item2 in EnumerateObjects(fieldValue2))
				{
					string gameNodeId = GetGameNodeId(GetFieldValue(item2, "node"));
					if (!string.IsNullOrWhiteSpace(gameNodeId) && gameNodeId != "<null>")
					{
						_revisitableChildLinks[gameNodeId] = new RevisitableNodeLink
						{
							ParentNodeId = text,
							ParentNode = gameNodeFromRegistry,
							ContinueNode = fieldValue,
							OptionIndex = num2
						};
						num++;
					}
					num2++;
				}
				string gameNodeId2 = GetGameNodeId(fieldValue);
				if (!string.IsNullOrWhiteSpace(gameNodeId2) && gameNodeId2 != "<null>")
				{
					_revisitableContinueNodeIds.Add(gameNodeId2);
				}
			}
			_revisitableLinkCacheBuilt = num > 0;
			Log.LogInfo((object)("[循环选项] 已建立通用循环节点映射: 子节点 " + num + " 个, 继续节点 " + _revisitableContinueNodeIds.Count + " 个"));
		}
		catch (Exception ex)
		{
			_revisitableLinkCacheBuilt = false;
			Log.LogDebug((object)("[循环选项] 建立通用循环节点映射失败: " + ex.Message));
		}
	}

	private static object GetGameNodeRegistry()
	{
		Type type = Type.GetType("GameNodeRegistry, Assembly-CSharp");
		object obj = GetActiveObject(type);
		if (obj == null)
		{
			obj = (type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public))?.GetValue(null);
		}
		return obj;
	}

	private static IEnumerable<object> EnumerateRevisitableRegistryNodeIds(object value)
	{
		if (value is IDictionary dictionary)
		{
			foreach (object key in dictionary.Keys)
			{
				if (IsRevisitableNodeMapping(dictionary[key]))
				{
					yield return key;
				}
			}
			yield break;
		}
		foreach (object item in EnumerateObjects(value))
		{
			if (item is DictionaryEntry dictionaryEntry)
			{
				if (IsRevisitableNodeMapping(dictionaryEntry.Value))
				{
					yield return dictionaryEntry.Key;
				}
				continue;
			}
			object obj = GetFieldValue(item, "Key") ?? GetPropertyValue(item, "Key");
			object mapping = GetFieldValue(item, "Value") ?? GetPropertyValue(item, "Value");
			if (obj != null && IsRevisitableNodeMapping(mapping))
			{
				yield return obj;
			}
		}
	}

	private static bool IsRevisitableNodeMapping(object mapping)
	{
		return (GetFieldValue(mapping, "nodeType") ?? GetPropertyValue(mapping, "nodeType"))?.ToString().Equals("RevisitableOptionNode", StringComparison.Ordinal) ?? false;
	}

	private static object GetPropertyValue(object obj, string propertyName)
	{
		if (obj == null || string.IsNullOrEmpty(propertyName))
		{
			return null;
		}
		try
		{
			return obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj, null);
		}
		catch
		{
			return null;
		}
	}

	private static RevisitableNodeLink CreateRevisitableLink(object parentNode, object childNode)
	{
		object fieldValue = GetFieldValue(parentNode, "options");
		object fieldValue2 = GetFieldValue(parentNode, "nextNodeAfterAllOptions");
		if (fieldValue == null || fieldValue2 == null)
		{
			return null;
		}
		string gameNodeId = GetGameNodeId(childNode);
		int num = 0;
		foreach (object item in EnumerateObjects(fieldValue))
		{
			object fieldValue3 = GetFieldValue(item, "node");
			if (fieldValue3 == childNode || gameNodeId == GetGameNodeId(fieldValue3))
			{
				return new RevisitableNodeLink
				{
					ParentNodeId = GetGameNodeId(parentNode),
					ParentNode = parentNode,
					ContinueNode = fieldValue2,
					OptionIndex = num
				};
			}
			num++;
		}
		return null;
	}

	private static void MarkRevisitableOptionVisited(object gameController, object parentNode, int optionIndex)
	{
		if (gameController == null || parentNode == null || optionIndex < 0)
		{
			return;
		}
		try
		{
			object fieldValue = GetFieldValue(gameController, "revisitableOptionStates");
			string gameNodeId = GetGameNodeId(parentNode);
			int revisitableOptionCount = GetRevisitableOptionCount(parentNode);
			if (fieldValue is Dictionary<string, bool[]> dictionary)
			{
				if (!dictionary.TryGetValue(gameNodeId, out var value) || value == null || value.Length < revisitableOptionCount)
				{
					value = (dictionary[gameNodeId] = new bool[revisitableOptionCount]);
				}
				if (optionIndex < value.Length)
				{
					value[optionIndex] = true;
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[循环选项] 标记循环选项访问状态失败: " + ex.Message));
		}
	}

	private static bool IsRevisitableOptionComplete(object gameController, object parentNode)
	{
		try
		{
			if (gameController == null || parentNode == null)
			{
				return false;
			}
			string gameNodeId = GetGameNodeId(parentNode);
			int revisitableOptionCount = GetRevisitableOptionCount(parentNode);
			if (string.IsNullOrWhiteSpace(gameNodeId) || gameNodeId == "<null>" || revisitableOptionCount <= 0)
			{
				return false;
			}
			if (!(GetFieldValue(gameController, "revisitableOptionStates") is Dictionary<string, bool[]> dictionary) || !dictionary.TryGetValue(gameNodeId, out var value) || value == null)
			{
				return false;
			}
			int num = 0;
			for (int i = 0; i < value.Length; i++)
			{
				if (value[i])
				{
					num++;
				}
			}
			int num2 = 0;
			if (GetFieldValue(parentNode, "requiredVisitedOptionCountBeforeNext") is int num3)
			{
				num2 = num3;
			}
			if (num2 <= 0 || num2 > revisitableOptionCount)
			{
				num2 = revisitableOptionCount;
			}
			Log.LogInfo((object)("[循环选项] 父节点 " + gameNodeId + " 已访问 " + num + "/" + num2));
			return num >= num2;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[循环选项] 判断循环选项完成状态失败: " + ex.Message));
			return false;
		}
	}

	private static int GetRevisitableOptionCount(object node)
	{
		int num = 0;
		foreach (object item in EnumerateObjects(GetFieldValue(node, "options")))
		{
			if (item != null)
			{
				num++;
			}
		}
		return num;
	}

	private static bool TryActivateKnownBrokenRevisitableContinue(object gameController, object currentNode)
	{
		string gameNodeId = GetGameNodeId(currentNode);
		if (gameController == null || string.IsNullOrWhiteSpace(gameNodeId))
		{
			return false;
		}
		if (gameNodeId == "03039")
		{
			object obj = GetFieldValue(currentNode, "nextNode") ?? GetGameNodeFromRegistry("03040");
			if (obj != null)
			{
				return ActivateGameNode(gameController, obj, "[循环选项] 已从继续节点跳到后续节点: " + GetGameNodeId(obj));
			}
			return false;
		}
		if (gameNodeId != "03035" && gameNodeId != "03036" && gameNodeId != "03037" && gameNodeId != "03038")
		{
			return false;
		}
		object obj2 = GetGameNodeFromRegistry("03039") ?? GetGameNodeFromRegistry("03040");
		if (obj2 == null)
		{
			Log.LogWarning((object)"[循环选项] 无法从 GameNodeRegistry 获取 03039/03040");
			return false;
		}
		if (ActivateGameNode(gameController, obj2, "[循环选项] 已针对谁是主谋预览节点跳到继续节点: " + GetGameNodeId(obj2)))
		{
			return true;
		}
		return false;
	}

	private static object GetGameNodeFromRegistry(string nodeId)
	{
		if (string.IsNullOrWhiteSpace(nodeId))
		{
			return null;
		}
		if (_gameNodeCache.TryGetValue(nodeId, out var value))
		{
			return value;
		}
		try
		{
			Type type = Type.GetType("GameNodeRegistry, Assembly-CSharp");
			object obj = GetActiveObject(type);
			if (obj == null)
			{
				obj = (type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public))?.GetValue(null);
			}
			object obj2 = (type?.GetMethod("GetGameNode", BindingFlags.Instance | BindingFlags.Public))?.Invoke(obj, new object[1] { nodeId });
			if (obj2 != null)
			{
				_gameNodeCache[nodeId] = obj2;
				Log.LogInfo((object)("[循环选项] 从 GameNodeRegistry 获取节点成功: " + nodeId));
			}
			return obj2;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[循环选项] 从 GameNodeRegistry 获取节点失败: " + nodeId + ", " + ex.Message));
			return null;
		}
	}

	private static bool ActivateGameNode(object gameController, object targetNode, string successLog)
	{
		if (gameController == null || targetNode == null || _gameControllerType == null)
		{
			return false;
		}
		try
		{
			SetFieldValue(gameController, "parentRevisitableNode", null);
			InvokeNoArg(gameController, "HideFullScreenButtons");
			MethodInfo method = _gameControllerType.GetMethod("OnEnterNewNode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				return false;
			}
			method.Invoke(gameController, new object[1] { targetNode });
			InvokeNoArg(gameController, "InitPlayer");
			Log.LogInfo((object)successLog);
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[循环选项] 激活节点失败: " + ex.GetType().Name + " - " + ex.Message));
			return false;
		}
	}

	private static object FindRevisitableParentNodeForChild(object childNode)
	{
		if (childNode == null || _gameNodeType == null || _gameOptionType == null)
		{
			return null;
		}
		string gameNodeId = GetGameNodeId(childNode);
		try
		{
			Array candidates = FindObjectsOfType(_gameNodeType);
			object obj = FindRevisitableParentNodeForChild(childNode, gameNodeId, candidates);
			if (obj != null)
			{
				return obj;
			}
			object activeObject = GetActiveObject(Type.GetType("GameNodeRegistry, Assembly-CSharp"));
			string[] array = new string[6] { "allNodes", "nodes", "gameNodes", "nodeList", "nodeRegistry", "nodeMap" };
			foreach (string fieldName in array)
			{
				obj = FindRevisitableParentNodeForChild(childNode, gameNodeId, GetFieldValue(activeObject, fieldName));
				if (obj != null)
				{
					return obj;
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("[循环选项] 反查父节点失败: " + ex.Message));
		}
		return null;
	}

	private static object FindRevisitableParentNodeForChild(object childNode, string childNodeId, object candidates)
	{
		foreach (object item in EnumerateObjects(candidates))
		{
			object obj = ExtractGameNode(item);
			if (obj == null)
			{
				continue;
			}
			object fieldValue = GetFieldValue(obj, "options");
			if (fieldValue == null || GetFieldValue(obj, "nextNodeAfterAllOptions") == null)
			{
				continue;
			}
			foreach (object item2 in EnumerateObjects(fieldValue))
			{
				object fieldValue2 = GetFieldValue(item2, "node");
				if (fieldValue2 != null && (fieldValue2 == childNode || (!string.IsNullOrWhiteSpace(childNodeId) && childNodeId != "<null>" && childNodeId == GetGameNodeId(fieldValue2))))
				{
					return obj;
				}
			}
		}
		return null;
	}

	private static object ExtractGameNode(object value)
	{
		if (value == null)
		{
			return null;
		}
		if (_gameNodeType != null && _gameNodeType.IsInstanceOfType(value))
		{
			return value;
		}
		if (value is DictionaryEntry dictionaryEntry)
		{
			return ExtractGameNode(dictionaryEntry.Value) ?? ExtractGameNode(dictionaryEntry.Key);
		}
		object fieldValue = GetFieldValue(value, "node");
		if (_gameNodeType != null && _gameNodeType.IsInstanceOfType(fieldValue))
		{
			return fieldValue;
		}
		return null;
	}

	private static string GetGameNodeId(object node)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		if (node == null)
		{
			return "<null>";
		}
		try
		{
			object fieldValue = GetFieldValue(node, "nodeId");
			if (fieldValue != null)
			{
				return fieldValue.ToString();
			}
			object obj = ((node is Object) ? node : null);
			if (obj != null)
			{
				return ((Object)obj).name;
			}
		}
		catch
		{
		}
		return node.GetType().Name;
	}

	private static bool IsConfirmationDialogOption(OptionItem optionItem)
	{
		string text = optionItem?.Text?.Trim();
		if (string.IsNullOrEmpty(text) || text.Length > 8)
		{
			return false;
		}
		string[] array = new string[10] { "确定", "确认", "是", "退出", "离开", "取消", "否", "返回", "关闭", "不" };
		foreach (string value in array)
		{
			if (text.Equals(value, StringComparison.OrdinalIgnoreCase) || (text.Length <= 4 && text.Contains(value)))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsCurrentOptionsConfirmationDialog()
	{
		if (_options != null)
		{
			return GetConfirmationDialogOptions(_options).Length >= 2;
		}
		return false;
	}

	private static void ClearCurrentOptionsAfterTransientClick(string reason)
	{
		_inOptionsMode = false;
		_options = new OptionItem[0];
		_currentOptionIndex = 0;
		_optionsMissCount = 0;
		_currentUIState = UIState.Unknown;
		_lastDetectedSignature = "";
		MarkNeedDetect();
		LogInputState("Clear options after " + reason);
	}

	private static void ClearCurrentOptionsAfterGameSelection()
	{
		if (AreCurrentOptionsFromGameController())
		{
			_inOptionsMode = false;
			_options = new OptionItem[0];
			_currentOptionIndex = 0;
			_optionsMissCount = 0;
			_currentUIState = UIState.Unknown;
			_lastDetectedSignature = "";
			_ignoreOptionsUntilUtc = DateTime.UtcNow.AddSeconds(1.5);
			MarkNeedDetect();
			LogInputState("Clear options after game selection");
		}
	}

	private static void ClearNodeMode(string reason)
	{
		if (_inNodeMode || (_storylineNodes != null && _storylineNodes.Length != 0) || _currentNodeIndex != 0)
		{
			_inNodeMode = false;
			_storylineNodes = new OptionItem[0];
			_currentNodeIndex = 0;
			LogInputState("Clear node mode: " + reason);
		}
	}

	private static bool TryActivateEndingPageOption(OptionItem optionItem)
	{
		if (optionItem == null || optionItem.ClickableComponent == null || _endingPageControllerType == null || optionItem.ClickableComponent.GetType() != _endingPageControllerType)
		{
			return false;
		}
		string text = null;
		if (optionItem.Index == -9001)
		{
			text = "OnReturnToMainClick";
		}
		else if (optionItem.Index == -9002)
		{
			text = "OnGotoStorylineClick";
		}
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		try
		{
			PlayGameSound("Click");
			TolkHelper.Speak("点击 " + optionItem.Text, interrupt: true);
			MethodInfo method = _endingPageControllerType.GetMethod(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Log.LogWarning((object)("[结尾页] 未找到方法: " + text));
				return false;
			}
			method.Invoke(optionItem.ClickableComponent, null);
			Log.LogInfo((object)("[结尾页] 已调用 " + text + ": " + optionItem.Text));
			_inOptionsMode = false;
			_options = new OptionItem[0];
			_currentOptionIndex = 0;
			_currentUIState = UIState.Unknown;
			_lastDetectedSignature = "";
			MarkNeedDetect();
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[结尾页] 调用按钮方法失败: " + ex.GetType().Name + " - " + ex.Message));
			return false;
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
				if (optionItem != null && optionItem.ClickableComponent != null && ClickComponent(optionItem.ClickableComponent))
				{
					Log.LogInfo((object)("[主菜单] 已触发按钮自身事件: " + text));
					MarkNeedDetect();
					return true;
				}
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
			string text2 = null;
			if (text.Contains("系统设置") || text.Contains("设置"))
			{
				text2 = "LoadSettings";
			}
			else if (text.Contains("档案"))
			{
				text2 = "OpenArchives";
			}
			else if (text.Contains("排行榜") || text.Contains("投票"))
			{
				text2 = "OpenVotePage";
			}
			else if (text.Contains("退出"))
			{
				text2 = "ExitGame";
			}
			if (text2 == null)
			{
				return false;
			}
			MethodInfo method = type.GetMethod(text2, BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			Log.LogInfo((object)("[主菜单] 直接调用 MainMenuManager." + text2 + " 处理: " + text));
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
		bool result = false;
		try
		{
			object activeObject = GetActiveObject(Type.GetType("IntroAnimationController, Assembly-CSharp"));
			if (activeObject != null)
			{
				bool boolProperty = GetBoolProperty(activeObject, "IsPlaying");
				bool boolProperty2 = GetBoolProperty(activeObject, "HasPlayed");
				Log.LogInfo((object)$"[主菜单] Intro 状态 IsPlaying={boolProperty}, HasPlayed={boolProperty2}");
				if (boolProperty)
				{
					MethodInfo method = activeObject.GetType().GetMethod("EndIntro", BindingFlags.Instance | BindingFlags.NonPublic);
					if (method != null)
					{
						method.Invoke(activeObject, null);
						Log.LogInfo((object)"[主菜单] 已结束开场动画阻塞");
						result = true;
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
				return result;
			}
			string text2 = (text.Contains("继续") ? "ContinuePlay" : "StartPlay");
			MethodInfo method2 = activeObject2.GetType().GetMethod(text2, BindingFlags.Instance | BindingFlags.Public);
			if (method2 == null)
			{
				Log.LogWarning((object)("[主菜单] GameController 未找到方法: " + text2));
				return result;
			}
			Log.LogInfo((object)("[主菜单] 直接调用 GameController." + text2 + " 处理: " + text));
			method2.Invoke(activeObject2, null);
			MarkNeedDetect();
			return true;
		}
		catch (Exception ex2)
		{
			Log.LogWarning((object)("[主菜单] 直接启动游戏失败: " + ex2.Message));
			return result;
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
			if (InvokeNoArg(GetFieldValue(activeObject, "storyLinePageToggle") ?? GetFieldValue(activeObject2, "storyLinePageToggle"), "PerformShow"))
			{
				Log.LogInfo((object)"[主菜单] storyLinePageToggle.PerformShow 成功");
				flag = true;
			}
			if (InvokeNoArg(GetFieldValue(activeObject, "chapterStorylineController") ?? GetFieldValue(activeObject2, "chapterStorylineController") ?? GetActiveObject(_chapterStorylineControllerType), "ShowChapterSelection"))
			{
				Log.LogInfo((object)"[主菜单] ChapterStorylineController.ShowChapterSelection 成功");
				flag = true;
			}
			if (InvokeNoArg(GetFieldValue(activeObject2, "storylineUIManager") ?? GetActiveObject(_storylineUIManagerType), "ShowChapterSelection"))
			{
				Log.LogInfo((object)"[主菜单] StorylineUIManager.ShowChapterSelection 成功");
				flag = true;
			}
			if (flag)
			{
				_restoreStorylineNodeModeOnOpen = false;
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

	private static bool TryGetEventLikeMember(Type type, object component, string memberName, out object value)
	{
		value = null;
		if (type == null || component == null || string.IsNullOrEmpty(memberName))
		{
			return false;
		}
		try
		{
			FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				value = field.GetValue(component);
				if (value != null)
				{
					return true;
				}
			}
			PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.GetIndexParameters().Length == 0)
			{
				value = property.GetValue(component);
				return value != null;
			}
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("读取 " + type.Name + "." + memberName + " 失败: " + ex.Message));
		}
		return false;
	}

	private static bool HasPublicInvoke(object value)
	{
		if (value != null)
		{
			return value.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public) != null;
		}
		return false;
	}

	private static bool HasNoArgMethod(Type type, string methodName)
	{
		MethodInfo methodInfo = type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (methodInfo != null)
		{
			return methodInfo.GetParameters().Length == 0;
		}
		return false;
	}

	private static bool TryInvokeEventLikeMember(Type type, object component, string memberName)
	{
		if (!TryGetEventLikeMember(type, component, memberName, out var value) || value == null)
		{
			return false;
		}
		try
		{
			MethodInfo method = value.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			ParameterInfo[] parameters = method.GetParameters();
			object[] array = null;
			if (parameters.Length != 0)
			{
				array = new object[parameters.Length];
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = GetDefaultValue(parameters[i].ParameterType);
				}
			}
			method.Invoke(value, array);
			Log.LogInfo((object)(type.Name + "." + memberName + ".Invoke() 调用成功"));
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("调用 " + type.Name + "." + memberName + ".Invoke() 失败: " + ex.Message));
			return false;
		}
	}

	private static object GetDefaultValue(Type type)
	{
		if (type == null || !type.IsValueType)
		{
			return null;
		}
		return Activator.CreateInstance(type);
	}

	private static bool TryInvokeNoArgClickMethod(Type type, object component, string methodName)
	{
		try
		{
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null || method.GetParameters().Length != 0)
			{
				return false;
			}
			method.Invoke(component, null);
			Log.LogInfo((object)(type.Name + "." + methodName + "() 调用成功"));
			return true;
		}
		catch (Exception ex)
		{
			Log.LogDebug((object)("调用 " + type.Name + "." + methodName + "() 失败: " + ex.Message));
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
			if (TryInvokeEventLikeMember(type, component, "onClick"))
			{
				return true;
			}
			string[] array = new string[6] { "OnClick", "Click", "OnMouseDown", "Trigger", "OnTrigger", "Invoke" };
			foreach (string methodName in array)
			{
				if (TryInvokeNoArgClickMethod(type, component, methodName))
				{
					return true;
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
		ClickAt(y: GetSystemMetrics(1) / 2, x: systemMetrics / 2);
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
			string obj = (string.IsNullOrEmpty(optionItem.Text) ? "（无文字）" : optionItem.Text);
			PlayGameSound("Highlight");
			TolkHelper.Speak(obj + text, interrupt: true);
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
		LeaveArchiveMode();
		ManualLogSource log = Log;
		if (log != null)
		{
			log.LogInfo((object)"离开选项/设置/节点模式");
		}
		LogInputState("LeaveOptions done");
	}

	public static void RequestSpeak(string text)
	{
		RequestSpeakFromTextComponent(null, text);
	}

	public static void RequestSpeakFromTextComponent(object textComponent, string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		text = text.Trim();
		if (IsIgnoredAutoSpeakText(text))
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogDebug((object)("跳过测试字幕: " + text));
			}
		}
		else
		{
			if (string.IsNullOrEmpty(text) || text == _lastSpokenText)
			{
				return;
			}
			if (IsPriorityStatusText(text))
			{
				_lastSpokenText = text;
				TolkHelper.Speak(text, interrupt: true);
				ManualLogSource log2 = Log;
				if (log2 != null)
				{
					log2.LogInfo((object)("[状态提示] 优先朗读: " + text));
				}
				MarkNeedDetect();
				return;
			}
			bool num = IsSubtitleTextComponent(textComponent);
			bool flag = IsNarrationTextComponent(textComponent);
			if (!num && !flag)
			{
				ManualLogSource log3 = Log;
				if (log3 != null)
				{
					log3.LogDebug((object)("[自动朗读] 跳过非字幕/旁白文本: " + text));
				}
				MarkNeedDetect();
				return;
			}
			if (!_subtitleSpeakEnabled)
			{
				ManualLogSource log4 = Log;
				if (log4 != null)
				{
					log4.LogDebug((object)("字幕朗读已关闭，跳过自动朗读: " + text));
				}
				_lastSpokenText = text;
				return;
			}
			_lastSpokenText = text;
			TolkHelper.Speak(text);
			ManualLogSource log5 = Log;
			if (log5 != null)
			{
				log5.LogDebug((object)("朗读: " + text));
			}
			MarkNeedDetect();
		}
	}

	private static bool IsSubtitleTextComponent(object textComponent)
	{
		if (textComponent == null)
		{
			return false;
		}
		ResolveSubtitleTypes();
		if (_subtitleTextComponent != null)
		{
			return textComponent == _subtitleTextComponent;
		}
		return false;
	}

	private static bool IsPriorityStatusText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.Length > 40)
		{
			return false;
		}
		bool num = text2.Contains("好感") || text2.Contains("威望") || text2.Contains("亲密") || text2.Contains("信任");
		bool flag = text2.Contains("增加") || text2.Contains("减少") || text2.Contains("上升") || text2.Contains("下降") || text2.Contains("+") || text2.Contains("-");
		return num && flag;
	}

	private static bool IsIgnoredAutoSpeakText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		string text2 = text.Trim();
		if (!text2.Contains("这是测试字幕") && !text2.Contains("测试字幕") && text2.IndexOf("This is Test Subtitle", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text2.IndexOf("Test Subtitle", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
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
			foreach (OptionItem optionItem in array)
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
				return;
			}
			Log.LogInfo((object)"没有找到可点击组件，尝试用短文本猜测");
			array = allVisibleTextsWithPosition;
			foreach (OptionItem optionItem2 in array)
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
			}
			else if (list.Count > 12)
			{
				Log.LogInfo((object)$"候选选项太多（{list.Count}个）");
				TolkHelper.Speak($"找到 {list.Count} 段短文本，无法确定是否为选项", interrupt: true);
			}
			else
			{
				Log.LogInfo((object)$"候选选项太少（{list.Count}个）");
				TolkHelper.Speak("没有明显选项", interrupt: true);
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
					if (IsNarrationTextComponent(item))
					{
						Log.LogDebug((object)("跳过剧情旁白文本: " + text));
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
				foreach (MethodInfo methodInfo in methods)
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
					MethodInfo[] methods = type5.GetMethods(BindingFlags.Static | BindingFlags.Public);
					foreach (MethodInfo methodInfo2 in methods)
					{
						if (methodInfo2.Name == "FindObjectsByType" && methodInfo2.IsGenericMethodDefinition && methodInfo2.GetParameters().Length == 2)
						{
							MethodInfo methodInfo3 = methodInfo2.MakeGenericMethod(type);
							Type? type6 = Type.GetType("UnityEngine.FindObjectsInactive, UnityEngine");
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
			foreach (OptionItem optionItem in array)
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
