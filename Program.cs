// file: Program.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Parsing;

/**
 * @class Program
 * @brief アプリケーションのエントリーポイントを持つクラスです。
 */
class Program
{
    private static ActivityMonitor _monitor;

    /**
     * @brief アプリケーションのメインエントリーポイントです。
     * @param args コマンドライン引数
     */
    [STAThread] // WinFormsを動かすために必要なおまじないです
    static void Main(string[] args)
    {
        var configOption = new Option<bool>("--config")
        {
            Description = "設定ファイル(settings.ini)のパスを表示します。"
        };

        var rootCommand = new RootCommand("PCのアクティビティを監視し、ログに記録するアプリケーションです。")
        {
            configOption
        };

        rootCommand.SetAction((context) =>
        {
            bool showConfig = context.GetValue(configOption);
            var settingsManager = new SettingsManager("settings.ini");

            if (showConfig)
            {
                // --configが指定された場合、一時的にコンソールを割り当てて表示します
                AllocConsole(); // コンソールを強制的に表示
                try
                {
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
                    Console.WriteLine($"設定ファイルのパス: {settingsPath}");
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
                StartMonitoring(settingsManager);
            }
        });

        var parser = CommandLineParser.Parse(rootCommand, args);

        // Mainスレッドで同期的に実行します（非同期にするとすぐに終了してしまうため）
        parser.Invoke();
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