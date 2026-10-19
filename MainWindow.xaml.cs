using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerChangerGUI
{
    public partial class MainWindow : Window
    {
        // 電源プラン1件分の情報（powercfg /list の1行に対応）
        private record PowerPlan(string Guid, string Name, bool IsActive);

        // プランの「負荷の強さ」。High 以上は薄赤色のボタンにして、ノートパソコンでは確認を出す
        private enum PlanRisk
        {
            Normal,   // 普通（確認なし）
            High,     // 高パフォーマンス（ノートパソコンでは確認1回）
            Extreme,  // 究極のパフォーマンス（ノートパソコンでは確認2回）
        }

        // Windows に元から用意されている標準プラン。
        // PC によっては一覧（/list）に出てこないが、GUID を指定すれば直接切り替えられることが多い。
        //   Guid     … Windows 標準の GUID
        //   CopyGuid … 標準の GUID で切り替えられない PC 用に、このアプリが複製を作るときの GUID
        //              （固定にしておくことで、何度押しても複製が増えない）
        private record StandardPlan(string DisplayName, string Guid, string CopyGuid, string[] KnownNames, PlanRisk Risk);

        private static readonly StandardPlan[] StandardPlans =
        {
            new("省電力",
                "a1841308-3541-4fab-bc81-f71556f20b4a", "d41172d4-ea65-4524-b797-eb247f0a8e9f",
                new[] { "省電力", "Power saver" }, PlanRisk.Normal),
            new("高パフォーマンス",
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "517e45d5-a06e-498d-b3cf-0b4bdb6c2e28",
                new[] { "高パフォーマンス", "High performance" }, PlanRisk.High),
            new("究極のパフォーマンス",
                "e9a42b02-d5df-448d-aa00-03f14749eb61", "5f60390b-f8e2-4a46-ae43-c37ae07bc34d",
                new[] { "究極のパフォーマンス", "Ultimate Performance" }, PlanRisk.Extreme),
        };

        private const string LaptopWarningText = "ノートパソコンでの常用は推奨されていません";

        // 起動時に一度だけ、ノートパソコンかどうかを調べておく
        private readonly bool _isLaptop = DeviceType.IsLaptop();

        // 「電源設定の GUID: xxxxxxxx-....  (プラン名) *」の行から
        //  1: GUID  2: プラン名  3: 使用中マーク(*) を取り出す
        private const string GuidPattern = @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

        private static readonly Regex PlanLineRegex = new("(" + GuidPattern + @")\s+\((.*)\)\s*(\*)?\s*$");

        private static readonly Regex GuidRegex = new(GuidPattern);

        private static readonly string PowercfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");

        private static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();

        public MainWindow()
        {
            InitializeComponent();

            // ウィンドウが画面（タスクバーを除いた範囲）より縦に大きくならないようにする
            MaxHeight = SystemParameters.WorkArea.Height;

            DeviceText.Text = _isLaptop
                ? "この PC はノートパソコンと判定されました。\n薄赤色のプランに切り替えるときは確認が出ます。"
                : "この PC はデスクトップと判定されました。\n※薄赤色のプランは" + LaptopWarningText + "。";
        }

        // ───────── 画面のイベント ─────────

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 画面を出してから取得するので「設定取得中...」がちゃんと見える
            await RefreshAsync();

            // ボタンが増えて縦に伸びたので、レイアウトが終わってから画面の真ん中に置き直す
            await Dispatcher.InvokeAsync(CenterOnScreen, DispatcherPriority.ContextIdle);
        }

        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        // ───────── 一覧の取得と表示 ─────────

        // プラン一覧を読み直してボタンを作り直す
        private async Task<List<PowerPlan>> RefreshAsync()
        {
            SetBusy(true);
            try
            {
                List<PowerPlan> plans = await GetPowerPlansAsync();
                ShowPlans(plans);

                PowerPlan active = plans.FirstOrDefault(p => p.IsActive);
                if (plans.Count == 0)
                    StatusText.Text = "電源プランを取得できませんでした。";
                else if (active != null)
                    StatusText.Text = $"現在:「{active.Name}」\n変更したいプランを選んでください。";
                else
                    StatusText.Text = "設定の取得完了:\n変更したいプランを選んでください。";

                return plans;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static async Task<List<PowerPlan>> GetPowerPlansAsync()
        {
            var (_, listOutput) = await RunPowercfgAsync("/list");
            List<PowerPlan> plans = ParsePlans(listOutput);

            // 使用中のプランは /getactivescheme で確かめる
            // （一覧に出てこない標準プランが使用中のこともあるため）
            var (_, activeOutput) = await RunPowercfgAsync("/getactivescheme");
            PowerPlan active = ParsePlans(activeOutput).FirstOrDefault();

            if (active != null)
            {
                plans = plans.Select(p => p with { IsActive = SameGuid(p.Guid, active.Guid) }).ToList();

                // 一覧に出てこないプランが使用中なら、一覧の最後に足して見えるようにする
                if (!plans.Any(p => p.IsActive))
                    plans.Add(active with { IsActive = true });
            }
            return plans;
        }

        // powercfg の出力から「GUID と名前」の行を全部取り出す
        private static List<PowerPlan> ParsePlans(string output)
        {
            var plans = new List<PowerPlan>();
            foreach (string line in output.Split('\n'))
            {
                Match m = PlanLineRegex.Match(line);
                if (!m.Success) continue;

                plans.Add(new PowerPlan(
                    Guid: m.Groups[1].Value,
                    Name: m.Groups[2].Value.Trim(),
                    IsActive: m.Groups[3].Success));
            }
            return plans;
        }

        private void ShowPlans(List<PowerPlan> plans)
        {
            // 使用中のプランを目立たせるスタイル（テーマに無ければ普通のボタンのまま）
            Style accentStyle = TryFindResource("AccentButtonStyle") as Style;

            // 1) PC に入っているプラン
            DynamicButtonBox.Children.Clear();
            foreach (PowerPlan plan in plans)
            {
                Button button = CreatePlanButton(plan.IsActive ? $"✓ {plan.Name}" : plan.Name);
                PlanRisk risk = GetRisk(plan);

                if (risk != PlanRisk.Normal)
                    ApplyPerformanceLook(button, plan.IsActive);   // 高パフォーマンス系は薄赤色
                else if (plan.IsActive && accentStyle != null)
                    button.Style = accentStyle;                    // それ以外の使用中プランは強調色

                button.Click += async (sender, e) => await ActivatePlanAsync(plan);
                DynamicButtonBox.Children.Add(button);
            }

            // 2) 一覧に出てこない標準プラン
            StandardPlanButtonBox.Children.Clear();
            foreach (StandardPlan std in StandardPlans)
            {
                if (plans.Any(p => IsSamePlan(p, std))) continue; // 一覧にあるものは出さない

                Button button = CreatePlanButton(std.DisplayName);
                if (std.Risk != PlanRisk.Normal)
                    ApplyPerformanceLook(button, false);

                button.Click += async (sender, e) => await ActivateStandardPlanAsync(std);
                StandardPlanButtonBox.Children.Add(button);
            }
            StandardPlanSection.Visibility = StandardPlanButtonBox.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            // ボタンの数が変わったので高さを中身に合わせ直す
            // （手でサイズを変えると WPF が自動調整を切るので、毎回もとに戻す）
            SizeToContent = SizeToContent.Height;
        }

        private static Button CreatePlanButton(string text)
        {
            return new Button
            {
                // 長いプラン名は「…」で省略し、マウスを乗せると全文が出る
                Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = text,
                Width = 260,
                Height = 45,
                Margin = new Thickness(10, 6, 10, 6),
            };
        }

        // ───────── ボタンを押したときの処理 ─────────

        // 一覧にあるプランに切り替える
        private async Task ActivatePlanAsync(PowerPlan plan)
        {
            PlanRisk risk = GetRisk(plan);

            // ノートパソコンで高パフォーマンス系を選んだら、切り替える前に確認する
            // （すでに使用中のプランを押し直したときは聞かない）
            if (_isLaptop && risk != PlanRisk.Normal && !plan.IsActive && !ConfirmOnLaptop(plan.Name, risk))
            {
                StatusText.Text = $"「{plan.Name}」への変更をやめました。";
                return;
            }

            SetBusy(true);
            StatusText.Text = $"「{plan.Name}」に切り替え中...";

            var (ok, output) = await TrySetActiveAsync(plan.Guid);

            await RefreshAsync();
            ShowResult(plan.Name, risk, ok, output);
        }

        // 一覧に出てこない標準プランに切り替える
        private async Task ActivateStandardPlanAsync(StandardPlan std)
        {
            if (_isLaptop && std.Risk != PlanRisk.Normal && !ConfirmOnLaptop(std.DisplayName, std.Risk))
            {
                StatusText.Text = $"「{std.DisplayName}」への変更をやめました。";
                return;
            }

            SetBusy(true);
            StatusText.Text = $"「{std.DisplayName}」に切り替え中...";

            // 1) Windows に元からある標準プランを、GUID を指定して直接使う
            var (ok, output) = await TrySetActiveAsync(std.Guid);

            // 2) だめなら、以前このアプリが作った複製を使う
            if (!ok)
                (ok, _) = await TrySetActiveAsync(std.CopyGuid);

            // 3) それも無ければ、標準プランを複製して作ってから切り替える
            //    （まず普通に、だめなら管理者権限で。UAC の確認画面が出ます）
            if (!ok)
            {
                string command = $"/duplicatescheme {std.Guid} {std.CopyGuid}";
                (_, output) = await RunPowercfgAsync(command);
                (ok, _) = await TrySetActiveAsync(std.CopyGuid);

                if (!ok)
                {
                    bool accepted;
                    (accepted, output) = await RunPowercfgAsAdminAsync(command);
                    if (!accepted)
                    {
                        await RefreshAsync();
                        StatusText.Text = "管理者権限の確認がキャンセルされたため、切り替えませんでした。";
                        return;
                    }
                    (ok, _) = await TrySetActiveAsync(std.CopyGuid);
                }
            }

            await RefreshAsync();
            ShowResult(std.DisplayName, std.Risk, ok, output);
        }

        private void ShowResult(string name, PlanRisk risk, bool ok, string output)
        {
            if (!ok)
                StatusText.Text = $"「{name}」に変更できませんでした。\n" +
                                  (string.IsNullOrWhiteSpace(output) ? "（powercfg からのメッセージはありませんでした）" : output);
            else if (_isLaptop && risk != PlanRisk.Normal)
                StatusText.Text = $"設定完了:「{name}」に変更しました!\n使い終わったらバランスに戻してください。";
            else
                StatusText.Text = $"設定完了:「{name}」に変更しました!";
        }

        // ノートパソコン用の確認。究極のパフォーマンスは2回確認する。続けてよければ true
        private bool ConfirmOnLaptop(string name, PlanRisk risk)
        {
            MessageBoxResult first = MessageBox.Show(this,
                $"この PC はノートパソコンと判定されました。\n" +
                $"「{name}」はノートパソコンでは推奨されていません。\n\n" +
                "長時間使うと、発熱による部品の故障やバッテリーの消耗につながります。\n" +
                "それでも変更しますか？",
                "ノートパソコンでの使用確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (first != MessageBoxResult.Yes) return false;
            if (risk != PlanRisk.Extreme) return true;

            MessageBoxResult second = MessageBox.Show(this,
                "「究極のパフォーマンス」は、ノートパソコンでは使わないことを強くおすすめします。\n\n" +
                "本当に変更しますか？",
                "最終確認",
                MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No);

            return second == MessageBoxResult.Yes;
        }

        // ───────── powercfg の実行 ─────────

        // プランを切り替えて、本当に切り替わったかを /getactivescheme で確かめる
        private static async Task<(bool Ok, string Output)> TrySetActiveAsync(string guid)
        {
            var (_, output) = await RunPowercfgAsync($"/setactive {guid}");
            string activeGuid = await GetActiveGuidAsync();
            return (activeGuid != null && SameGuid(activeGuid, guid), output);
        }

        // 今使用中のプランの GUID。分からなければ null
        private static async Task<string> GetActiveGuidAsync()
        {
            var (_, output) = await RunPowercfgAsync("/getactivescheme");
            Match m = GuidRegex.Match(output);
            return m.Success ? m.Value : null;
        }

        // 普通に実行して、終了コードと出力を受け取る
        private static async Task<(int ExitCode, string Output)> RunPowercfgAsync(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PowercfgPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = ConsoleEncoding,
                StandardErrorEncoding = ConsoleEncoding,
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // 出力とエラー出力を同時に読む（片方だけ待つと止まることがある）
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string output = (await stdoutTask) + (await stderrTask);
            return (process.ExitCode, output.Trim());
        }

        // 管理者として実行する（UAC の確認が出る）。
        // Accepted: UAC で「はい」が押されたか / Output: powercfg のメッセージ
        private static async Task<(bool Accepted, string Output)> RunPowercfgAsAdminAsync(string arguments)
        {
            // 管理者で動かすと出力を直接受け取れないので、cmd 経由でいったんファイルに書き出してもらう
            string outFile = Path.Combine(Path.GetTempPath(), "PowerChanger_" + Path.GetRandomFileName() + ".txt");

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/c \"\"{PowercfgPath}\" {arguments} > \"{outFile}\" 2>&1\"",
                UseShellExecute = true,   // runas を使うには true が必要
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                using Process process = Process.Start(startInfo);
                if (process == null) return (false, "");
                await process.WaitForExitAsync();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // 1223 = ユーザーがキャンセル
            {
                return (false, "");
            }

            try
            {
                string output = File.Exists(outFile) ? File.ReadAllText(outFile, ConsoleEncoding).Trim() : "";
                File.Delete(outFile);
                return (true, output);
            }
            catch (Exception)
            {
                return (true, "");
            }
        }

        private static Encoding CreateConsoleEncoding()
        {
            // 日本語版 Windows の powercfg は Shift_JIS で出力するので、それに合わせて読む
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("Shift_JIS");
        }

        // ───────── 小さな部品 ─────────

        private void SetBusy(bool busy)
        {
            // 処理中はボタンを押せないようにする（連打対策）
            DynamicButtonBox.IsEnabled = !busy;
            StandardPlanSection.IsEnabled = !busy;
            ReloadButton.IsEnabled = !busy;
        }

        private void CenterOnScreen()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + (area.Height - ActualHeight) / 2;
        }

        // 高パフォーマンス系のボタンを薄赤色にする（色は MainWindow.xaml の Window.Resources で変更可）
        private void ApplyPerformanceLook(Button button, bool isActive)
        {
            Brush foreground = (Brush)FindResource("PerformanceButtonForeground");
            button.Background = (Brush)FindResource("PerformanceButtonBackground");
            button.Foreground = foreground;
            button.ToolTip = $"{button.ToolTip}\n{LaptopWarningText}";

            if (isActive)
            {
                // 使用中なら枠を濃くして分かるようにする
                button.BorderBrush = foreground;
                button.BorderThickness = new Thickness(2);
            }
        }

        // 標準プランと一致すればその強さ、一致しなければ Normal
        private static PlanRisk GetRisk(PowerPlan plan) =>
            StandardPlans.FirstOrDefault(std => IsSamePlan(plan, std))?.Risk ?? PlanRisk.Normal;

        private static bool SameGuid(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // 標準プランかどうか（標準の GUID・アプリが作った複製の GUID・名前のどれかで判定）
        private static bool IsSamePlan(PowerPlan plan, StandardPlan std) =>
            SameGuid(plan.Guid, std.Guid) ||
            SameGuid(plan.Guid, std.CopyGuid) ||
            std.KnownNames.Any(n => string.Equals(n, plan.Name, StringComparison.OrdinalIgnoreCase));
    }
}
