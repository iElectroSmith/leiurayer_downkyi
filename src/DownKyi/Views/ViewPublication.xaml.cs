using DownKyi.ViewModels;
using DownKyi.ViewModels.PageViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        /// <summary>
        /// 单击行：选中该行，并切换该行 checkbox。
        /// 点击 CheckBox 自身时由其自身处理，避免双重切换。
        /// </summary>
        private void OnRowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsWithin<CheckBox>(e.OriginalSource as DependencyObject)) { return; }

            if (sender is ListBoxItem item && item.DataContext is PublicationMedia media)
            {
                media.IsSelected = !media.IsSelected;
            }
        }

        /// <summary>
        /// 空格键切换当前选中行的 checkbox；上下箭头由 ListBox 默认处理。
        /// </summary>
        private void OnMediasPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space) { return; }

            if (sender is ListBox listBox && listBox.SelectedItem is PublicationMedia media)
            {
                media.IsSelected = !media.IsSelected;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 沿可视/逻辑树向上查找祖先类型 T，存在则返回 true。
        /// </summary>
        private static bool IsWithin<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T) { return true; }
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }
    }
}
