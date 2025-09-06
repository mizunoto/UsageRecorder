// file: ActivityMonitor.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private readonly string _logDirectory;
    private IntPtr _hook;
    private WinEventDelegate _delegate; // デリゲートをフィールドとして保持し、ガベージコレクションを防ぎます
    private string _lastWindowTitle = string.Empty;

    private Timer _idleCheckTimer;
    private bool _isIdle = false;
    // アイドル状態と判断するまでの時間（ミリ秒）。ここでは5分に設定しています。
    private const int IDLE_THRESHOLD_MS = 5 * 60 * 1000;

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

        // 10秒ごとにアイドル状態をチェックするタイマーを開始します。
        _idleCheckTimer = new Timer(CheckIdleState, null, 0, 10000);

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

        // ユーザーの操作があったということなので、アイドル状態から復帰したかチェックします。
        CheckIdleState(null);
        LogCurrentActiveWindow();
    }

    /**
     * @brief ユーザーのアイドル状態を定期的にチェックします。
     */
    private void CheckIdleState(object state)
    {
        uint idleTime = GetIdleTime();

        // 現在アイドル状態かどうか
        bool currentlyIdle = idleTime > IDLE_THRESHOLD_MS;

        if (_isIdle != currentlyIdle)
        {
            // 状態が変化した時だけログに記録します
            _isIdle = currentlyIdle;
            LogActivity(_isIdle ? "Idle Start" : "Idle End");

            // アイドルから復帰した直後は、現在のアクティブウィンドウも記録しておくと分かりやすいです。
            if (!_isIdle)
            {
                LogCurrentActiveWindow();
            }
        }
    }

    /**
     * @brief 最後のユーザー入力からの経過時間をミリ秒単位で取得します。
     * @return 経過時間 (ミリ秒)
     */
    private static uint GetIdleTime()
    {
        LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

        if (!GetLastInputInfo(ref lastInputInfo))
        {
            return 0;
        }

        // Environment.TickCountはOS起動からの経過ミリ秒。GetLastInputInfoのdwTimeも同様。
        // これらは周回することがある(約49.7日で0に戻る)ので、差分を取ることでその影響を回避します。
        return (uint)Environment.TickCount - lastInputInfo.dwTime;
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