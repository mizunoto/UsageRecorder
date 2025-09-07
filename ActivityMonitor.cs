// file: ActivityMonitor.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
// Windows Formsの機能を使うためにusingを追加
using System.Windows.Forms;

/**
 * @class ActivityMonitor
 * @brief アクティブウィンドウの変更を監視し、活動ログを記録するクラスです。
 */
public class ActivityMonitor : IDisposable
{
    // ... (Win32 APIの定義は変更ありません) ...
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private readonly string _logDirectory;
    private IntPtr _hook;
    private WinEventDelegate _delegate;
    private string _lastWindowTitle = string.Empty;
    private System.Threading.Timer _idleCheckTimer;
    private bool _isIdle = false;
    private const int IDLE_THRESHOLD_MS = 5 * 60 * 1000;

    // ApplicationContextは、ウィンドウなしでメッセージループを動かすための仕組みです
    private ApplicationContext _context;

    /**
     * @brief コンストラクタ
     * @param logDirectory ログファイルを保存するディレクトリのパス
     */
    public ActivityMonitor(string logDirectory)
    {
        _logDirectory = logDirectory;
        _delegate = new WinEventDelegate(WinEventProc);
    }

    /**
     * @brief 監視処理を開始し、メッセージループを実行します。プログラムはここで待機状態になります。
     */
    public void Run()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // OSの終了イベントを検知してクリーンアップ処理を行うように設定
        Application.ApplicationExit += OnApplicationExit;

        // ウィンドウイベントのフックを設定
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        _idleCheckTimer = new System.Threading.Timer(CheckIdleState, null, 0, 10000);

        LogActivity("Application Started");
        LogCurrentActiveWindow();

        // Application.Run()でメッセージループを開始します。
        // これがウィンドウイベントを安定して受信するための鍵だよ。
        _context = new ApplicationContext();
        Application.Run(_context);
    }

    /**
     * @brief アプリケーション終了時に呼び出されるイベントハンドラです。
     */
    private void OnApplicationExit(object sender, EventArgs e)
    {
        // OSからのシャットダウン信号などでここが呼ばれます
        Stop();
    }

    /**
     * @brief ウィンドウの監視を停止します。
     */
    private void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            if (_isIdle)
            {
                LogActivity("Idle End");
            }
            // Application.ApplicationExitイベントはOSシャットダウン時に発生するので、
            // ここで記録するメッセージをより具体的にします。
            LogActivity("System Shutdown or Logoff");

            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        _idleCheckTimer?.Change(Timeout.Infinite, 0);
    }

    /**
     * @brief リソースを解放します。
     */
    public void Dispose()
    {
        Stop();
        _idleCheckTimer?.Dispose();
        _context?.Dispose();
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