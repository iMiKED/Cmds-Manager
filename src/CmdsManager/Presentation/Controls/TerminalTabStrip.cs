using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Presentation.Controls
{
    public sealed class TerminalTabEventArgs : EventArgs
    {
        public TerminalTabEventArgs(int key)
        {
            Key = key;
        }

        public int Key { get; }
    }

    public sealed class TerminalTabStrip : Control, IMessageFilter
    {
        private const int TopMargin = 4;
        private const int LeftMargin = 2;
        private const int TabWing = 8;
        private const int TabRadius = 8;
        private const int MinimumTabWidth = 150;
        private const int MaximumTabWidth = 240;
        private const int OverflowAreaWidth = 40;
        private const int CloseSize = 18;
        private const int WheelDelta = 120;
        private const int WheelScrollPixels = 90;
        private const int DragScrollEdge = 28;
        private const int DragScrollPixels = 18;
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHorizontalWheel = 0x020E;

        private sealed class TabItem
        {
            internal int Key { get; set; }
            internal string Text { get; set; }
            internal string ToolTipText { get; set; }
            internal bool IsRunning { get; set; }
            internal Rectangle LogicalBounds { get; set; }
            internal Rectangle Bounds { get; set; }
            internal Rectangle CloseBounds { get; set; }
        }

        private readonly List<TabItem> _items = new List<TabItem>();
        private readonly ToolTip _toolTip = new ToolTip
        {
            AutomaticDelay = 450,
            AutoPopDelay = 10000,
            ReshowDelay = 100
        };
        private readonly ContextMenuStrip _overflowMenu = new ContextMenuStrip();
        private readonly Timer _dragScrollTimer = new Timer { Interval = 50 };
        private readonly Font _ownedFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private int _selectedIndex = -1;
        private int _hotIndex = -1;
        private int _hotCloseIndex = -1;
        private int _toolTipIndex = -1;
        private int _scrollOffset;
        private bool _showOverflow;
        private bool _hotOverflow;
        private int _verticalWheelRemainder;
        private int _horizontalWheelRemainder;
        private TabItem _dragItem;
        private Rectangle _dragThreshold;
        private Point _dragPoint;
        private bool _dragging;
        private int _dropIndex = -1;

        public TerminalTabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            TabStop = true;
            Height = 40;
            MinimumSize = new Size(0, 36);
            Font = _ownedFont;
            BackColor = Color.FromArgb(243, 244, 246);
            InactiveTabColor = Color.FromArgb(252, 252, 253);
            HoverTabColor = Color.FromArgb(100, 126, 160);
            ActiveTabColor = Color.FromArgb(28, 28, 28);
            ActiveTextColor = Color.FromArgb(245, 247, 250);
            InactiveTextColor = Color.FromArgb(38, 43, 50);
            RunningColor = Color.FromArgb(39, 190, 112);
            StoppedColor = Color.FromArgb(137, 146, 157);
            _dragScrollTimer.Tick += HandleDragScroll;
        }

        public event EventHandler<TerminalTabEventArgs> SelectedTabChanged;
        public event EventHandler<TerminalTabEventArgs> CloseRequested;

        public Color ActiveTabColor { get; set; }
        public Color InactiveTabColor { get; set; }
        public Color HoverTabColor { get; set; }
        public Color ActiveTextColor { get; set; }
        public Color InactiveTextColor { get; set; }
        public Color RunningColor { get; set; }
        public Color StoppedColor { get; set; }

        public int TabCount => _items.Count;
        public int SelectedIndex => _selectedIndex;
        public int SelectedKey => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex].Key
            : -1;

        public string GetTabText(int index)
        {
            return ItemAt(index).Text;
        }

        public int GetTabKey(int index)
        {
            return ItemAt(index).Key;
        }

        public bool IsTabRunning(int index)
        {
            return ItemAt(index).IsRunning;
        }

        public Rectangle GetTabBounds(int index)
        {
            return ItemAt(index).Bounds;
        }

        public Rectangle GetCloseBounds(int index)
        {
            return ItemAt(index).CloseBounds;
        }

        public void AddTab(int key, string text, string toolTipText, bool isRunning)
        {
            var existing = IndexOfKey(key);
            if (existing >= 0)
            {
                UpdateTab(key, text, toolTipText, isRunning);
                return;
            }

            _items.Add(new TabItem
            {
                Key = key,
                Text = text ?? string.Empty,
                ToolTipText = toolTipText ?? string.Empty,
                IsRunning = isRunning
            });
            var selectionChanged = _selectedIndex < 0;
            if (selectionChanged) _selectedIndex = 0;
            RecalculateLayout();
            if (selectionChanged) EnsureSelectedVisible();
            if (_dragging) UpdateDragLocation(_dragPoint);
            Invalidate();
            if (selectionChanged) RaiseSelectedTabChanged();
        }

        public void UpdateTab(int key, string text, string toolTipText, bool isRunning)
        {
            var index = IndexOfKey(key);
            if (index < 0) return;
            var item = _items[index];
            item.Text = text ?? string.Empty;
            item.ToolTipText = toolTipText ?? string.Empty;
            item.IsRunning = isRunning;
            RecalculateLayout();
            SetToolTipIndex(-1);
            if (_dragging) UpdateDragLocation(_dragPoint);
            Invalidate();
        }

        public bool RemoveTab(int key)
        {
            var index = IndexOfKey(key);
            if (index < 0) return false;
            EndDrag();
            var previousSelectedKey = SelectedKey;
            _items.RemoveAt(index);
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                _scrollOffset = 0;
            }
            else if (index < _selectedIndex)
            {
                _selectedIndex--;
            }
            else if (index == _selectedIndex)
            {
                _selectedIndex = Math.Min(index, _items.Count - 1);
            }

            _hotIndex = -1;
            _hotCloseIndex = -1;
            SetToolTipIndex(-1);
            RecalculateLayout();
            if (previousSelectedKey != SelectedKey) EnsureSelectedVisible();
            Invalidate();
            if (previousSelectedKey != SelectedKey) RaiseSelectedTabChanged();
            return true;
        }

        public bool SelectTab(int key)
        {
            return SelectIndex(IndexOfKey(key));
        }

        public bool SelectIndex(int index)
        {
            if (index < 0 || index >= _items.Count) return false;
            if (_selectedIndex == index)
            {
                EnsureSelectedVisible();
                Invalidate();
                return true;
            }

            _selectedIndex = index;
            EnsureSelectedVisible();
            Invalidate();
            RaiseSelectedTabChanged();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                EndDrag();
                _dragScrollTimer.Dispose();
                _toolTip.Dispose();
                _overflowMenu.Dispose();
                _ownedFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            System.Windows.Forms.Application.AddMessageFilter(this);
        }

        protected override void OnHandleDestroyed(EventArgs args)
        {
            System.Windows.Forms.Application.RemoveMessageFilter(this);
            EndDrag();
            base.OnHandleDestroyed(args);
        }

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            if (message.Msg != WmMouseWheel && message.Msg != WmMouseHorizontalWheel) return false;
            if (!IsHandleCreated || IsDisposed || !Visible || !Enabled) return false;

            // Wheel messages can target the focused console. Route only those over
            // this strip; WindowFromPoint also excludes menus and overlapping windows.
            // Signed coordinates support monitors to the left/above the primary screen.
            var packed = message.LParam.ToInt64();
            var screenPoint = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
            if (!ClientRectangle.Contains(PointToClient(screenPoint)) ||
                NativeMethods.WindowFromPoint(screenPoint) != Handle) return false;

            var delta = unchecked((short)(message.WParam.ToInt64() >> 16));
            ScrollWheel(delta, message.Msg == WmMouseHorizontalWheel, PointToClient(screenPoint));
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseHorizontalWheel)
            {
                var packed = message.LParam.ToInt64();
                var point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
                if (ClientRectangle.Contains(point))
                    ScrollWheel(unchecked((short)(message.WParam.ToInt64() >> 16)), true, point);
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnFontChanged(EventArgs args)
        {
            base.OnFontChanged(args);
            RecalculateLayout();
            Invalidate();
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            RecalculateLayout();
            EnsureSelectedVisible();
            if (_dragging) UpdateDragLocation(_dragPoint);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (_items.Count == 0) return;

            var previousSmoothing = args.Graphics.SmoothingMode;
            var previousPixelOffset = args.Graphics.PixelOffsetMode;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            args.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var state = args.Graphics.Save();
            args.Graphics.SetClip(new Rectangle(0, 0, ViewportRight, ClientSize.Height));
            for (var index = 0; index < _items.Count; index++)
            {
                if (index != _selectedIndex) DrawTab(args.Graphics, index);
            }
            if (_selectedIndex >= 0) DrawTab(args.Graphics, _selectedIndex);
            DrawDropMarker(args.Graphics);
            args.Graphics.Restore(state);

            if (_showOverflow) DrawOverflowButton(args.Graphics);
            args.Graphics.SmoothingMode = previousSmoothing;
            args.Graphics.PixelOffsetMode = previousPixelOffset;
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            EndDrag();
            Focus();
            if (_showOverflow && OverflowBounds.Contains(args.Location))
            {
                if (args.Button == MouseButtons.Left) ShowOverflowMenu();
                return;
            }

            var index = HitTest(args.Location);
            if (index < 0) return;
            var item = _items[index];
            var closeClicked = item.CloseBounds.Contains(args.Location);
            SelectIndex(index);
            if (args.Button == MouseButtons.Left && closeClicked)
            {
                CloseRequested?.Invoke(this, new TerminalTabEventArgs(item.Key));
                return;
            }
            if (args.Button != MouseButtons.Left || _items.Count < 2 || !_items.Contains(item)) return;

            _dragItem = item;
            var dragSize = SystemInformation.DragSize;
            _dragThreshold = new Rectangle(args.X - dragSize.Width / 2,
                args.Y - dragSize.Height / 2, dragSize.Width, dragSize.Height);
            _dragPoint = args.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (_dragItem != null)
            {
                if ((args.Button & MouseButtons.Left) == 0)
                    EndDrag();
                else if (_dragging || !_dragThreshold.Contains(args.Location))
                {
                    _dragging = true;
                    Cursor = Cursors.SizeAll;
                    _hotCloseIndex = -1;
                    SetToolTipIndex(-1);
                    UpdateDragLocation(args.Location);
                    return;
                }
            }
            UpdateHover(args.Location);
        }

        private void UpdateHover(Point location)
        {
            var hotOverflow = _showOverflow && OverflowBounds.Contains(location);
            var index = hotOverflow ? -1 : HitTest(location);
            var closeIndex = index >= 0 && _items[index].CloseBounds.Contains(location) ? index : -1;
            SetToolTipIndex(index);
            if (_hotIndex == index && _hotCloseIndex == closeIndex && _hotOverflow == hotOverflow) return;
            _hotIndex = index;
            _hotCloseIndex = closeIndex;
            _hotOverflow = hotOverflow;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            base.OnMouseUp(args);
            if (args.Button != MouseButtons.Left) return;
            if (_dragging)
            {
                UpdateDragLocation(args.Location);
                if (_dropIndex >= 0) MoveDraggedTab();
            }
            EndDrag();
            UpdateHover(args.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs args)
        {
            base.OnMouseCaptureChanged(args);
            if (!Capture) EndDrag();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (_dragItem != null && keyData == Keys.Escape)
            {
                EndDrag();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            _hotIndex = -1;
            _hotCloseIndex = -1;
            _hotOverflow = false;
            SetToolTipIndex(-1);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs args)
        {
            base.OnMouseWheel(args);
            if (!ClientRectangle.Contains(args.Location)) return;
            var handledArgs = args as HandledMouseEventArgs;
            if (handledArgs != null) handledArgs.Handled = true;
            ScrollWheel(args.Delta, false, args.Location);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            base.OnKeyDown(args);
            if (_items.Count == 0) return;
            if (args.KeyCode == Keys.Left)
            {
                SelectIndex((_selectedIndex - 1 + _items.Count) % _items.Count);
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.Right)
            {
                SelectIndex((_selectedIndex + 1) % _items.Count);
                args.Handled = true;
            }
        }

        private TabItem ItemAt(int index)
        {
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }

        private int IndexOfKey(int key)
        {
            return _items.FindIndex(item => item.Key == key);
        }

        private int HitTest(Point location)
        {
            if (!ClientRectangle.Contains(location) || location.X >= ViewportRight) return -1;
            if (_selectedIndex >= 0 && _items[_selectedIndex].Bounds.Contains(location)) return _selectedIndex;
            for (var index = _items.Count - 1; index >= 0; index--)
            {
                if (_items[index].Bounds.Contains(location)) return index;
            }
            return -1;
        }

        private void ScrollWheel(int delta, bool horizontal, Point location)
        {
            if (!_showOverflow || delta == 0) return;
            var remainder = horizontal ? _horizontalWheelRemainder : _verticalWheelRemainder;
            var amount = remainder + delta * WheelScrollPixels;
            if (horizontal) _horizontalWheelRemainder = amount % WheelDelta;
            else _verticalWheelRemainder = amount % WheelDelta;
            ScrollBy((horizontal ? 1 : -1) * (amount / WheelDelta));
            if (_dragging) UpdateDragLocation(location);
            else UpdateHover(location);
        }

        private bool ScrollBy(int pixels)
        {
            var offset = Math.Max(0, Math.Min(MaximumScrollOffset, _scrollOffset + pixels));
            if (offset == _scrollOffset) return false;
            _scrollOffset = offset;
            RecalculateVisibleBounds();
            Invalidate();
            return true;
        }

        private void UpdateDragLocation(Point location)
        {
            _dragPoint = location;
            var dropIndex = -1;
            if (ClientRectangle.Contains(location))
            {
                var logicalX = Math.Min(location.X, ViewportRight) + _scrollOffset;
                dropIndex = 0;
                foreach (var item in _items)
                {
                    if (item == _dragItem) continue;
                    if (logicalX < item.LogicalBounds.Left + item.LogicalBounds.Width / 2) break;
                    dropIndex++;
                }
            }
            if (_dropIndex != dropIndex)
            {
                _dropIndex = dropIndex;
                Invalidate();
            }
            _dragScrollTimer.Enabled = DragScrollDirection != 0;
        }

        private int DragScrollDirection
        {
            get
            {
                if (!_dragging || !_showOverflow || !ClientRectangle.Contains(_dragPoint)) return 0;
                var edge = Math.Min(DragScrollEdge, Math.Max(1, ViewportRight / 3));
                if (_dragPoint.X < edge && _scrollOffset > 0) return -1;
                if (_dragPoint.X >= ViewportRight - edge && _scrollOffset < MaximumScrollOffset) return 1;
                return 0;
            }
        }

        private void HandleDragScroll(object sender, EventArgs args)
        {
            var direction = DragScrollDirection;
            if (direction == 0 || !Capture)
            {
                _dragScrollTimer.Stop();
                return;
            }
            ScrollBy(direction * DragScrollPixels);
            UpdateDragLocation(_dragPoint);
        }

        private void MoveDraggedTab()
        {
            var oldIndex = _items.IndexOf(_dragItem);
            if (oldIndex < 0 || oldIndex == _dropIndex) return;
            var selectedKey = SelectedKey;
            _items.RemoveAt(oldIndex);
            _items.Insert(_dropIndex, _dragItem);
            _selectedIndex = IndexOfKey(selectedKey);
            RecalculateLayout();
            EnsureSelectedVisible();
        }

        private void EndDrag()
        {
            if (_dragItem == null) return;
            _dragItem = null;
            _dragging = false;
            _dropIndex = -1;
            _dragScrollTimer.Stop();
            Capture = false;
            Cursor = Cursors.Default;
            _hotIndex = -1;
            _hotCloseIndex = -1;
            SetToolTipIndex(-1);
            Invalidate();
        }

        private void DrawDropMarker(Graphics graphics)
        {
            if (!_dragging || _dropIndex < 0 || _dropIndex == _items.IndexOf(_dragItem)) return;
            var remainingIndex = 0;
            var x = LeftMargin;
            foreach (var item in _items)
            {
                if (item == _dragItem) continue;
                if (remainingIndex++ == _dropIndex)
                {
                    x = item.Bounds.Left;
                    break;
                }
                x = item.Bounds.Right;
            }
            x = Math.Max(2, Math.Min(ViewportRight - 3, x));
            using (var pen = new Pen(RunningColor, 2f))
            using (var brush = new SolidBrush(RunningColor))
            {
                graphics.DrawLine(pen, x, TopMargin + 3, x, ClientSize.Height - 3);
                graphics.FillEllipse(brush, x - 3, TopMargin, 6, 6);
            }
        }

        private int ViewportRight => Math.Max(0, _showOverflow ? OverflowBounds.Left - 2 : ClientSize.Width);

        private void DrawTab(Graphics graphics, int index)
        {
            var item = _items[index];
            if (item.Bounds.Right <= 0 || item.Bounds.Left >= ClientSize.Width) return;
            var selected = index == _selectedIndex;
            var hot = index == _hotIndex;
            var background = selected ? ActiveTabColor : hot ? HoverTabColor : InactiveTabColor;
            var foreground = selected || hot ? ActiveTextColor : InactiveTextColor;

            using (var path = CreateTabPath(item.Bounds))
            using (var brush = new SolidBrush(background))
                graphics.FillPath(brush, path);

            var statusBounds = new Rectangle(item.Bounds.Left + TabWing + 10,
                TopMargin + (ClientSize.Height - TopMargin - 8) / 2, 8, 8);
            using (var status = new SolidBrush(item.IsRunning ? RunningColor : StoppedColor))
                graphics.FillEllipse(status, statusBounds);

            if (index == _hotCloseIndex)
            {
                var closeBackground = selected || hot
                    ? Color.FromArgb(55, 255, 255, 255)
                    : Color.FromArgb(24, 0, 0, 0);
                using (var brush = new SolidBrush(closeBackground))
                    graphics.FillEllipse(brush, item.CloseBounds);
            }

            using (var closePen = new Pen(foreground, 1.35f))
            {
                var left = item.CloseBounds.Left + 5;
                var top = item.CloseBounds.Top + 5;
                var right = item.CloseBounds.Right - 5;
                var bottom = item.CloseBounds.Bottom - 5;
                graphics.DrawLine(closePen, left, top, right, bottom);
                graphics.DrawLine(closePen, right, top, left, bottom);
            }

            var textBounds = Rectangle.FromLTRB(statusBounds.Right + 8, TopMargin,
                item.CloseBounds.Left - 7, ClientSize.Height);
            TextRenderer.DrawText(graphics, item.Text, Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private static GraphicsPath CreateTabPath(Rectangle bounds)
        {
            var left = bounds.Left;
            var right = bounds.Right;
            var top = bounds.Top;
            var bottom = bounds.Bottom;
            var bodyLeft = left + TabWing;
            var bodyRight = right - TabWing;
            var path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(bodyLeft + TabRadius, top, bodyRight - TabRadius, top);
            path.AddBezier(bodyRight - TabRadius, top, bodyRight - 3, top,
                bodyRight, top + 3, bodyRight, top + TabRadius);
            path.AddLine(bodyRight, top + TabRadius, bodyRight, bottom - TabWing);
            path.AddBezier(bodyRight, bottom - TabWing, bodyRight, bottom - 3,
                right - 3, bottom, right, bottom);
            path.AddLine(right, bottom, left, bottom);
            path.AddBezier(left, bottom, left + 3, bottom,
                bodyLeft, bottom - 3, bodyLeft, bottom - TabWing);
            path.AddLine(bodyLeft, bottom - TabWing, bodyLeft, top + TabRadius);
            path.AddBezier(bodyLeft, top + TabRadius, bodyLeft, top + 3,
                bodyLeft + 3, top, bodyLeft + TabRadius, top);
            path.CloseFigure();
            return path;
        }

        private void DrawOverflowButton(Graphics graphics)
        {
            var bounds = OverflowBounds;
            var background = _hotOverflow ? Color.FromArgb(222, 226, 232) : Color.FromArgb(249, 250, 251);
            using (var path = RoundedRectangle(bounds, 6))
            using (var brush = new SolidBrush(background))
                graphics.FillPath(brush, path);

            var centerX = bounds.Left + bounds.Width / 2;
            var centerY = bounds.Top + bounds.Height / 2 + 1;
            using (var pen = new Pen(Color.FromArgb(70, 75, 82), 1.4f))
            {
                graphics.DrawLine(pen, centerX - 4, centerY - 2, centerX, centerY + 2);
                graphics.DrawLine(pen, centerX, centerY + 2, centerX + 4, centerY - 2);
            }
        }

        private void ShowOverflowMenu()
        {
            _overflowMenu.Items.Clear();
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                var key = item.Key;
                var menuItem = new ToolStripMenuItem(item.Text)
                {
                    Checked = index == _selectedIndex,
                    ToolTipText = item.ToolTipText
                };
                menuItem.Click += (sender, args) => SelectTab(key);
                _overflowMenu.Items.Add(menuItem);
            }
            _overflowMenu.Show(this, new Point(OverflowBounds.Left, OverflowBounds.Bottom));
        }

        private void RecalculateLayout()
        {
            var x = LeftMargin;
            foreach (var item in _items)
            {
                var textWidth = TextRenderer.MeasureText(item.Text ?? string.Empty, Font,
                    new Size(MaximumTabWidth, ClientSize.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                var width = Math.Max(MinimumTabWidth, Math.Min(MaximumTabWidth, textWidth + 72));
                item.LogicalBounds = new Rectangle(x, TopMargin, width,
                    Math.Max(1, ClientSize.Height - TopMargin + 1));
                x += width;
            }

            _showOverflow = x + LeftMargin > ClientSize.Width;
            if (!_showOverflow)
            {
                _verticalWheelRemainder = 0;
                _horizontalWheelRemainder = 0;
            }
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaximumScrollOffset));
            RecalculateVisibleBounds();
        }

        private void RecalculateVisibleBounds()
        {
            foreach (var item in _items)
            {
                var bounds = item.LogicalBounds;
                bounds.Offset(-_scrollOffset, 0);
                item.Bounds = bounds;
                var bodyRight = bounds.Right - TabWing;
                var closeTop = TopMargin + Math.Max(0, (ClientSize.Height - TopMargin - CloseSize) / 2);
                item.CloseBounds = new Rectangle(bodyRight - CloseSize - 8, closeTop, CloseSize, CloseSize);
            }
        }

        private void EnsureSelectedVisible()
        {
            if (!_showOverflow || _selectedIndex < 0 || _selectedIndex >= _items.Count)
            {
                if (!_showOverflow) _scrollOffset = 0;
                RecalculateVisibleBounds();
                return;
            }

            var logical = _items[_selectedIndex].LogicalBounds;
            var viewportLeft = LeftMargin;
            var viewportRight = ViewportRight - 1;
            if (logical.Left - _scrollOffset < viewportLeft)
                _scrollOffset = Math.Max(0, logical.Left - viewportLeft);
            else if (logical.Right - _scrollOffset > viewportRight)
                _scrollOffset = Math.Min(MaximumScrollOffset, logical.Right - viewportRight);
            RecalculateVisibleBounds();
        }

        private int MaximumScrollOffset
        {
            get
            {
                if (!_showOverflow || _items.Count == 0) return 0;
                var logicalRight = _items[_items.Count - 1].LogicalBounds.Right + LeftMargin;
                return Math.Max(0, logicalRight - ViewportRight);
            }
        }

        private Rectangle OverflowBounds => new Rectangle(
            Math.Max(0, ClientSize.Width - OverflowAreaWidth + 3),
            Math.Max(3, TopMargin - 2),
            Math.Max(1, OverflowAreaWidth - 9),
            Math.Max(1, ClientSize.Height - TopMargin - 1));

        private void SetToolTipIndex(int index)
        {
            if (_toolTipIndex == index) return;
            _toolTipIndex = index;
            _toolTip.SetToolTip(this, index >= 0 && index < _items.Count
                ? _items[index].ToolTipText
                : string.Empty);
        }

        private void RaiseSelectedTabChanged()
        {
            SelectedTabChanged?.Invoke(this, new TerminalTabEventArgs(SelectedKey));
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
