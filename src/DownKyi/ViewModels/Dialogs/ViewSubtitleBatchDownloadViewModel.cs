using DownKyi.Core.BiliApi.Users.Models;
using DownKyi.Core.Logging;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage;
using DownKyi.Core.Utils;
using DownKyi.Services;
using DownKyi.Utils;
using Prism.Commands;
using Prism.Services.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DownKyi.ViewModels.Dialogs
{
    public class ViewSubtitleBatchDownloadViewModel : BaseDialogViewModel
    {
        public const string Tag = "DialogSubtitleBatchDownload";

        #region 页面属性申明

        // 任务信息
        private long mid;
        public long Mid
        {
            get => mid;
            set => SetProperty(ref mid, value);
        }

        private int tabId;
        public int TabId
        {
            get => tabId;
            set => SetProperty(ref tabId, value);
        }

        private string upName;
        public string UpName
        {
            get => upName;
            set
            {
                SetProperty(ref upName, value);
                RefreshTargetDisplay();
            }
        }

        private string tabName;
        public string TabName
        {
            get => tabName;
            set
            {
                SetProperty(ref tabName, value);
                RefreshTargetDisplay();
            }
        }

        private int totalCount;
        public int TotalCount
        {
            get => totalCount;
            set
            {
                SetProperty(ref totalCount, value);
                RefreshTargetDisplay();
            }
        }

        private string targetDisplay;
        public string TargetDisplay
        {
            get => targetDisplay;
            set => SetProperty(ref targetDisplay, value);
        }

        private string outputDirectory;
        public string OutputDirectory
        {
            get => outputDirectory;
            set => SetProperty(ref outputDirectory, value);
        }

        // 进度
        private int progressCurrent;
        public int ProgressCurrent
        {
            get => progressCurrent;
            set
            {
                SetProperty(ref progressCurrent, value);
                RefreshProgressDisplay();
            }
        }

        private int progressTotal;
        public int ProgressTotal
        {
            get => progressTotal;
            set
            {
                SetProperty(ref progressTotal, value);
                RefreshProgressDisplay();
            }
        }

        private string progressDisplay;
        public string ProgressDisplay
        {
            get => progressDisplay;
            set => SetProperty(ref progressDisplay, value);
        }

        private string progressPercent;
        public string ProgressPercent
        {
            get => progressPercent;
            set => SetProperty(ref progressPercent, value);
        }

        private string currentBvid;
        public string CurrentBvid
        {
            get => currentBvid;
            set => SetProperty(ref currentBvid, value);
        }

        private string currentTitle;
        public string CurrentTitle
        {
            get => currentTitle;
            set => SetProperty(ref currentTitle, value);
        }

        // 统计
        private int countOk;
        public int CountOk
        {
            get => countOk;
            set => SetProperty(ref countOk, value);
        }

        private int countNoSub;
        public int CountNoSub
        {
            get => countNoSub;
            set => SetProperty(ref countNoSub, value);
        }

        private int countFailed;
        public int CountFailed
        {
            get => countFailed;
            set => SetProperty(ref countFailed, value);
        }

        private int countPending;
        public int CountPending
        {
            get => countPending;
            set => SetProperty(ref countPending, value);
        }

        // 按钮状态
        private bool canStart = true;
        public bool CanStart
        {
            get => canStart;
            set => SetProperty(ref canStart, value);
        }

        private bool canStop;
        public bool CanStop
        {
            get => canStop;
            set => SetProperty(ref canStop, value);
        }

        private string startButtonText;
        public string StartButtonText
        {
            get => startButtonText;
            set => SetProperty(ref startButtonText, value);
        }

        // 续传提示文字 + 可见性
        private string resumeHint;
        public string ResumeHint
        {
            get => resumeHint;
            set => SetProperty(ref resumeHint, value);
        }

        private Visibility resumeHintVisibility = Visibility.Collapsed;
        public Visibility ResumeHintVisibility
        {
            get => resumeHintVisibility;
            set => SetProperty(ref resumeHintVisibility, value);
        }

        #endregion

        // 后台任务取消
        private CancellationTokenSource tokenSource;
        // 当前任务的 manifest（运行期间维持引用；探测到续传时也提前赋值）
        private SubtitleBatchManifest currentManifest;
        // 每页拉取条数（B 站投稿接口上限 50）
        private const int PageSize = 50;
        // 接口间隔
        private const int IntervalMs = 300;

        private readonly IDialogService dialogService;

        public ViewSubtitleBatchDownloadViewModel(IDialogService dialogService)
        {
            this.dialogService = dialogService;

            Title = DictionaryResource.GetString("SubtitleBatchTitle");
            StartButtonText = DictionaryResource.GetString("SubtitleBatchStart");
            ProgressDisplay = "0 / 0";
            ProgressPercent = "0%";
            CurrentBvid = string.Empty;
            CurrentTitle = string.Empty;
            OutputDirectory = string.Empty;
            UpName = string.Empty;
            TabName = string.Empty;
            ResumeHint = string.Empty;
        }

        #region 命令申明

        // 选择目录
        private DelegateCommand selectDirCommand;
        public DelegateCommand SelectDirCommand => selectDirCommand ?? (selectDirCommand = new DelegateCommand(ExecuteSelectDirCommand));

        private void ExecuteSelectDirCommand()
        {
            string dir = DialogUtils.SetDownloadDirectory();
            if (!string.IsNullOrEmpty(dir))
            {
                OutputDirectory = dir;
                ProbeManifest();
            }
        }

        // 开始 / 继续
        private DelegateCommand startCommand;
        public DelegateCommand StartCommand => startCommand ?? (startCommand = new DelegateCommand(ExecuteStartCommand));

        private async void ExecuteStartCommand()
        {
            if (string.IsNullOrEmpty(OutputDirectory))
            {
                LogManager.Debug(Tag, "OutputDirectory 为空，无法开始");
                return;
            }

            CanStart = false;
            CanStop = true;
            tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            bool resume = currentManifest != null;
            try
            {
                if (resume)
                {
                    LogManager.Debug(Tag, $"续传：复用 manifest items={currentManifest.Items.Count}");
                    await Task.Run(() => DownloadSubtitlesLoop(currentManifest, token), token);
                }
                else
                {
                    await Task.Run(() => FetchAllAndStartDownload(token), token);
                }
            }
            catch (OperationCanceledException)
            {
                // 用户主动停止
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
            finally
            {
                CanStart = true;
                CanStop = false;
                tokenSource?.Dispose();
                tokenSource = null;
            }
        }

        /// <summary>
        /// 探测 OutputDirectory 下的 manifest：
        /// - 不存在：清空续传状态，按"开始"模式
        /// - 存在且 mid+tabId 匹配：进入续传模式（按钮文字"继续"，提示行可见，统计/进度同步到已落盘状态）
        /// - 存在但 mid+tabId 不匹配：弹错误并禁用开始按钮（用户需重选目录）
        /// </summary>
        private void ProbeManifest()
        {
            // 默认置回"新任务"状态
            currentManifest = null;
            StartButtonText = DictionaryResource.GetString("SubtitleBatchStart");
            ResumeHint = string.Empty;
            ResumeHintVisibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(OutputDirectory) || !Directory.Exists(OutputDirectory))
            {
                CanStart = true;
                return;
            }

            SubtitleBatchManifest loaded = null;
            try
            {
                loaded = SubtitleBatchManifestStore.Load(OutputDirectory);
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
                // 解析失败留给步骤 8 处理；本步骤先不阻塞，按新任务走
                CanStart = true;
                return;
            }

            if (loaded == null)
            {
                CanStart = true;
                return;
            }

            // 校验任务身份
            if (loaded.Mid != Mid || loaded.TabId != TabId)
            {
                LogManager.Debug(Tag, $"目录不匹配 manifest.mid={loaded.Mid} tabId={loaded.TabId} vs 当前 mid={Mid} tabId={TabId}");
                CanStart = false;
                ResumeHint = DictionaryResource.GetString("SubtitleBatchDirMismatch");
                ResumeHintVisibility = Visibility.Visible;
                ShowAlert(DictionaryResource.GetString("SubtitleBatchDirMismatch"), AlertKind.Warning);
                return;
            }

            // 命中续传
            currentManifest = loaded;
            ProgressTotal = loaded.Items.Count;
            RecountStats(loaded);
            // 进度条对齐到已处理（done + no_subtitle + failed）的数量
            int processed = 0;
            foreach (var it in loaded.Items)
            {
                if (it.Status == "done" || it.Status == "no_subtitle" || it.Status == "failed")
                {
                    processed++;
                }
            }
            ProgressCurrent = processed;
            StartButtonText = DictionaryResource.GetString("SubtitleBatchContinue");
            ResumeHint = DictionaryResource.GetString("SubtitleBatchResumeFound");
            ResumeHintVisibility = Visibility.Visible;
            CanStart = true;
            LogManager.Debug(Tag, $"探测到续传 manifest：items={loaded.Items.Count} 已处理={processed} ok={CountOk} noSub={CountNoSub} failed={CountFailed} pending={CountPending}");
        }

        /// <summary>
        /// 拉全量投稿列表，按页落盘到 manifest，完成后进入下载循环（下载循环步骤 6 实现）。
        /// </summary>
        private void FetchAllAndStartDownload(CancellationToken token)
        {
            int totalPages = TotalCount > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 1;
            LogManager.Debug(Tag, $"开始拉列表 mid={Mid} tabId={TabId} total={TotalCount} pages={totalPages} dir={OutputDirectory}");

            var manifest = new SubtitleBatchManifest
            {
                Mid = Mid,
                UpName = UpName,
                TabId = TabId,
                TabName = TabName,
                Total = TotalCount
            };
            currentManifest = manifest;

            for (int page = 1; page <= totalPages; page++)
            {
                if (token.IsCancellationRequested) { break; }

                CurrentBvid = string.Empty;
                CurrentTitle = $"正在拉取列表 {page}/{totalPages}";
                LogManager.Debug(Tag, $"拉取第 {page}/{totalPages} 页");

                SpacePublicationList pubList = null;
                try
                {
                    pubList = Core.BiliApi.Users.UserSpace.GetPublication(Mid, page, PageSize, TabId);
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }

                int gotCount = pubList?.Vlist?.Count ?? 0;
                LogManager.Debug(Tag, $"第 {page} 页返回 {gotCount} 条");

                if (pubList?.Vlist != null)
                {
                    foreach (var v in pubList.Vlist)
                    {
                        manifest.Items.Add(new SubtitleBatchItem
                        {
                            Bvid = v.Bvid,
                            Title = v.Title,
                            Created = v.Created,
                            Status = "pending"
                        });
                    }
                }

                // 每页落盘一次，中途停止已拉部分不丢
                try
                {
                    SubtitleBatchManifestStore.Save(OutputDirectory, manifest);
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }

                if (page < totalPages)
                {
                    try { Task.Delay(IntervalMs, token).Wait(token); }
                    catch (OperationCanceledException) { break; }
                }
            }

            // 同步统计 / 进度（基于已拉到的条目）
            ProgressTotal = manifest.Items.Count;
            RecountStats(manifest);
            LogManager.Debug(Tag, $"列表全部拉完，items={manifest.Items.Count}");

            if (token.IsCancellationRequested)
            {
                CurrentTitle = DictionaryResource.GetString("SubtitleBatchStopped");
                LogManager.Debug(Tag, "拉列表阶段被取消");
                return;
            }

            // 进入字幕下载主循环
            DownloadSubtitlesLoop(manifest, token);
        }

        /// <summary>
        /// 字幕下载主循环：按顺序处理 manifest.Items，每条结束立即 Save manifest。
        /// </summary>
        private void DownloadSubtitlesLoop(SubtitleBatchManifest manifest, CancellationToken token)
        {
            int total = manifest.Items.Count;
            LogManager.Debug(Tag, $"进入下载循环，共 {total} 条");

            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested) { break; }

                ProgressCurrent = i + 1;
                var item = manifest.Items[i];
                if (item.Status == "done" || item.Status == "no_subtitle")
                {
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 跳过 ({item.Status}) {item.Bvid}");
                    continue;
                }

                CurrentBvid = item.Bvid;
                CurrentTitle = item.Title;
                LogManager.Debug(Tag, $"[{i + 1}/{total}] 开始 {item.Bvid} | {item.Title}");

                try
                {
                    var view = Core.BiliApi.Video.VideoInfo.VideoViewInfo(item.Bvid);
                    if (view == null || view.Pages == null || view.Pages.Count == 0)
                    {
                        throw new InvalidOperationException("视频信息获取失败");
                    }

                    long aid = view.Aid;
                    long cid = view.Pages[0].Cid;
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 视频信息 aid={aid} cid={cid}");

                    var subRips = Core.BiliApi.VideoStream.VideoStream.GetSubtitle(aid, item.Bvid, cid);
                    int subCount = subRips?.Count ?? -1;
                    // 仅保留中文字幕（zh-CN / zh-Hans / zh-Hant / ai-zh 等）
                    var chineseSubs = subRips == null
                        ? null
                        : subRips.FindAll(s => !string.IsNullOrEmpty(s.Lan) && s.Lan.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0);
                    int chineseCount = chineseSubs?.Count ?? 0;
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 字幕条数 {subCount} 中文 {chineseCount}");

                    if (chineseSubs == null || chineseSubs.Count == 0)
                    {
                        item.Status = "no_subtitle";
                        item.Files = null;
                        item.Error = null;
                    }
                    else
                    {
                        string datePrefix = item.Created > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(item.Created).LocalDateTime.ToString("yyyy.MM.dd") + "_"
                            : string.Empty;
                        var files = new List<string>();
                        foreach (var sub in chineseSubs)
                        {
                            string fileName = Format.FormatFileName($"{datePrefix}{item.Title}_{sub.LanDoc}") + ".srt";
                            string fullPath = Path.Combine(OutputDirectory, fileName);
                            File.WriteAllText(fullPath, sub.SrtString);
                            files.Add(fileName);
                            LogManager.Debug(Tag, $"[{i + 1}/{total}] 写入 {fileName}");
                        }
                        item.Files = files;
                        item.Status = "done";
                        item.Error = null;
                    }
                }
                catch (Exception e)
                {
                    item.Status = "failed";
                    item.Error = e.Message;
                    LogManager.Error(Tag, e);
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 失败 {item.Bvid}: {e.Message}");
                }
                finally
                {
                    item.Attempts++;
                    try { SubtitleBatchManifestStore.Save(OutputDirectory, manifest); }
                    catch (Exception e) { LogManager.Error(Tag, e); }
                    RecountStats(manifest);
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 结束 status={item.Status} | ok={CountOk} noSub={CountNoSub} failed={CountFailed} pending={CountPending}");
                }

                if (i < total - 1)
                {
                    try { Task.Delay(IntervalMs, token).Wait(token); }
                    catch (OperationCanceledException) { break; }
                }
            }

            if (token.IsCancellationRequested)
            {
                CurrentTitle = DictionaryResource.GetString("SubtitleBatchStopped");
                LogManager.Debug(Tag, $"下载循环被取消 | ok={CountOk} noSub={CountNoSub} failed={CountFailed} pending={CountPending}");
                ShowCompletionReport(stopped: true);
                return;
            }

            CurrentTitle = DictionaryResource.GetString("SubtitleBatchDone");
            LogManager.Debug(Tag, $"下载循环完成 | ok={CountOk} noSub={CountNoSub} failed={CountFailed} pending={CountPending}");
            ShowCompletionReport(stopped: false);
        }

        /// <summary>
        /// 弹完成报告（已完成 / 已停止），显示 4 个统计计数。
        /// 在循环线程上调用；AlertService 内部会 Dispatcher.Invoke 切到 UI 线程。
        /// </summary>
        private void ShowCompletionReport(bool stopped)
        {
            string title = DictionaryResource.GetString(stopped ? "SubtitleBatchStopped" : "SubtitleBatchDone");
            var sb = new StringBuilder();
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusOk")}: {CountOk}");
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusNoSub")}: {CountNoSub}");
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusFailed")}: {CountFailed}");
            sb.Append($"{DictionaryResource.GetString("SubtitleBatchStatusPending")}: {CountPending}");

            try
            {
                if (dialogService == null) { return; }
                var alert = new AlertService(dialogService);
                alert.ShowMessage(Images.SystemIcon.Instance().Info, title, sb.ToString(), 1);
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
        }

        private enum AlertKind { Info, Warning, Error }

        private void ShowAlert(string message, AlertKind kind)
        {
            try
            {
                if (dialogService == null) { return; }
                var alert = new AlertService(dialogService);
                switch (kind)
                {
                    case AlertKind.Warning: alert.ShowWarning(message); break;
                    case AlertKind.Error: alert.ShowError(message); break;
                    default: alert.ShowInfo(message, 1); break;
                }
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
        }

        /// <summary>
        /// 按 manifest.Items 的 status 重新统计 4 个计数。
        /// </summary>
        private void RecountStats(SubtitleBatchManifest manifest)
        {
            int ok = 0, noSub = 0, failed = 0, pending = 0;
            foreach (var it in manifest.Items)
            {
                switch (it.Status)
                {
                    case "done": ok++; break;
                    case "no_subtitle": noSub++; break;
                    case "failed": failed++; break;
                    default: pending++; break;
                }
            }
            CountOk = ok;
            CountNoSub = noSub;
            CountFailed = failed;
            CountPending = pending;
        }

        // 停止
        private DelegateCommand stopCommand;
        public DelegateCommand StopCommand => stopCommand ?? (stopCommand = new DelegateCommand(ExecuteStopCommand));

        private void ExecuteStopCommand()
        {
            tokenSource?.Cancel();
        }

        // 打开目录
        private DelegateCommand openDirCommand;
        public DelegateCommand OpenDirCommand => openDirCommand ?? (openDirCommand = new DelegateCommand(ExecuteOpenDirCommand));

        private void ExecuteOpenDirCommand()
        {
            if (string.IsNullOrEmpty(OutputDirectory)) { return; }
            if (!Directory.Exists(OutputDirectory)) { return; }
            try
            {
                Process.Start("explorer.exe", OutputDirectory);
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
        }

        // 查看报告
        private DelegateCommand openReportCommand;
        public DelegateCommand OpenReportCommand => openReportCommand ?? (openReportCommand = new DelegateCommand(ExecuteOpenReportCommand));

        private void ExecuteOpenReportCommand()
        {
            if (string.IsNullOrEmpty(OutputDirectory)) { return; }
            string path = SubtitleBatchManifestStore.PathOf(OutputDirectory);
            if (!File.Exists(path))
            {
                LogManager.Debug(Tag, $"manifest 文件不存在：{path}");
                return;
            }
            try
            {
                Process.Start("notepad.exe", path);
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
        }

        #endregion

        public override void OnDialogOpened(IDialogParameters parameters)
        {
            base.OnDialogOpened(parameters);

            if (parameters == null) { return; }

            Mid = parameters.GetValue<long>("mid");
            TabId = parameters.GetValue<int>("tabId");
            UpName = parameters.GetValue<string>("upName") ?? string.Empty;
            TabName = parameters.GetValue<string>("tabName") ?? string.Empty;
            TotalCount = parameters.GetValue<int>("totalCount");

            ProgressTotal = TotalCount;
            CountPending = TotalCount;

            // 默认工作目录：{默认下载根}\{mid}_{upName}\，未存在则建
            if (string.IsNullOrEmpty(OutputDirectory))
            {
                string dir = ComputeWorkingDirectory();
                try
                {
                    if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }
                OutputDirectory = dir;
            }

            // 探测目录下是否已有 manifest，命中则进入续传模式
            ProbeManifest();
        }

        private void RefreshTargetDisplay()
        {
            string upPart = string.IsNullOrEmpty(UpName) ? $"{Mid}" : $"{Mid}_{UpName}";
            TargetDisplay = $"{upPart} / {TabName} ({TotalCount} 条)";
        }

        /// <summary>
        /// 工作目录约定：{默认下载根}\{mid}_{upName}\，无 upName 时退回 {mid}\
        /// </summary>
        private string ComputeWorkingDirectory()
        {
            string root = SettingsManager.GetInstance().GetSaveVideoRootPath();
            string folderName = string.IsNullOrEmpty(UpName)
                ? Mid.ToString()
                : $"{Mid}_{Format.FormatFileName(UpName)}";
            return Path.Combine(root, folderName);
        }

        private void RefreshProgressDisplay()
        {
            ProgressDisplay = $"{ProgressCurrent} / {ProgressTotal}";
            int percent = ProgressTotal > 0 ? (int)(100.0 * ProgressCurrent / ProgressTotal) : 0;
            ProgressPercent = $"{percent}%";
        }

    }
}
