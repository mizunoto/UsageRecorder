// file: SettingsManager.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/**
 * @class SettingsManager
 * @brief アプリケーションの設定(settings.ini)を管理するクラスです。
 */
public class SettingsManager
{
    private readonly string _settingsFilePath;
    private Dictionary<string, string> _settings;

    /**
     * @property LogDirectory
     * @brief ログファイルを保存するディレクトリのパスを取得します。
     */
    public string LogDirectory { get; private set; }

    /**
     * @brief コンストラクタ
     * @param settingsFileName 設定ファイル名 (例: "settings.ini")
     */
    public SettingsManager(string settingsFileName)
    {
        // 実行ファイルと同じディレクトリに設定ファイルを置きます。
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
        _settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /**
     * @brief 設定を読み込みます。ファイルが存在しない場合はデフォルト設定で新規作成します。
     */
    public void Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            Console.WriteLine("設定ファイルが見つかりません。デフォルト設定で新規作成します。");
            CreateDefaultSettingsFile();
        }

        ParseSettingsFile();

        // 読み込んだ設定値を取得し、不正な場合はデフォルト値を使用します。
        if (!_settings.TryGetValue("LogDirectory", out string logDir) || string.IsNullOrWhiteSpace(logDir))
        {
            Console.WriteLine("[WARNING] LogDirectoryの設定が不正です。デフォルト値を使用します。");
            LogDirectory = GetDefaultLogDirectory();
        }
        else
        {
            // パスに環境変数などが含まれている可能性を考慮して展開します。
            LogDirectory = Environment.ExpandEnvironmentVariables(logDir);
        }
    }

    /**
     * @brief デフォルトのログディレクトリパスを取得します。
     * @return デフォルトのログディレクトリのフルパス
     */
    private string GetDefaultLogDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
    }

    /**
     * @brief デフォルト設定で設定ファイルを作成します。
     */
    private void CreateDefaultSettingsFile()
    {
        try
        {
            string defaultConfig =
@"; UsageRecorder Settings
; このファイルでアプリケーションの動作を設定します。

[Settings]
; ログファイルを保存するディレクトリのパス
; 絶対パス (例: C:\Users\YourName\Documents\UsageLogs)
; 相対パス (例: log) が指定できます。
LogDirectory = log
";
            File.WriteAllText(_settingsFilePath, defaultConfig, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] デフォルト設定ファイルの作成に失敗しました: {ex.Message}");
        }
    }

    /**
     * @brief 設定ファイルを解析し、内容をメモリに読み込みます。
     */
    private void ParseSettingsFile()
    {
        _settings.Clear();
        try
        {
            string[] lines = File.ReadAllLines(_settingsFilePath);
            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                // コメント行や空行は無視します
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                {
                    continue;
                }

                // キーと値を'='で分割します。値に'='が含まれる可能性を考慮して、最初の'='のみで分割します。
                string[] parts = trimmedLine.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    _settings[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 設定ファイルの読み込みに失敗しました: {ex.Message}");
        }
    }
}