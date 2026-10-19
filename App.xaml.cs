using System;
using System.IO;
using System.Windows;

namespace PowerChangerGUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ▼ アプリ全体で発生した「予期せぬエラー」をすべてキャッチする罠 ▼
            this.DispatcherUnhandledException += (sender, args) =>
            {
                // 1. 実行ファイルと同じ場所に「error_log.txt」を作って、エラーの全貌を書き込む
                File.WriteAllText("error_log.txt", args.Exception.ToString());

                // 2. 画面にポップアップを出して、エラーが起きたことを知らせる
                MessageBox.Show("致命的なエラーを検知しました。\n詳細は error_log.txt を確認してください。\n\n【原因】\n" + args.Exception.Message, 
                                "エラーログ検知", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Error);

                // 3. アプリの強制終了（サイレントクラッシュ）を防ぐ
                args.Handled = true;
            };
        }
    }
}