using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Utils;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DownKyi.ViewModels.PageViewModels
{
    public class FriendInfo : BindableBase
    {
        protected readonly IEventAggregator eventAggregator;

        public FriendInfo(IEventAggregator eventAggregator)
        {
            this.eventAggregator = eventAggregator;
        }

        public long Mid { get; set; }

        #region 页面属性申明

        private BitmapImage header;
        public BitmapImage Header
        {
            get => header;
            set => SetProperty(ref header, value);
        }

        private string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        private string sign;
        public string Sign
        {
            get => sign;
            set => SetProperty(ref sign, value);
        }

        // 字幕下载历史（来自 UpDownloadIndex）：未下过为 null
        private DateTime? lastDownloadTime;
        public DateTime? LastDownloadTime
        {
            get => lastDownloadTime;
            set
            {
                SetProperty(ref lastDownloadTime, value);
                RaisePropertyChanged(nameof(DownloadBadgeBrush));
                RaisePropertyChanged(nameof(HasDownloadHistory));
            }
        }

        private int downloadedCount;
        public int DownloadedCount
        {
            get => downloadedCount;
            set
            {
                SetProperty(ref downloadedCount, value);
                RaisePropertyChanged(nameof(DownloadCountText));
                RaisePropertyChanged(nameof(HasDownloadHistory));
            }
        }

        // 是否有下载历史（任一字段满足即可），驱动徽章和计数 label 的可见性
        public bool HasDownloadHistory => LastDownloadTime.HasValue || DownloadedCount > 0;

        // 数量显示文本，DownloadedCount > 0 才显示
        public string DownloadCountText => DownloadedCount > 0 ? $"已下 {DownloadedCount}" : string.Empty;

        // 徽章颜色（按最近下载时间分级）。返回 Brush 直接给 xaml Fill 绑，省一个 converter。
        public Brush DownloadBadgeBrush
        {
            get
            {
                if (!LastDownloadTime.HasValue)
                {
                    // 没下过：用透明，xaml 上靠 HasDownloadHistory 控制可见性
                    return Brushes.Transparent;
                }
                var span = DateTime.Now - LastDownloadTime.Value;
                if (span.TotalDays <= 7) { return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); }    // 一周内：绿
                if (span.TotalDays <= 30) { return new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); }   // 一月内：蓝
                if (span.TotalDays <= 90) { return new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26)); }   // 三月内：橙
                return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));                                  // 更早：灰
            }
        }

        #endregion

        #region 命令申明

        // 视频标题点击事件
        private DelegateCommand<object> userCommand;
        public DelegateCommand<object> UserCommand => userCommand ?? (userCommand = new DelegateCommand<object>(ExecuteUserCommand));

        /// <summary>
        /// 视频标题点击事件
        /// </summary>
        /// <param name="parameter"></param>
        private void ExecuteUserCommand(object parameter)
        {
            if (!(parameter is string tag)) { return; }

            NavigateToView.NavigationView(eventAggregator, ViewUserSpaceViewModel.Tag, tag, Mid);
        }

        #endregion
    }
}
