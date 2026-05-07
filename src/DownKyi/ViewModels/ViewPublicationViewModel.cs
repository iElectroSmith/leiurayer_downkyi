using DownKyi.Core.BiliApi.VideoStream;
using DownKyi.Core.Logging;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage;
using DownKyi.Core.Utils;
using DownKyi.CustomControl;
using DownKyi.Events;
using DownKyi.Images;
using DownKyi.Services;
using DownKyi.Services.Download;
using DownKyi.Utils;
using DownKyi.ViewModels.Dialogs;
using DownKyi.ViewModels.PageViewModels;
using DownKyi.ViewModels.UserSpace;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prism.Services.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DownKyi.ViewModels
{
    public class ViewPublicationViewModel : BaseViewModel
    {
        public const string Tag = "PagePublication";

        private CancellationTokenSource tokenSource;

        private long mid = -1;
        private string upName = string.Empty;

        // 每页视频数量，暂时在此写死，以后在设置中增加选项
        private readonly int VideoNumberInPage = 50;

        #region 页面属性申明

        private string pageName = Tag;
        public string PageName
        {
            get => pageName;
            set => SetProperty(ref pageName, value);
        }

        private GifImage loading;
        public GifImage Loading
        {
            get => loading;
            set => SetProperty(ref loading, value);
        }

        private Visibility loadingVisibility;
        public Visibility LoadingVisibility
        {
            get => loadingVisibility;
            set => SetProperty(ref loadingVisibility, value);
        }

        private Visibility noDataVisibility;
        public Visibility NoDataVisibility
        {
            get => noDataVisibility;
            set => SetProperty(ref noDataVisibility, value);
        }

        private VectorImage arrowBack;
        public VectorImage ArrowBack
        {
            get => arrowBack;
            set => SetProperty(ref arrowBack, value);
        }

        private VectorImage downloadManage;
        public VectorImage DownloadManage
        {
            get => downloadManage;
            set => SetProperty(ref downloadManage, value);
        }

        private ObservableCollection<TabHeader> tabHeaders;
        public ObservableCollection<TabHeader> TabHeaders
        {
            get => tabHeaders;
            set => SetProperty(ref tabHeaders, value);
        }

        private int selectTabId;
        public int SelectTabId
        {
            get => selectTabId;
            set => SetProperty(ref selectTabId, value);
        }

        private bool isEnabled = true;
        public bool IsEnabled
        {
            get => isEnabled;
            set => SetProperty(ref isEnabled, value);
        }

        private CustomPagerViewModel pager;
        public CustomPagerViewModel Pager
        {
            get => pager;
            set => SetProperty(ref pager, value);
        }

        private ObservableCollection<PublicationMedia> medias;
        public ObservableCollection<PublicationMedia> Medias
        {
            get => medias;
            set => SetProperty(ref medias, value);
        }

        private bool isSelectAll;
        public bool IsSelectAll
        {
            get => isSelectAll;
            set => SetProperty(ref isSelectAll, value);
        }

        // 当前页"还有 N 个未下载"label，全部已下载时为空字符串
        private string undownloadedCountText;
        public string UndownloadedCountText
        {
            get => undownloadedCountText;
            set => SetProperty(ref undownloadedCountText, value);
        }

        #endregion

        public ViewPublicationViewModel(IEventAggregator eventAggregator, IDialogService dialogService) : base(eventAggregator)
        {
            this.dialogService = dialogService;

            #region 属性初始化

            // 初始化loading gif
            Loading = new GifImage(Properties.Resources.loading);
            Loading.StartAnimate();
            LoadingVisibility = Visibility.Collapsed;
            NoDataVisibility = Visibility.Collapsed;

            ArrowBack = NavigationIcon.Instance().ArrowBack;
            ArrowBack.Fill = DictionaryResource.GetColor("ColorTextDark");

            // 下载管理按钮
            DownloadManage = ButtonIcon.Instance().DownloadManage;
            DownloadManage.Height = 24;
            DownloadManage.Width = 24;
            DownloadManage.Fill = DictionaryResource.GetColor("ColorPrimary");

            TabHeaders = new ObservableCollection<TabHeader>();
            Medias = new ObservableCollection<PublicationMedia>();
            // 当前页"还有 N 个未下载"实时统计
            Medias.CollectionChanged += (_, __) => RefreshUndownloadedCount();

            #endregion
        }

        #region 命令申明

        // 返回事件
        private DelegateCommand backSpaceCommand;
        public DelegateCommand BackSpaceCommand => backSpaceCommand ?? (backSpaceCommand = new DelegateCommand(ExecuteBackSpace));

        /// <summary>
        /// 返回事件
        /// </summary>
        private void ExecuteBackSpace()
        {
            ArrowBack.Fill = DictionaryResource.GetColor("ColorText");

            // 结束任务
            tokenSource?.Cancel();

            NavigationParam parameter = new NavigationParam
            {
                ViewName = ParentView,
                ParentViewName = null,
                Parameter = null
            };
            eventAggregator.GetEvent<NavigationEvent>().Publish(parameter);
        }

        // 前往下载管理页面
        private DelegateCommand downloadManagerCommand;
        public DelegateCommand DownloadManagerCommand => downloadManagerCommand ?? (downloadManagerCommand = new DelegateCommand(ExecuteDownloadManagerCommand));

        /// <summary>
        /// 前往下载管理页面
        /// </summary>
        private void ExecuteDownloadManagerCommand()
        {
            NavigationParam parameter = new NavigationParam
            {
                ViewName = ViewDownloadManagerViewModel.Tag,
                ParentViewName = Tag,
                Parameter = null
            };
            eventAggregator.GetEvent<NavigationEvent>().Publish(parameter);
        }

        // 左侧tab点击事件
        private DelegateCommand<object> leftTabHeadersCommand;
        public DelegateCommand<object> LeftTabHeadersCommand => leftTabHeadersCommand ?? (leftTabHeadersCommand = new DelegateCommand<object>(ExecuteLeftTabHeadersCommand, CanExecuteLeftTabHeadersCommand));

        /// <summary>
        /// 左侧tab点击事件
        /// </summary>
        /// <param name="parameter"></param>
        private void ExecuteLeftTabHeadersCommand(object parameter)
        {
            if (!(parameter is TabHeader tabHeader)) { return; }

            // 页面选择
            Pager = new CustomPagerViewModel(1, (int)Math.Ceiling(double.Parse(tabHeader.SubTitle) / VideoNumberInPage));
            Pager.CurrentChanged += OnCurrentChanged_Pager;
            Pager.CountChanged += OnCountChanged_Pager;
            Pager.Current = 1;
        }

        /// <summary>
        /// 左侧tab点击事件是否允许执行
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private bool CanExecuteLeftTabHeadersCommand(object parameter)
        {
            return IsEnabled;
        }

        // 全选按钮点击事件
        private DelegateCommand<object> selectAllCommand;
        public DelegateCommand<object> SelectAllCommand => selectAllCommand ?? (selectAllCommand = new DelegateCommand<object>(ExecuteSelectAllCommand));

        /// <summary>
        /// 全选按钮点击事件
        /// </summary>
        /// <param name="parameter"></param>
        private void ExecuteSelectAllCommand(object parameter)
        {
            isBatchUpdatingSelection = true;
            try
            {
                bool target = IsSelectAll;
                foreach (var item in Medias)
                {
                    // 全选时跳过已下载项（绿点字幕 / 蓝点视频任一为真都跳过，只勾未下载的）；取消全选时仍清掉所有勾选
                    if (target && (item.IsVideoDownloaded || item.IsDownloaded)) { continue; }
                    item.IsSelected = target;
                }
            }
            finally
            {
                isBatchUpdatingSelection = false;
            }
        }

        // 批量更新选中状态时，避免逐项 PropertyChanged 回写 IsSelectAll
        private bool isBatchUpdatingSelection;

        /// <summary>
        /// 单项 IsSelected 变化时，根据"全部未下载项是否都已选"重新计算 IsSelectAll
        /// </summary>
        private void OnMediaPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (isBatchUpdatingSelection) { return; }
            if (e.PropertyName != nameof(PublicationMedia.IsSelected)) { return; }

            var selectable = Medias.Where(m => !m.IsVideoDownloaded && !m.IsDownloaded).ToList();
            IsSelectAll = selectable.Count > 0 && selectable.All(m => m.IsSelected);
        }

        /// <summary>
        /// 刷新"还有 N 个未下载"label 文本。Medias 变化时由 CollectionChanged 触发。
        /// 全部都有点（字幕或视频）时返回空字符串，让 UI 隐藏 label。
        /// </summary>
        private void RefreshUndownloadedCount()
        {
            int count = Medias.Count(m => !m.IsVideoDownloaded && !m.IsDownloaded);
            UndownloadedCountText = count > 0 ? $"还有 {count} 个未下载" : string.Empty;
        }

        // 添加选中项到下载列表事件
        private DelegateCommand addToDownloadCommand;
        public DelegateCommand AddToDownloadCommand => addToDownloadCommand ?? (addToDownloadCommand = new DelegateCommand(ExecuteAddToDownloadCommand));

        /// <summary>
        /// 添加选中项到下载列表事件
        /// </summary>
        private void ExecuteAddToDownloadCommand()
        {
            AddToDownload(true);
        }

        // 添加所有视频到下载列表事件
        private DelegateCommand addAllToDownloadCommand;
        public DelegateCommand AddAllToDownloadCommand => addAllToDownloadCommand ?? (addAllToDownloadCommand = new DelegateCommand(ExecuteAddAllToDownloadCommand));

        /// <summary>
        /// 添加所有视频到下载列表事件
        /// </summary>
        private void ExecuteAddAllToDownloadCommand()
        {
            AddToDownload(false);
        }

        // 字幕批量下载事件
        private DelegateCommand batchSubtitleDownloadCommand;
        public DelegateCommand BatchSubtitleDownloadCommand => batchSubtitleDownloadCommand ?? (batchSubtitleDownloadCommand = new DelegateCommand(ExecuteBatchSubtitleDownloadCommand));

        /// <summary>
        /// 字幕批量下载事件
        /// </summary>
        private void ExecuteBatchSubtitleDownloadCommand()
        {
            if (SelectTabId < 0 || SelectTabId >= TabHeaders.Count) { return; }

            var tab = TabHeaders[SelectTabId];
            int.TryParse(tab.SubTitle, out int totalCount);

            var parameters = new DialogParameters
            {
                { "mid", mid },
                { "tabId", tab.Id },
                { "upName", upName ?? string.Empty },
                { "tabName", tab.Title },
                { "totalCount", totalCount }
            };

            dialogService.Show(ViewSubtitleBatchDownloadViewModel.Tag, parameters, null);
        }

        #endregion

        /// <summary>
        /// 添加到下载
        /// </summary>
        /// <param name="isOnlySelected"></param>
        private async void AddToDownload(bool isOnlySelected)
        {
            // 收藏夹里只有视频
            AddToDownloadService addToDownloadService = new AddToDownloadService(PlayStreamType.VIDEO);

            // 选择文件夹
            string directory = addToDownloadService.SetDirectory(dialogService);

            // 视频计数
            int i = 0;
            await Task.Run(() =>
            {
                // 为了避免执行其他操作时，
                // Medias变化导致的异常
                var list = Medias.ToList();

                // 添加到下载
                foreach (var media in list)
                {
                    // 只下载选中项，跳过未选中项
                    if (isOnlySelected && !media.IsSelected) { continue; }

                    /// 有分P的就下载全部

                    // 开启服务
                    VideoInfoService videoInfoService = new VideoInfoService(media.Bvid);

                    addToDownloadService.SetVideoInfoService(videoInfoService);
                    addToDownloadService.GetVideo();
                    addToDownloadService.ParseVideo(videoInfoService);
                    // 下载
                    i += addToDownloadService.AddToDownload(eventAggregator, dialogService, directory);
                }
            });

            if (directory == null)
            {
                return;
            }

            // 通知用户添加到下载列表的结果
            if (i <= 0)
            {
                eventAggregator.GetEvent<MessageEvent>().Publish(DictionaryResource.GetString("TipAddDownloadingZero"));
            }
            else
            {
                eventAggregator.GetEvent<MessageEvent>().Publish($"{DictionaryResource.GetString("TipAddDownloadingFinished1")}{i}{DictionaryResource.GetString("TipAddDownloadingFinished2")}");
            }
        }

        private void OnCountChanged_Pager(int count) { }

        private bool OnCurrentChanged_Pager(int old, int current)
        {
            if (!IsEnabled)
            {
                //Pager.Current = old;
                return false;
            }

            Medias.Clear();
            IsSelectAll = false;
            LoadingVisibility = Visibility.Visible;
            NoDataVisibility = Visibility.Collapsed;

            UpdatePublication(current);

            return true;
        }

        private async void UpdatePublication(int current)
        {
            // 是否正在获取数据
            // 在所有的退出分支中都需要设为true
            IsEnabled = false;

            var tab = TabHeaders[SelectTabId];

            await Task.Run(() =>
            {
                CancellationToken cancellationToken = tokenSource.Token;

                var publications = Core.BiliApi.Users.UserSpace.GetPublication(mid, current, VideoNumberInPage, tab.Id);
                if (publications == null)
                {
                    // 没有数据，UI提示
                    LoadingVisibility = Visibility.Collapsed;
                    NoDataVisibility = Visibility.Visible;
                    return;
                }

                var videos = publications.Vlist;
                if (videos == null)
                {
                    // 没有数据，UI提示
                    LoadingVisibility = Visibility.Collapsed;
                    NoDataVisibility = Visibility.Visible;
                    return;
                }

                int indexBase = (current - 1) * VideoNumberInPage;
                int offset = 0;

                // 字幕已下载（绿点）：来自批量字幕 manifest status=done
                var subtitleBvids = new HashSet<string>();
                try
                {
                    string root = SettingsManager.GetInstance().GetSaveVideoRootPath();
                    string folderName = string.IsNullOrEmpty(upName)
                        ? mid.ToString()
                        : $"{mid}_{Format.FormatFileName(upName)}";
                    string subtitleDir = System.IO.Path.Combine(root, folderName);
                    var subtitleManifest = SubtitleBatchManifestStore.Load(subtitleDir);
                    if (subtitleManifest?.Items != null)
                    {
                        foreach (var it in subtitleManifest.Items)
                        {
                            if (it.Status == "done" && !string.IsNullOrEmpty(it.Bvid))
                            {
                                subtitleBvids.Add(it.Bvid);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    LogManager.Error(Tag, e);
                }

                // 音视频已下载（蓝点）：来自项目原有 DownloadedList。
                // 同时收集 Bvid 和 Avid——某些视频在 LiteDB 里 Bvid 字段缺失（用 AV URL 下载场景），靠 Avid 兜底匹配。
                var videoBvids = new HashSet<string>();
                var videoAids = new HashSet<long>();
                if (App.DownloadedList != null)
                {
                    foreach (var d in App.DownloadedList)
                    {
                        if (d?.DownloadBase == null) { continue; }
                        if (!string.IsNullOrEmpty(d.DownloadBase.Bvid)) { videoBvids.Add(d.DownloadBase.Bvid); }
                        if (d.DownloadBase.Avid > 0) { videoAids.Add(d.DownloadBase.Avid); }
                    }
                }
                LogManager.Debug(Tag, $"页 {current} 视频 bvid/avid 集合: 字幕完成 {subtitleBvids.Count} / 已下载Bvid {videoBvids.Count} / 已下载Avid {videoAids.Count} / DownloadedList 总条数 {App.DownloadedList?.Count ?? -1}");

                foreach (var video in videos)
                {
                    // 播放数
                    string play = string.Empty;
                    if (video.Play > 0)
                    {
                        play = Format.FormatNumber(video.Play);
                    }
                    else
                    {
                        play = "--";
                    }

                    DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1)); // 当地时区
                    DateTime dateCTime = startTime.AddSeconds(video.Created);
                    string ctime = dateCTime.ToString("yyyy-MM-dd");

                    int displayIndex = indexBase + offset + 1;
                    offset++;

                    bool hasSubtitle = !string.IsNullOrEmpty(video.Bvid) && subtitleBvids.Contains(video.Bvid);
                    bool hasVideo =
                        (!string.IsNullOrEmpty(video.Bvid) && videoBvids.Contains(video.Bvid))
                        || (video.Aid > 0 && videoAids.Contains(video.Aid));

                    App.PropertyChangeAsync(new Action(() =>
                    {
                        PublicationMedia media = new PublicationMedia(eventAggregator)
                        {
                            Index = displayIndex,
                            Avid = video.Aid,
                            Bvid = video.Bvid,
                            Duration = video.Length,
                            Title = video.Title,
                            PlayNumber = play,
                            CreateTime = ctime,
                            IsDownloaded = hasSubtitle,
                            IsVideoDownloaded = hasVideo
                        };
                        media.PropertyChanged += OnMediaPropertyChanged;
                        medias.Add(media);

                        LoadingVisibility = Visibility.Collapsed;
                        NoDataVisibility = Visibility.Collapsed;
                    }));

                    // 判断是否该结束线程，若为true，跳出循环
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

            }, (tokenSource = new CancellationTokenSource()).Token);
            IsEnabled = true;
        }

        /// <summary>
        /// 初始化页面数据
        /// </summary>
        private void InitView()
        {
            ArrowBack.Fill = DictionaryResource.GetColor("ColorTextDark");

            DownloadManage = ButtonIcon.Instance().DownloadManage;
            DownloadManage.Height = 24;
            DownloadManage.Width = 24;
            DownloadManage.Fill = DictionaryResource.GetColor("ColorPrimary");

            TabHeaders.Clear();
            Medias.Clear();
            SelectTabId = -1;
            IsSelectAll = false;
        }

        /// <summary>
        /// 导航到页面时执行
        /// </summary>
        /// <param name="navigationContext"></param>
        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            base.OnNavigatedTo(navigationContext);

            // 根据传入参数不同执行不同任务
            var parameter = navigationContext.Parameters.GetValue<Dictionary<string, object>>("Parameter");
            if (parameter == null)
            {
                return;
            }

            InitView();

            mid = (long)parameter["mid"];
            int tid = (int)parameter["tid"];
            List<PublicationZone> zones = (List<PublicationZone>)parameter["list"];
            upName = parameter.ContainsKey("upName") ? (parameter["upName"] as string ?? string.Empty) : string.Empty;

            foreach (var item in zones)
            {
                TabHeaders.Add(new TabHeader
                {
                    Id = item.Tid,
                    Title = item.Name,
                    SubTitle = item.Count.ToString()
                });
            }

            // 初始选中项
            var selectTab = TabHeaders.FirstOrDefault(item => item.Id == tid);
            SelectTabId = TabHeaders.IndexOf(selectTab);

            // 页面选择
            Pager = new CustomPagerViewModel(1, (int)Math.Ceiling(double.Parse(selectTab.SubTitle) / VideoNumberInPage));
            Pager.CurrentChanged += OnCurrentChanged_Pager;
            Pager.CountChanged += OnCountChanged_Pager;
            Pager.Current = 1;
        }

    }
}
