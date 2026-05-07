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

            try
            {
                await Task.Run(() =>
                {
                    // 1. 准备 manifest（新建：空 manifest；续传：复用 ProbeManifest 加载的）
                    if (currentManifest == null)
                    {
                        currentManifest = new SubtitleBatchManifest
                        {
                            Mid = Mid,
                            UpName = UpName,
                            TabId = TabId,
                            TabName = TabName,
                            Total = TotalCount
                        };
                    }
                    else
                    {
                        // 续传：把"最近一次跑的分类"同步到 manifest（增量下载 manifest 跨分类汇总，
                        // 这些字段只记录最后一次的入口）
                        currentManifest.UpName = UpName;
                        currentManifest.TabId = TabId;
                        currentManifest.TabName = TabName;
                        currentManifest.Total = TotalCount;
                    }

                    // 2. 拉列表并按 bvid 增量合并（已有跳过、新条目 append 为 pending）
                    FetchAndMergePublicationList(currentManifest, token);
                    if (token.IsCancellationRequested)
                    {
                        CurrentTitle = DictionaryResource.GetString("SubtitleBatchStopped");
                        LogManager.Debug(Tag, "拉列表阶段被取消");
                        return;
                    }

                    // 3. 进入字幕下载主循环
                    DownloadSubtitlesLoop(currentManifest, token);
                }, token);
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
        /// - 存在且 mid 匹配：进入续传模式（不分 tabId，做增量——同一 UP 主所有分类共享 manifest）
        /// - 存在但 mid 不匹配：弹错误（用户手动选错目录的情况）
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

            // 校验 UP 主身份（增量下载只看 mid，分类可以混跑）
            if (loaded.Mid != Mid)
            {
                LogManager.Debug(Tag, $"目录不匹配 manifest.mid={loaded.Mid} vs 当前 mid={Mid}");
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
        /// 拉投稿列表并按 bvid 增量合并到 manifest：
        /// - 已存在的 bvid：保留原 SubtitleBatchItem（status / Files / Attempts 等）
        /// - 新 bvid：append 为 pending 条目
        /// 末页判定：单页返回少于 PageSize 即认为到尾。
        /// 适用于新建（空 manifest）和续传（已有 manifest）两种场景。
        /// </summary>
        private void FetchAndMergePublicationList(SubtitleBatchManifest manifest, CancellationToken token)
        {
            var existingBvids = new HashSet<string>();
            foreach (var it in manifest.Items)
            {
                if (!string.IsNullOrEmpty(it.Bvid)) { existingBvids.Add(it.Bvid); }
            }
            bool isResume = existingBvids.Count > 0;
            int newCount = 0;
            LogManager.Debug(Tag, $"开始拉列表 mid={Mid} tabId={TabId} mode={(isResume ? "增量" : "全量")} 已有={existingBvids.Count} dir={OutputDirectory}");

            int pageNum = 1;
            while (!token.IsCancellationRequested)
            {
                CurrentBvid = string.Empty;
                CurrentTitle = isResume
                    ? $"检查更新 第 {pageNum} 页"
                    : $"正在拉取列表 第 {pageNum} 页";
                LogManager.Debug(Tag, $"拉取第 {pageNum} 页");

                SpacePublicationList pubList = null;
                try
                {
                    pubList = Core.BiliApi.Users.UserSpace.GetPublication(Mid, pageNum, PageSize, TabId);
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }

                int gotCount = pubList?.Vlist?.Count ?? 0;
                LogManager.Debug(Tag, $"第 {pageNum} 页返回 {gotCount} 条");

                if (gotCount == 0) { break; }

                int pageNew = 0;
                foreach (var v in pubList.Vlist)
                {
                    if (existingBvids.Contains(v.Bvid)) { continue; }
                    manifest.Items.Add(new SubtitleBatchItem
                    {
                        Bvid = v.Bvid,
                        Title = v.Title,
                        Created = v.Created,
                        Status = "pending"
                    });
                    existingBvids.Add(v.Bvid);
                    newCount++;
                    pageNew++;
                }
                LogManager.Debug(Tag, $"第 {pageNum} 页新增 {pageNew} 条（累计新增 {newCount}）");

                // 每页落盘一次，中途停止已拉部分不丢
                try
                {
                    SubtitleBatchManifestStore.Save(OutputDirectory, manifest);
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }

                // 末页判定
                if (gotCount < PageSize) { break; }

                pageNum++;
                try { Task.Delay(IntervalMs, token).Wait(token); }
                catch (OperationCanceledException) { break; }
            }

            // 同步 manifest 元信息与 UI（增量后总数可能变大）
            manifest.Total = manifest.Items.Count;
            TotalCount = manifest.Items.Count;
            ProgressTotal = manifest.Items.Count;
            RecountStats(manifest);
            LogManager.Debug(Tag, $"列表拉取结束 items={manifest.Items.Count} 新增={newCount} ok={CountOk} noSub={CountNoSub} failed={CountFailed} pending={CountPending}");
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
                    int pageCount = view.Pages.Count;
                    bool isMultiPart = pageCount > 1;
                    item.IsMultiPart = isMultiPart;
                    LogManager.Debug(Tag, $"[{i + 1}/{total}] 视频信息 aid={aid} pages={pageCount}");

                    string datePrefix = item.Created > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(item.Created).LocalDateTime.ToString("yyyy.MM.dd") + "_"
                        : string.Empty;

                    var allFiles = new List<string>();
                    int pIndex = 0;
                    foreach (var page in view.Pages)
                    {
                        if (token.IsCancellationRequested) { break; }
                        pIndex++;
                        long cid = page.Cid;

                        // 注意：不能用 view.Subtitle.List 做预筛——B 站 view 接口 subtitle.list 经常为空，
                        // 即使视频实际有字幕。可靠来源是 PlayerV2 接口（GetSubtitle 内部走的就是它）。
                        var subRips = Core.BiliApi.VideoStream.VideoStream.GetSubtitle(aid, item.Bvid, cid);
                        int subCount = subRips?.Count ?? -1;
                        // 拉空时延迟重试 1 次：B 站对密集 PlayerV2 调用偶发返回空 subtitles。
                        if (subCount <= 0 && !token.IsCancellationRequested)
                        {
                            LogManager.Debug(Tag, $"[{i + 1}/{total}] {item.Bvid} P{pIndex} 第一次拉空，2s 后重试");
                            try { Task.Delay(2000, token).Wait(token); }
                            catch (OperationCanceledException) { throw; }
                            subRips = Core.BiliApi.VideoStream.VideoStream.GetSubtitle(aid, item.Bvid, cid);
                            subCount = subRips?.Count ?? -1;
                        }

                        // 仅保留中文字幕（zh-CN / zh-Hans / zh-Hant / ai-zh 等）
                        var chineseSubs = subRips == null
                            ? null
                            : subRips.FindAll(s => !string.IsNullOrEmpty(s.Lan) && s.Lan.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0);
                        int chineseCount = chineseSubs?.Count ?? 0;
                        LogManager.Debug(Tag, $"[{i + 1}/{total}] {item.Bvid} P{pIndex}/{pageCount} 字幕条数={subCount} 中文={chineseCount}");

                        if (chineseSubs == null || chineseSubs.Count == 0)
                        {
                            continue; // 这个 P 没字幕，看下一个 P
                        }

                        // 多 P 时文件名加 P 后缀；单 P 不加，保持简洁
                        string partSuffix = "";
                        if (isMultiPart)
                        {
                            string partPiece = string.IsNullOrEmpty(page.Part) ? "" : "_" + page.Part;
                            partSuffix = $"_P{pIndex}{partPiece}";
                        }

                        foreach (var sub in chineseSubs)
                        {
                            string fileName = Format.FormatFileName($"{datePrefix}{item.Bvid}_{item.Title}{partSuffix}_{sub.LanDoc}") + ".srt";
                            string fullPath = Path.Combine(OutputDirectory, fileName);
                            File.WriteAllText(fullPath, sub.SrtString);
                            allFiles.Add(fileName);
                            LogManager.Debug(Tag, $"[{i + 1}/{total}] 写入 {fileName}");
                        }

                        // P 间也限速，避免连续高频调用 PlayerV2
                        if (pIndex < pageCount && !token.IsCancellationRequested)
                        {
                            try { Task.Delay(IntervalMs, token).Wait(token); }
                            catch (OperationCanceledException) { throw; }
                        }
                    }

                    if (allFiles.Count > 0)
                    {
                        item.Files = allFiles;
                        item.Status = "done";
                        item.Error = null;
                    }
                    else
                    {
                        // 所有 P 都没拉到中文 → 探一次 PlayerV2 第一 P，记录原因
                        try
                        {
                            var probe = Core.BiliApi.VideoStream.VideoStream.PlayerV2(aid, item.Bvid, view.Pages[0].Cid);
                            if (probe == null)
                            {
                                LogManager.Debug(Tag, $"[{i + 1}/{total}] {item.Bvid} 探测：PlayerV2 返回 null");
                            }
                            else if (probe.Subtitle?.Subtitles == null || probe.Subtitle.Subtitles.Count == 0)
                            {
                                LogManager.Debug(Tag, $"[{i + 1}/{total}] {item.Bvid} 探测：Subtitles 为空");
                            }
                            else
                            {
                                LogManager.Debug(Tag, $"[{i + 1}/{total}] {item.Bvid} 探测：Subtitles.Count={probe.Subtitle.Subtitles.Count}");
                                foreach (var s in probe.Subtitle.Subtitles)
                                {
                                    LogManager.Debug(Tag, $"  Lan={s.Lan} LanDoc={s.LanDoc}");
                                }
                            }
                        }
                        catch (Exception probeEx) { LogManager.Error(Tag, probeEx); }

                        item.Status = "no_subtitle";
                        item.Files = null;
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

            // 同步更新 UP 主下载历史聚合索引（被取消和完成都更新，反映"最新一次操作"语义）
            UpdateUpDownloadIndex(manifest);

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

        private void UpdateUpDownloadIndex(SubtitleBatchManifest manifest)
        {
            if (manifest == null || manifest.Mid <= 0) { return; }
            int doneCount = 0;
            if (manifest.Items != null)
            {
                foreach (var it in manifest.Items)
                {
                    if (it != null && it.Status == "done") { doneCount++; }
                }
            }
            try
            {
                UpDownloadIndexStore.Upsert(
                    manifest.Mid,
                    manifest.UpName ?? string.Empty,
                    manifest.UpdatedAt ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    doneCount,
                    manifest.Items?.Count ?? 0);
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
        }

        /// <summary>
        /// 弹完成报告（已完成 / 已停止），显示 4 个统计计数。
        /// 后台循环线程调用；用 Dispatcher.BeginInvoke 异步派发到 UI 线程。
        /// 注意：用 WPF 原生 MessageBox.Show 而非 Prism dialogService.ShowDialog——
        /// 后者在"非模态字幕对话框 + 模态 AlertDialog"场景下会出 Owner / 焦点死锁，已踩坑两次。
        /// </summary>
        private void ShowCompletionReport(bool stopped)
        {
            string title = DictionaryResource.GetString(stopped ? "SubtitleBatchStopped" : "SubtitleBatchDone");
            var sb = new StringBuilder();
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusOk")}: {CountOk}");
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusNoSub")}: {CountNoSub}");
            sb.AppendLine($"{DictionaryResource.GetString("SubtitleBatchStatusFailed")}: {CountFailed}");
            sb.Append($"{DictionaryResource.GetString("SubtitleBatchStatusPending")}: {CountPending}");
            string message = sb.ToString();

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) { return; }

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    System.Windows.MessageBox.Show(
                        message,
                        title,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }
            }));
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

        public override void OnDialogClosed()
        {
            base.OnDialogClosed();
            // 关闭对话框时取消后台任务，避免窗口关了循环还在跑
            try { tokenSource?.Cancel(); } catch { }
        }

        private void RefreshTargetDisplay()
        {
            string upPart = string.IsNullOrEmpty(UpName) ? $"{Mid}" : $"{Mid}_{UpName}";
            TargetDisplay = $"{upPart} / {TabName} ({TotalCount} 条)";
        }

        /// <summary>
        /// 工作目录约定：{默认下载根}\{mid}_{upName}\
        /// 同一 UP 主所有分类共用一个目录 + 一个 manifest，做增量下载（已下过的跳过、新出现的追加）。
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
