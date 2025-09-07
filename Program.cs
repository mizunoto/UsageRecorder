// file: Program.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks; // Taskを使うために追加
using System.CommandLine; // System.CommandLineライブラリを使うために追加

/**
 * @class Program
 * @brief アプリケーションのエントリーポイントを持つクラスです。
 */
class Program
{
    private static ActivityMonitor _monitor;
    private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);
    private delegate bool HandlerRoutine(CtrlType sig);
    private enum CtrlType
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }
    private static HandlerRoutine _handler;

    /**
     * @brief アプリケーションのメインエントリーポイントです。
     * コマンドライン引数を解析し、適切な処理を呼び出します。
     * @param args コマンドライン引数
     * @return 終了コード
     */
    static async Task<int> Main(string[] args)
    {
        // --- コマンドライン引数の定義 ---

        // 1. --config オプションを定義します。
        var configOption = new Option<bool>(
            name: "--config",
            description: "設定ファイル(settings.ini)のパスを表示します。");

        // 2. ルートコマンドを定義します。これは引数なしで実行されたときのメインの動作です。
        var rootCommand = new RootCommand("PCのアクティビティを監視し、ログに記録するアプリケーションです。");
        rootCommand.AddOption(configOption);

        // 3. コマンドが実行されたときの処理を設定します。
        rootCommand.SetHandler((showConfig) =>
        {
            var settingsManager = new SettingsManager("settings.ini");

            if (showConfig)
            {
                // --config が指定された場合の処理
                // 設定ファイルのフルパスを表示します。
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
                Console.WriteLine($"設定ファイルのパス: {settingsPath}");
            }
            else
            {
                // 引数なしで実行された場合の処理（メインの監視処理）
                StartMonitoring(settingsManager);
            }
        }, configOption);

        // --- コマンドの実行 ---
        // 引数を解析し、設定されたハンドラを呼び出します。
        return await rootCommand.InvokeAsync(args);
    }

    /**
     * @brief アプリケーションの監視処理を開始し、終了シグナルを待機します。
     * @param settingsManager 設定を管理するSettingsManagerのインスタンス
     */
    private static void StartMonitoring(SettingsManager settingsManager)
    {
        settingsManager.Load();
        string logPath = settingsManager.LogDirectory;

        _monitor = new ActivityMonitor(logPath);

        _handler = new HandlerRoutine(ConsoleCtrlCheck);
        SetConsoleCtrlHandler(_handler, true);

        _monitor.Start();
        Console.WriteLine("アクティビティの監視を開始しました。");
        Console.WriteLine($"ログは '{logPath}' に保存されます。");
        Console.WriteLine("OSのシャットダウン、またはこのウィンドウを閉じると監視を終了します。");

        _exitEvent.WaitOne();

        Console.WriteLine("監視を終了処理中...");
        _monitor?.Dispose();
        Console.WriteLine("監視を終了しました。");
    }

    /**
     * @brief Windowsから制御信号を受け取った際に呼び出されるコールバックメソッドです。
     * @param signalType 受け取った信号の種類
     * @return イベントを処理した場合はtrue、そうでなければfalse
     */
    private static bool ConsoleCtrlCheck(CtrlType signalType)
    {
        string logMessage = string.Empty;

        switch (signalType)
        {
            case CtrlType.CTRL_C_EVENT:
            case CtrlType.CTRL_BREAK_EVENT:
            case CtrlType.CTRL_CLOSE_EVENT:
                logMessage = "Application Terminated by User";
                break;
            case CtrlType.CTRL_LOGOFF_EVENT:
                logMessage = "System Logoff";
                break;
            case CtrlType.CTRL_SHUTDOWN_EVENT:
                logMessage = "System Shutdown";
                break;
        }

        if (!string.IsNullOrEmpty(logMessage))
        {
            Console.WriteLine($"終了シグナル受信: {signalType}");
            if (_monitor != null)
            {
                _monitor.LogActivity(logMessage);
                Thread.Sleep(1000);
            }

            _exitEvent.Set();
        }

        return true;
    }
}