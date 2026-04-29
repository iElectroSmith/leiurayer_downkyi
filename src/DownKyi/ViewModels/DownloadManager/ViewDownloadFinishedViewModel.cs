using DownKyi.Core.Logging;
using DownKyi.Core.Settings;
using DownKyi.CustomControl;
using DownKyi.Services;
using DownKyi.Utils;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prism.Services.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace DownKyi.ViewModels.DownloadManager
{
    public class ViewDownloadFinishedViewModel : BaseViewModel
    {
        public const string Tag = "PageDownloadManagerDownloadFinished";

        // 每页显示条数
        private readonly int PageSize = 30;

        #region 页面属性申明

        private ObservableCollection<DownloadedItem> downloadedList;
        public ObservableCollection<DownloadedItem> DownloadedList
        {
            get => downloadedList;
            set => SetProperty(ref downloadedList, value);
        }

        private ObservableCollection<DownloadedItem> displayList;
        public ObservableCollection<DownloadedItem> DisplayList
        {
            get => displayList;
            set => SetProperty(ref displayList, value);
        }

        private int finishedSortBy;
        public int FinishedSortBy
        {
            get => finishedSortBy;
            set => SetProperty(ref finishedSortBy, value);
        }

        private CustomPagerViewModel pager;
        public CustomPagerViewModel Pager
        {
            get => pager;
            set => SetProperty(ref pager, value);
        }

        #endregion

        public ViewDownloadFinishedViewModel(IEventAggregator eventAggregator, IDialogService dialogService) : base(eventAggregator, dialogService)
        {
            // 初始化DownloadedList
            DownloadedList = App.DownloadedList;
            DisplayList = new ObservableCollection<DownloadedItem>();

            DownloadedList.CollectionChanged += new NotifyCollectionChangedEventHandler((sender, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    SetDialogService();
                }

                // 列表变化时刷新分页
                App.PropertyChangeAsync(new Action(() =>
                {
                    RefreshPage();
                }));
            });
            SetDialogService();

            DownloadFinishedSort finishedSort = SettingsManager.GetInstance().GetDownloadFinishedSort();
            switch (finishedSort)
            {
                case DownloadFinishedSort.DOWNLOAD:
                    FinishedSortBy = 0;
                    break;
                case DownloadFinishedSort.NUMBER:
                    FinishedSortBy = 1;
                    break;
                default:
                    FinishedSortBy = 0;
                    break;
            }
            App.SortDownloadedList(finishedSort);

            // 初始化分页（排序后DownloadedList已填充）
            InitPager();
        }

        #region 分页

        /// <summary>
        /// 初始化分页器
        /// </summary>
        private void InitPager()
        {
            int count = (int)Math.Ceiling((double)App.DownloadedList.Count / PageSize);
            if (count < 1) { count = 1; }

            Pager = new CustomPagerViewModel(1, count);
            Pager.CurrentChanged += OnCurrentChanged_Pager;
            Pager.CountChanged += OnCountChanged_Pager;

            RefreshPage();
        }

        private void OnCountChanged_Pager(int count) { }

        private bool OnCurrentChanged_Pager(int old, int current)
        {
            RefreshPage(current);
            return true;
        }

        /// <summary>
        /// 刷新当前页数据
        /// </summary>
        private void RefreshPage(int? targetPage = null)
        {
            int totalCount = App.DownloadedList.Count;
            int pageCount = (int)Math.Ceiling((double)totalCount / PageSize);
            if (pageCount < 1) { pageCount = 1; }

            // 确定当前页
            int currentPage = targetPage ?? (Pager?.Current ?? 1);
            if (currentPage > pageCount) { currentPage = pageCount; }
            if (currentPage < 1) { currentPage = 1; }

            // 当总页数变化时重建分页器（避免Count setter在count<current时不生效的问题）
            if (Pager == null || Pager.Count != pageCount)
            {
                var newPager = new CustomPagerViewModel(currentPage, pageCount);
                newPager.CurrentChanged += OnCurrentChanged_Pager;
                newPager.CountChanged += OnCountChanged_Pager;
                Pager = newPager;
            }

            // 计算切片范围
            int skip = (currentPage - 1) * PageSize;
            var pageItems = App.DownloadedList.Skip(skip).Take(PageSize).ToList();

            // 更新显示列表
            DisplayList.Clear();
            foreach (var item in pageItems)
            {
                DisplayList.Add(item);
            }
        }

        #endregion

        #region 命令申明

        // 下载完成列表排序事件
        private DelegateCommand<object> finishedSortCommand;
        public DelegateCommand<object> FinishedSortCommand => finishedSortCommand ?? (finishedSortCommand = new DelegateCommand<object>(ExecuteFinishedSortCommand));

        /// <summary>
        /// 下载完成列表排序事件
        /// </summary>
        /// <param name="parameter"></param>
        private void ExecuteFinishedSortCommand(object parameter)
        {
            if (!(parameter is int index)) { return; }

            switch (index)
            {
                case 0:
                    App.SortDownloadedList(DownloadFinishedSort.DOWNLOAD);
                    // 更新设置
                    SettingsManager.GetInstance().SetDownloadFinishedSort(DownloadFinishedSort.DOWNLOAD);
                    break;
                case 1:
                    App.SortDownloadedList(DownloadFinishedSort.NUMBER);
                    // 更新设置
                    SettingsManager.GetInstance().SetDownloadFinishedSort(DownloadFinishedSort.NUMBER);
                    break;
                default:
                    App.SortDownloadedList(DownloadFinishedSort.DOWNLOAD);
                    // 更新设置
                    SettingsManager.GetInstance().SetDownloadFinishedSort(DownloadFinishedSort.DOWNLOAD);
                    break;
            }
        }

        // 清空下载完成列表事件
        private DelegateCommand clearAllDownloadedCommand;
        public DelegateCommand ClearAllDownloadedCommand => clearAllDownloadedCommand ?? (clearAllDownloadedCommand = new DelegateCommand(ExecuteClearAllDownloadedCommand));

        /// <summary>
        /// 清空下载完成列表事件
        /// </summary>
        private async void ExecuteClearAllDownloadedCommand()
        {
            AlertService alertService = new AlertService(dialogService);
            ButtonResult result = alertService.ShowWarning(DictionaryResource.GetString("ConfirmDelete"));
            if (result != ButtonResult.OK)
            {
                return;
            }

            // 使用Clear()不能触发NotifyCollectionChangedAction.Remove事件
            // 因此遍历删除
            // DownloadingList中元素被删除后不能继续遍历
            await Task.Run(() =>
            {
                List<DownloadedItem> list = DownloadedList.ToList();
                foreach (DownloadedItem item in list)
                {
                    App.PropertyChangeAsync(new Action(() =>
                    {
                        App.DownloadedList.Remove(item);
                    }));
                }
            });
        }

        #endregion

        private async void SetDialogService()
        {
            try
            {
                await Task.Run(() =>
                {
                    List<DownloadedItem> list = DownloadedList.ToList();
                    foreach (var item in list)
                    {
                        if (item != null && item.DialogService == null)
                        {
                            item.DialogService = dialogService;
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Core.Utils.Debugging.Console.PrintLine("SetDialogService()发生异常: {0}", e);
                LogManager.Error($"{Tag}.SetDialogService()", e);
            }
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            base.OnNavigatedFrom(navigationContext);

            SetDialogService();
        }

    }
}
