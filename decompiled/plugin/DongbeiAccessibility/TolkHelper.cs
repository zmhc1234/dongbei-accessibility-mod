using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace DongbeiAccessibility;

public static class TolkHelper
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Tolk_LoadDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Tolk_UnloadDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_SpeakDelegate(IntPtr text, [MarshalAs(UnmanagedType.I1)] bool interrupt);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_SilenceDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_IsSpeakingDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr Tolk_DetectScreenReaderDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_IsLoadedDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_HasSpeechDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Tolk_HasBrailleDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Tolk_TrySAPIDelegate([MarshalAs(UnmanagedType.I1)] bool trySAPI);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Tolk_PreferSAPIDelegate([MarshalAs(UnmanagedType.I1)] bool preferSAPI);

	private static Tolk_LoadDelegate _tolkLoad;

	private static Tolk_UnloadDelegate _tolkUnload;

	private static Tolk_SpeakDelegate _tolkSpeak;

	private static Tolk_SilenceDelegate _tolkSilence;

	private static Tolk_IsSpeakingDelegate _tolkIsSpeaking;

	private static Tolk_DetectScreenReaderDelegate _tolkDetectScreenReader;

	private static Tolk_IsLoadedDelegate _tolkIsLoaded;

	private static Tolk_HasSpeechDelegate _tolkHasSpeech;

	private static Tolk_HasBrailleDelegate _tolkHasBraille;

	private static Tolk_TrySAPIDelegate _tolkTrySAPI;

	private static Tolk_PreferSAPIDelegate _tolkPreferSAPI;

	private static bool _initialized = false;

	private static bool _available = false;

	private static IntPtr _hModule = IntPtr.Zero;

	public static bool IsAvailable => _available;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string lpFileName);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
	private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

	public static void Initialize()
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		ManualLogSource log = Plugin.Log;
		if (log != null)
		{
			log.LogInfo((object)"正在初始化 Tolk 屏幕阅读器接口（委托模式）...");
		}
		try
		{
			_hModule = LoadLibrary("Tolk.dll");
			if (_hModule == IntPtr.Zero)
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				ManualLogSource log2 = Plugin.Log;
				if (log2 != null)
				{
					log2.LogWarning((object)$"无法加载 Tolk.dll，错误码: {lastWin32Error}");
				}
				_available = false;
				return;
			}
			ManualLogSource log3 = Plugin.Log;
			if (log3 != null)
			{
				log3.LogInfo((object)"Tolk.dll 加载成功");
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log4 = Plugin.Log;
			if (log4 != null)
			{
				log4.LogWarning((object)("加载 Tolk.dll 时发生异常: " + ex.GetType().Name + " - " + ex.Message));
			}
			_available = false;
			return;
		}
		try
		{
			_tolkLoad = GetDelegate<Tolk_LoadDelegate>("Tolk_Load");
			_tolkUnload = GetDelegate<Tolk_UnloadDelegate>("Tolk_Unload");
			_tolkSpeak = GetDelegate<Tolk_SpeakDelegate>("Tolk_Speak");
			_tolkSilence = GetDelegate<Tolk_SilenceDelegate>("Tolk_Silence");
			_tolkIsSpeaking = GetDelegate<Tolk_IsSpeakingDelegate>("Tolk_IsSpeaking");
			_tolkDetectScreenReader = GetDelegate<Tolk_DetectScreenReaderDelegate>("Tolk_DetectScreenReader");
			_tolkIsLoaded = GetDelegate<Tolk_IsLoadedDelegate>("Tolk_IsLoaded");
			_tolkHasSpeech = GetDelegate<Tolk_HasSpeechDelegate>("Tolk_HasSpeech");
			_tolkHasBraille = GetDelegate<Tolk_HasBrailleDelegate>("Tolk_HasBraille");
			_tolkTrySAPI = GetDelegate<Tolk_TrySAPIDelegate>("Tolk_TrySAPI");
			_tolkPreferSAPI = GetDelegate<Tolk_PreferSAPIDelegate>("Tolk_PreferSAPI");
			ManualLogSource log5 = Plugin.Log;
			if (log5 != null)
			{
				log5.LogInfo((object)"所有 Tolk 函数委托创建成功");
			}
		}
		catch (Exception ex2)
		{
			ManualLogSource log6 = Plugin.Log;
			if (log6 != null)
			{
				log6.LogWarning((object)("创建 Tolk 委托失败: " + ex2.GetType().Name + " - " + ex2.Message));
			}
			_available = false;
			return;
		}
		try
		{
			_tolkTrySAPI?.Invoke(trySAPI: true);
			_tolkLoad?.Invoke();
			if (_tolkIsLoaded != null && _tolkIsLoaded())
			{
				_available = true;
				ManualLogSource log7 = Plugin.Log;
				if (log7 != null)
				{
					log7.LogInfo((object)"Tolk 初始化成功");
				}
				string text = DetectScreenReader();
				ManualLogSource log8 = Plugin.Log;
				if (log8 != null)
				{
					log8.LogInfo((object)("检测到的屏幕阅读器: " + text));
				}
				ManualLogSource log9 = Plugin.Log;
				if (log9 != null)
				{
					log9.LogInfo((object)$"是否支持语音: {_tolkHasSpeech?.Invoke() ?? false}");
				}
				ManualLogSource log10 = Plugin.Log;
				if (log10 != null)
				{
					log10.LogInfo((object)$"是否支持盲文: {_tolkHasBraille?.Invoke() ?? false}");
				}
			}
			else
			{
				_available = false;
				ManualLogSource log11 = Plugin.Log;
				if (log11 != null)
				{
					log11.LogWarning((object)"Tolk_Load 返回 false，初始化失败");
				}
			}
		}
		catch (Exception ex3)
		{
			ManualLogSource log12 = Plugin.Log;
			if (log12 != null)
			{
				log12.LogWarning((object)"Tolk 初始化时发生未知异常:");
			}
			ManualLogSource log13 = Plugin.Log;
			if (log13 != null)
			{
				log13.LogWarning((object)("  异常类型: " + ex3.GetType().FullName));
			}
			ManualLogSource log14 = Plugin.Log;
			if (log14 != null)
			{
				log14.LogWarning((object)("  异常消息: " + ex3.Message));
			}
			ManualLogSource log15 = Plugin.Log;
			if (log15 != null)
			{
				log15.LogWarning((object)("  堆栈跟踪: " + ex3.StackTrace));
			}
			_available = false;
		}
	}

	private static T GetDelegate<T>(string functionName) where T : Delegate
	{
		IntPtr procAddress = GetProcAddress(_hModule, functionName);
		if (procAddress == IntPtr.Zero)
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogWarning((object)$"找不到函数: {functionName}, 错误码: {lastWin32Error}");
			}
			return null;
		}
		return (T)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(T));
	}

	public static string DetectScreenReader()
	{
		if (!_available || _tolkDetectScreenReader == null)
		{
			return "不可用";
		}
		try
		{
			IntPtr intPtr = _tolkDetectScreenReader();
			if (intPtr == IntPtr.Zero)
			{
				return "未知";
			}
			return Marshal.PtrToStringUni(intPtr) ?? "未知";
		}
		catch
		{
			return "检测失败";
		}
	}

	public static void Speak(string text, bool interrupt = false)
	{
		if (!_available || _tolkSpeak == null || string.IsNullOrEmpty(text))
		{
			return;
		}
		IntPtr intPtr = IntPtr.Zero;
		try
		{
			intPtr = Marshal.StringToHGlobalUni(text);
			_tolkSpeak(intPtr, interrupt);
		}
		catch (Exception ex)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogDebug((object)("Tolk 朗读失败: " + ex.Message));
			}
		}
		finally
		{
			if (intPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}
	}

	public static void Stop()
	{
		if (!_available || _tolkSilence == null)
		{
			return;
		}
		try
		{
			_tolkSilence();
		}
		catch (Exception ex)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogDebug((object)("Tolk 停止朗读失败: " + ex.Message));
			}
		}
	}

	public static bool IsSpeaking()
	{
		if (!_available || _tolkIsSpeaking == null)
		{
			return false;
		}
		try
		{
			return _tolkIsSpeaking();
		}
		catch
		{
			return false;
		}
	}

	public static void Unload()
	{
		if (!_available)
		{
			return;
		}
		try
		{
			_tolkUnload?.Invoke();
			_available = false;
		}
		catch (Exception ex)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogDebug((object)("Tolk 卸载失败: " + ex.Message));
			}
		}
	}
}
