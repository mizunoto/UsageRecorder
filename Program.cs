// file: Program.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;

/**
 * @class Program
 * @brief アプリケーションのエントリーポイントを持つクラスです。
 */
class Program
{
    private static ActivityMonitor _monitor;
    // 2重起動を防止するためのMutexオブジェクト
    // アプリケーション固有のGUIDなどを名前に使うのが一般的です
    private static Mutex _mutex = new Mutex(true, "UsageRecorder-mizunoto-app-mutex");

    /**
     * @brief アプリケーションのメインエントリーポイントです。
     * @param args コマンドライン引数
     */
    [STAThread] // WinFormsを動かすために必要なおまじないです
    static void Main(string[] args)
    {
        if (!_mutex.WaitOne(TimeSpan.Zero, true) && args.Length == 0)
        {
            // Mutexを取得できず(既に起動している)、かつ引数がない場合は、
            // 新しいプロセスを何もせずに終了します。
            return;
        }

        var configOption = new Option<bool>("--config")
        {
            Description = "設定ファイル(settings.ini)のパスを表示します。"
        };

        var versionOption = new Option<bool>("--version")
        {
            Aliases = { "-v" },
            Description = "バージョン情報を表示します。"
        };

        var rootCommand = new RootCommand("PCのアクティビティを監視し、ログに記録するアプリケーションです。")
        {
            configOption,
            versionOption
        };

        rootCommand.SetAction((context) =>
        {
            bool showConfig = context.GetValue(configOption);
            bool showVersion = context.GetValue(versionOption);

            if (showConfig || showVersion)
            {
                // --configが指定された場合、一時的にコンソールを割り当てて表示します
                AllocConsole(); // コンソールを強制的に表示
                try
                {
                    if (showConfig)
                    {
                        string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
                        Console.WriteLine($"設定ファイルのパス: {settingsPath}");
                    }
                    if (showVersion)
                    {
                        var version = Assembly.GetExecutingAssembly().GetName().Version;
                        Console.WriteLine($"UsageRecorder Version: {version?.ToString(3)}");
                    }
                    Console.WriteLine("\n何かキーを押すと終了します...");
                    Console.ReadKey(); // ユーザーの入力を待つ
                }
                finally
                {
                    FreeConsole(); // 表示したコンソールを解放
                }
            }
            else
            {
                // 通常起動の場合は監視を開始
                StartMonitoring(new SettingsManager("settings.ini"));
            }
        });

        var parser = CommandLineParser.Parse(rootCommand, args);

        // Mainスレッドで同期的に実行します（非同期にするとすぐに終了してしまうため）
        parser.Invoke();

        _mutex.ReleaseMutex();
    }

    /**
     * @brief アプリケーションの監視処理を開始します。
     * @param settingsManager 設定を管理するSettingsManagerのインスタンス
     */
    private static void StartMonitoring(SettingsManager settingsManager)
    {
        settingsManager.Load();
        string logPath = settingsManager.LogDirectory;

        try
        {
            _monitor = new ActivityMonitor(logPath);
            // 監視処理の実行
            _monitor.Run();
        }
        catch (Exception ex)
        {
            // 予期せぬエラーはファイルに記録します
            LogWriter.WriteError($"[FATAL] An unhandled exception occurred: {ex}");
        }
    }

    // Windows APIをインポートして、コンソールを動的に表示/非表示します
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}

/**
 * @class LogWriter
 * @brief エラーログなどをファイルに書き出すためのシンプルなクラスです。
 */
public static class LogWriter
{
    private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_error.log");
    private static readonly object _lock = new object();

    /**
     * @brief エラーメッセージをログファイルに追記します。
     * @param message 記録するメッセージ
     */
    public static void WriteError(string message)
    {
        lock (_lock)
        {
            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch
            {
                // 何もしない
            }
        }
    }
}