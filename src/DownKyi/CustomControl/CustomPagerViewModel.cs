using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace DownKyi.CustomControl
{
    public class PagerItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private int number;
        public int Number
        {
            get => number;
            set
            {
                number = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
            }
        }

        private bool isCurrent;
        public bool IsCurrent
        {
            get => isCurrent;
            set
            {
                isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }
    }

    public class CustomPagerViewModel : INotifyPropertyChanged
    {
        public CustomPagerViewModel(int current, int count)
        {
            Pages = new ObservableCollection<PagerItem>();
            Current = current;
            Count = count;

            SetView();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Current修改的回调
        public delegate bool CurrentChangedHandler(int old, int current);
        public event CurrentChangedHandler CurrentChanged;
        protected virtual bool OnCurrentChanged(int old, int current)
        {
            if (CurrentChanged == null)
            {
                return false;
            }
            else
            {
                return CurrentChanged.Invoke(old, current);
            }
        }

        // Count修改的回调
        public delegate void CountChangedHandler(int count);
        public event CountChangedHandler CountChanged;
        protected virtual void OnCountChanged(int count)
        {
            CountChanged?.Invoke(count);
        }

        #region 绑定属性

        // 整体可见性（Count<=1 时隐藏整个分页器）
        private Visibility visibility;
        public Visibility Visibility
        {
            get => visibility;
            set
            {
                visibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
            }
        }

        // 全部页号（每个项含 Number + IsCurrent）。SetView 中重建。
        public ObservableCollection<PagerItem> Pages { get; }

        private int count;
        public int Count
        {
            get => count;
            set
            {
                if (value < Current || value < 0)
                {
                    Visibility = Visibility.Hidden;
                    System.Console.WriteLine(value.ToString());
                }
                else
                {
                    count = value;
                    if (count <= 1) { Visibility = Visibility.Hidden; }
                    else { Visibility = Visibility.Visible; }

                    OnCountChanged(count);
                    SetView();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
                }
            }
        }

        private int current;
        public int Current
        {
            get
            {
                if (current < 1) { current = 1; }
                return current;
            }
            set
            {
                if (Count > 0 && (value > Count || value < 1))
                {
                    // 越界忽略
                }
                else
                {
                    bool isSuccess = OnCurrentChanged(current, value);
                    if (isSuccess)
                    {
                        current = value;
                        SetView();
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
                    }
                }
            }
        }

        // 上一页 / 下一页箭头可见性
        private Visibility previousVisibility;
        public Visibility PreviousVisibility
        {
            get => previousVisibility;
            set
            {
                previousVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviousVisibility)));
            }
        }

        private Visibility nextVisibility;
        public Visibility NextVisibility
        {
            get => nextVisibility;
            set
            {
                nextVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NextVisibility)));
            }
        }

        #endregion

        #region 命令

        // 上一页
        private MyDelegateCommand previousCommand;
        public MyDelegateCommand PreviousCommand => previousCommand ?? (previousCommand = new MyDelegateCommand(PreviousExecuted));
        public void PreviousExecuted(object obj)
        {
            Current -= 1;
        }

        // 下一页
        private MyDelegateCommand nextCommand;
        public MyDelegateCommand NextCommand => nextCommand ?? (nextCommand = new MyDelegateCommand(NextExecuted));
        public void NextExecuted(object obj)
        {
            Current += 1;
        }

        // 跳转到指定页（CommandParameter = 页号）
        private MyDelegateCommand goToPageCommand;
        public MyDelegateCommand GoToPageCommand => goToPageCommand ?? (goToPageCommand = new MyDelegateCommand(GoToPageExecuted));
        public void GoToPageExecuted(object obj)
        {
            if (obj == null) { return; }
            int target;
            if (obj is int n) { target = n; }
            else if (!int.TryParse(obj.ToString(), out target)) { return; }
            if (target == Current) { return; }
            Current = target;
        }

        #endregion

        /// <summary>
        /// 全展开页号集合（不省略），并更新箭头可见性。
        /// 总页数较大时由外层 ScrollViewer 提供水平滚动。
        /// </summary>
        private void SetView()
        {
            // 上一页 / 下一页箭头始终显示；越界点击在 Current setter 中已被忽略
            PreviousVisibility = Visibility.Visible;
            NextVisibility = Visibility.Visible;

            // 重建 Pages（既有项更新 IsCurrent，长度变化时增删）
            int targetLen = Count > 0 ? Count : 0;
            // 增长
            while (Pages.Count < targetLen)
            {
                Pages.Add(new PagerItem { Number = Pages.Count + 1, IsCurrent = false });
            }
            // 收缩
            while (Pages.Count > targetLen)
            {
                Pages.RemoveAt(Pages.Count - 1);
            }
            // 更新每项状态
            for (int i = 0; i < Pages.Count; i++)
            {
                int num = i + 1;
                if (Pages[i].Number != num) { Pages[i].Number = num; }
                bool shouldBeCurrent = num == Current;
                if (Pages[i].IsCurrent != shouldBeCurrent) { Pages[i].IsCurrent = shouldBeCurrent; }
            }
        }
    }
}
