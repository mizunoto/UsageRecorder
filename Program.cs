// file: Program.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

/**
 * @class Program
 * @brief アプリケーションのエントリーポイントを持つクラスです。
 */
class Program
{
    private static ActivityMonitor _monitor;
    // プログラムが終了シグナルを受け取るまで待機するための仕組みです。
    private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);

    // Windowsからの制御信号を処理するためのデリゲートとAPIの宣言です
    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

    // コールバックメソッドの型を定義します
    private delegate bool HandlerRoutine(CtrlType sig);

    // 制御信号の種類を定義する列挙型です
    private enum CtrlType
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }

    // デリゲートのインスタンスをフィールドとして保持し、ガベージコレクションを防ぎます
    private static HandlerRoutine _handler;

    /**
     * @brief アプリケーションのメインエントリーポイントです。
     * @param args コマンドライン引数
     */
    static void Main(string[] args)
    {
        // とりあえず、ログの保存場所をプログラム内に直接書いておきます。
        // 将来的にはこれをsettings.iniから読み込むように変更。
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");

        _monitor = new ActivityMonitor(logPath);

        // Windowsからの制御信号(シャットダウン、ログオフなど)を補足するハンドラを登録します。
        // これにより、単にウィンドウを閉じた時だけでなく、OSからの終了命令も検知できるようになります。
        _handler = new HandlerRoutine(ConsoleCtrlCheck);
        SetConsoleCtrlHandler(_handler, true);

        // 監視を開始
        _monitor.Start();
        Console.WriteLine("アクティビティの監視を開始しました。");
        Console.WriteLine("このウィンドウを閉じるか、Ctrl+Cを押すと監視を終了します。");

        // 終了シグナルが来るまで、ここでプログラムを待機させます。
        _exitEvent.WaitOne();

        // クリーンアップ処理
        Console.WriteLine("監視を終了処理中...");
        // _monitor?.Dispose() は、もし_monitorがnullでなければDispose()を呼ぶ、という書き方です。
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
            _monitor.LogActivity(logMessage); // 終了理由をログに記録

            // 少し待機時間を設けることで、ファイルへの書き込みが完了するのを待ちます。
            // シャットダウン時はシステムがすぐにプロセスを終了させようとするためだよ。
            Thread.Sleep(1000);

            _exitEvent.Set(); // Mainスレッドの待機を解除して、クリーンアップ処理に進ませる
        }

        // trueを返すことで、このイベントを処理したことをOSに伝えます
        return true;
    }
}