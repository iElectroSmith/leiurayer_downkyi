using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DownKyi.Views.Dialogs
{
    public partial class ViewSubtitleBatchDownload : UserControl
    {
        public ViewSubtitleBatchDownload()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) { return; }
            var window = Window.GetWindow(this);
            if (window != null) { window.DragMove(); }
        }
    }
}
