// file: Program.cs

using System;
using System.IO;
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

        // プログラムが終了する時(×ボタンで閉じるなど)の処理を登録
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        // Ctrl+Cが押された時の処理を登録
        Console.CancelKeyPress += OnCancelKeyPress;

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
     * @brief プロセスが終了する際に呼び出されるイベントハンドラです。
     */
    private static void OnProcessExit(object sender, EventArgs e)
    {
        // 予期せずプログラムが終了した場合でも、きちんと後片付けができるようにします。
        _monitor?.Dispose();
    }

    /**
     * @brief Ctrl+Cが押された際に呼び出されるイベントハンドラです。
     */
    private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        // デフォルトの動作(プロセス終了)をキャンセルして、自分で終了処理を行う
        e.Cancel = true;
        // Mainメソッドの待機を解除するためのシグナルを送ります
        _exitEvent.Set();
    }
}