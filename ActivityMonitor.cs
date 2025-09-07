// file: ActivityMonitor.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms; // ApplicationContext を使うために必要です

/**
 * @class ActivityMonitor
 * @brief アクティブウィンドウの変更を監視し、活動ログを記録するクラスです。
 */
public class ActivityMonitor : IDisposable
{
    // Win32 API定義と、その他のフィールドは変更ありません
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint max, IntPtr mod, WinEventDelegate proc, uint idProcess, uint idThread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder str, int maxCount);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private readonly string _logDirectory;
    private IntPtr _hook;
    private WinEventDelegate _delegate;
    private string _lastWindowTitle = string.Empty;
    private System.Threading.Timer _idleCheckTimer;
    private bool _isIdle = false;
    private const int IDLE_THRESHOLD_MS = 5 * 60 * 1000;

    // ApplicationContextが、ウィンドウなしでメッセージループを動かすための心臓部です
    private ApplicationContext _context;

    public ActivityMonitor(string logDirectory)
    {
        _logDirectory = logDirectory;
        // ガベージコレクションでデリゲートが解放されないように、フィールドに保持します
        _delegate = new WinEventDelegate(WinEventProc);
    }

    public void Run()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // OSのシャットダウンやログオフを検知するためのイベントハンドラを登録します
        Application.ApplicationExit += OnApplicationExit;

        // ウィンドウイベントのフックを開始
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        _idleCheckTimer = new System.Threading.Timer(CheckIdleState, null, 0, 10000);

        LogActivity("Application Started");
        LogCurrentActiveWindow();

        // ApplicationContextを使って、ウィンドウなしのメッセージループを開始します。
        // これで、Alt+Tabにウィンドウが表示されることは二度とありません。
        _context = new ApplicationContext();
        Application.Run(_context);
    }

    private void OnApplicationExit(object sender, EventArgs e)
    {
        // OSからの終了命令でここが呼ばれます
        StopAndDispose();
    }

    private void StopAndDispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;

            if (_isIdle)
            {
                LogActivity("Idle End");
            }
            LogActivity("Application Ended");
        }

        _idleCheckTimer?.Dispose();
        _context?.Dispose();
    }

    public void Dispose()
    {
        StopAndDispose();
    }

    // ... (WinEventProc, CheckIdleState, GetIdleTime, LogCurrentActiveWindow, LogActivityメソッドは変更ありません) ...
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        CheckIdleState(null);
        LogCurrentActiveWindow();
    }

    private void CheckIdleState(object state)
    {
        uint idleTime = GetIdleTime();
        bool currentlyIdle = idleTime > IDLE_THRESHOLD_MS;
        if (_isIdle != currentlyIdle)
        {
            _isIdle = currentlyIdle;
            LogActivity(_isIdle ? "Idle Start" : "Idle End");
            if (!_isIdle)
            {
                LogCurrentActiveWindow();
            }
        }
    }

    private static uint GetIdleTime()
    {
        LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
        if (!GetLastInputInfo(ref lastInputInfo))
        {
            return 0;
        }
        return (uint)Environment.TickCount - lastInputInfo.dwTime;
    }

    private void LogCurrentActiveWindow()
    {
        if (_isIdle) return;
        IntPtr hWnd = GetForegroundWindow();
        StringBuilder sb = new StringBuilder(256);
        if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
        {
            string currentWindowTitle = sb.ToString();
            if (currentWindowTitle != _lastWindowTitle)
            {
                LogActivity(currentWindowTitle);
                _lastWindowTitle = currentWindowTitle;
            }
        }
    }

    public void LogActivity(string message)
    {
        try
        {
            DateTime now = DateTime.Now;
            string date = now.ToString("yyyy-MM-dd");
            string time = now.ToString("HH:mm:ss");
            string fileName = $"usage history {date}.csv";
            string filePath = Path.Combine(_logDirectory, fileName);
            string escapedMessage = $"\"{message.Replace("\"", "\"\"")}\"";
            string logEntry = $"{time},{escapedMessage}{Environment.NewLine}";
            File.AppendAllText(filePath, logEntry, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogWriter.WriteError($"[ERROR] Failed to write log: {ex.Message}");
        }
    }
}