// file: ActivityMonitor.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/**
 * @class ActivityMonitor
 * @brief アクティブウィンドウの変更を監視し、活動ログを記録するクラスです。
 * IDisposableを実装し、リソースの解放を保証します。
 */
public class ActivityMonitor : IDisposable
{
    // Windows APIをC#から呼び出すための準備です。
    // アクティブウィンドウが変更されたことを知るための仕組みを使います。
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private readonly string _logDirectory;
    private IntPtr _hook;
    private WinEventDelegate _delegate; // デリゲートをフィールドとして保持し、ガベージコレクションを防ぎます
    private string _lastWindowTitle = string.Empty;

    /**
     * @brief コンストラクタ
     * @param logDirectory ログファイルを保存するディレクトリのパス
     */
    public ActivityMonitor(string logDirectory)
    {
        _logDirectory = logDirectory;
        // デリゲートのインスタンスを作成
        _delegate = new WinEventDelegate(WinEventProc);
    }

    /**
     * @brief ウィンドウの監視を開始します。
     */
    public void Start()
    {
        // ログディレクトリが存在しない場合は作成
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // ウィンドウイベントのフックを設定
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        LogActivity("Application Started");
        // 開始直後のアクティブウィンドウも記録します
        LogCurrentActiveWindow();
    }

    /**
     * @brief ウィンドウの監視を停止します。
     */
    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            LogActivity("Application Ended");
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /**
     * @brief リソースを解放します。
     */
    public void Dispose()
    {
        Stop();
    }

    /**
     * @brief ウィンドウイベントが発生したときに呼び出されるコールバックメソッドです。
     */
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        LogCurrentActiveWindow();
    }

    /**
     * @brief 現在のアクティブウィンドウのタイトルを取得し、ログに記録します。
     */
    private void LogCurrentActiveWindow()
    {
        IntPtr hWnd = GetForegroundWindow();
        StringBuilder sb = new StringBuilder(256);
        if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
        {
            string currentWindowTitle = sb.ToString();
            // 直前のウィンドウタイトルと同じ場合は記録しない
            if (currentWindowTitle != _lastWindowTitle)
            {
                LogActivity(currentWindowTitle);
                _lastWindowTitle = currentWindowTitle;
            }
        }
    }

    /**
     * @brief 指定されたメッセージをCSV形式でログファイルに記録します。
     * @param message 記録するメッセージ（ウィンドウタイトルなど）
     */
    public void LogActivity(string message) // ← private から public に変更
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
            Console.WriteLine($"[ERROR] Failed to write log: {ex.Message}");
        }
    }
}