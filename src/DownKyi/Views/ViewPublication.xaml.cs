using DownKyi.ViewModels;
using DownKyi.ViewModels.PageViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DownKyi.Views
{
    /// <summary>
    /// ViewPublication.xaml 的交互逻辑
    /// </summary>
    public partial class ViewPublication : UserControl
    {
        public ViewPublication()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 标题左键双击跳转视频详情。单击不再跳转，避免误触。
        /// </summary>
        private void OnTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) { return; }

            if (sender is FrameworkElement fe
                && fe.DataContext is PublicationMedia media
                && DataContext is ViewPublicationViewModel vm)
            {
                if (media.TitleCommand?.CanExecute(vm.PageName) == true)
                {
                    media.TitleCommand.Execute(vm.PageName);
                }
            }
        }
    }
}
