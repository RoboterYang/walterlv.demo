﻿using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using Lsj.Util.Win32.Enums;
using Lsj.Util.Win32;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Walterlv.Demo.Windows
{
    /// <summary>
    /// 包装一个 <see cref="Window"/> 成为一个 WPF 控件。
    /// </summary>
    internal class WindowWrapper : FrameworkElement
    {
        static WindowWrapper()
        {
            FocusableProperty.OverrideMetadata(typeof(WindowWrapper), new UIPropertyMetadata(true));
        }

        [ThreadStatic]
        private static Dictionary<IntPtr, Dictionary<IntPtr, WindowWrapper>> _topWindowChildrenWindowRelationship;

        private IntPtr _currentParentHandle = IntPtr.Zero;

        /// <summary>
        /// 创建包装句柄为 <paramref name="childHandle"/> 窗口的 <see cref="WindowWrapper"/> 的实例。
        /// </summary>
        /// <param name="childHandle">要包装的窗口的句柄。</param>
        public WindowWrapper(IntPtr childHandle)
        {
            // 初始化。
            Handle = childHandle;

            // 监听事件。
            IsVisibleChanged += OnIsVisibleChanged;

            // 设置窗口样式为子窗口。这里的样式值与 HwndHost/HwndSource 成对时设置的值一模一样。
            Lsj.Util.Win32.User32.SetWindowLong(childHandle,
                GetWindowLongIndexes.GWL_STYLE,
                (IntPtr)(WindowStyles.WS_CHILDWINDOW | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPCHILDREN));

            WindowAccentCompositor.DisableDwm(childHandle);
        }

        /// <summary>
        /// 获取包装的窗口的句柄。
        /// </summary>
        public IntPtr Handle { get; }

        private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var parent = Window.GetWindow(this);
            if (IsVisible)
            {
                if (parent != null)
                {
                    parent.LocationChanged += ParentWindow_LocationChanged;
                }
                LayoutUpdated += OnLayoutUpdated;
                await ShowChildAsync().ConfigureAwait(false);
            }
            else
            {
                if (parent != null)
                {
                    parent.LocationChanged -= ParentWindow_LocationChanged;
                }
                LayoutUpdated -= OnLayoutUpdated;
                await HideChildAsync().ConfigureAwait(false);
            }
        }

        private void ParentWindow_LocationChanged(object sender, EventArgs e)
        {
            // WPF 子窗口的触摸在父窗口位置更新的时候不会更新，因此需要手动更新，否则会触摸偏差。
            //_ = ArrangeChildAsync(new Size(RenderSize.Width, RenderSize.Height + 1));
            //_ = ArrangeChildAsync(RenderSize);
        }

        protected override Size MeasureOverride(Size availableSize) => default;

        protected override Size ArrangeOverride(Size finalSize) =>
            // _ = ArrangeChildAsync(finalSize);
            // Dispatcher.InvokeAsync(() => ArrangeChildAsync(finalSize), DispatcherPriority.Loaded);
            finalSize;

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            // 最终决定在 LayoutUpdated 事件里面更新子窗口的位置和尺寸，因为：
            //  1. Arrange 仅在大小改变的时候才会触发，这使得如果外面更新了 Margin 等导致位置改变大小不变时（例如窗口最大化），窗口位置不会刷新。
            //  2. 使用 Loaded 优先级延迟布局可以解决以上问题，但会导致布局更新不及时，部分依赖于布局的代码（例如拖拽调整窗口大小）会计算错误。
            //_ = ArrangeChildAsync(new Size(ActualWidth, ActualHeight));
        }

        /// <summary>
        /// 显示子窗口。
        /// </summary>
        private async Task ShowChildAsync()
        {
            // 计算父窗口的句柄。
            var hwndParent = PresentationSource.FromVisual(this) is HwndSource parentSource
                ? parentSource.Handle
                : IntPtr.Zero;

            _currentParentHandle = hwndParent;
            var relationship = GetRelationship(hwndParent);
            relationship[Handle] = this;

            // 连接子窗口。
            // 注意连接子窗口后，窗口的消息循环会强制同步，这可能降低 UI 的响应性能。详情请参考：
            // https://blog.walterlv.com/post/all-processes-freezes-if-their-windows-are-connected-via-setparent.html
            SetParent(Handle, hwndParent);
            Debug.WriteLine("[Window] 嵌入");

            // 显示子窗口。
            // 注意调用顺序：先嵌入子窗口，这可以避免任务栏中出现窗口图标。
            Lsj.Util.Win32.User32.ShowWindow(Handle, ShowWindowCommands.SW_SHOW);
        }

        /// <summary>
        /// 隐藏子窗口。
        /// </summary>
        private async Task HideChildAsync()
        {
            // 隐藏子窗口。
            Lsj.Util.Win32.User32.ShowWindow(Handle, ShowWindowCommands.SW_HIDE);

            // 断开子窗口的连接。
            // 这是为了避免一直连接窗口对 UI 响应性能的影响。详情请参考：
            // https://blog.walterlv.com/post/all-processes-freezes-if-their-windows-are-connected-via-setparent.html
            SetParent(Handle, IntPtr.Zero);
            Debug.WriteLine("[Window] 取出");

            var relationship = GetRelationship(_currentParentHandle);
            relationship.Remove(Handle);
            _currentParentHandle = IntPtr.Zero;
        }

        /// <summary>
        /// 布局子窗口。
        /// </summary>
        /// <param name="size">设定子窗口的显示尺寸。</param>
        public void ArrangeChild(Size size)
        {
            var presentationSource = PresentationSource.FromVisual(this);
            if (presentationSource is null)
            {
                // 因为此方法可能被延迟执行，所以可能此元素已经不在可视化树了，这时会进入此分支。
                return;
            }

            // 获取子窗口相对于父窗口的相对坐标。
            var transform = TransformToAncestor(presentationSource.RootVisual);
            var offset = transform.Transform(default);

            // 转移到后台线程执行代码，这可以让 UI 短暂地立刻响应。
            // await Dispatcher.ResumeBackgroundAsync();

            // 移动子窗口到合适的布局位置。
            var x = (int)(offset.X);
            var y = (int)(offset.Y);
            var w = (int)(size.Width);
            var h = (int)(size.Height);
            Lsj.Util.Win32.User32.SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, SetWindowPosFlags.SWP_NOZORDER);
            // Debug.WriteLine($"[Window] 布局 {x}, {y}, {w}, {h}");
        }

        public static Dictionary<IntPtr, WindowWrapper> GetRelationship(IntPtr topWindowHandle)
        {
            if (_topWindowChildrenWindowRelationship is null)
            {
                _topWindowChildrenWindowRelationship = new Dictionary<IntPtr, Dictionary<IntPtr, WindowWrapper>>();
            }
            if (_topWindowChildrenWindowRelationship.TryGetValue(topWindowHandle, out var relationship))
            {
                return relationship;
            }
            relationship = new Dictionary<IntPtr, WindowWrapper>();
            _topWindowChildrenWindowRelationship[topWindowHandle] = relationship;
            return relationship;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    }
}
