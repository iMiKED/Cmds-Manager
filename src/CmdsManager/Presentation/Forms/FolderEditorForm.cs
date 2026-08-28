using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Controls;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    public sealed class FolderEditorForm : Form
    {
        private readonly Guid _id;
        private readonly Guid? _parentFolderId;
        private readonly int _sortOrder;
        private readonly bool _isExpanded;
        private readonly LocalizationService _text;
        private readonly FluentTextBox _name = new FluentTextBox();
        private readonly FolderIconButton _iconButton = new FolderIconButton();
        private readonly FolderPickerControl _picker;
        private readonly ToolStripDropDown _pickerDropDown = new ToolStripDropDown();
        private readonly ToolTip _toolTip = new ToolTip();
        private FolderIconKind _selectedIcon;
        private string _selectedColor;

        public FolderEditorForm(ScriptFolderDefinition source, Guid? parentFolderId, LocalizationService text,
            ApplicationTheme theme = ApplicationTheme.System)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            var model = source?.Clone() ?? new ScriptFolderDefinition { ParentFolderId = parentFolderId };
            _id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
            _parentFolderId = source == null ? parentFolderId : model.ParentFolderId;
            _sortOrder = model.SortOrder;
            _isExpanded = model.IsExpanded;
            _selectedIcon = model.Icon;
            _selectedColor = string.IsNullOrWhiteSpace(model.IconColor) ? "#4F46E5" : model.IconColor;

            Text = source == null ? _text["Folder.Title.Add"] : _text["Folder.Title.Edit"];
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(474, 108);
            Icon = ApplicationResources.Icon;

            _name.Text = model.Name;
            _name.LeadingInset = 42;
            _name.Controls.Add(_iconButton);
            _name.Resize += (sender, args) => LayoutIconButton();
            LayoutIconButton();

            _iconButton.Icon = _selectedIcon;
            _iconButton.IconColor = _selectedColor;
            _iconButton.AccessibleName = _text["Folder.ChooseIcon"];
            _iconButton.Click += ShowIconPicker;
            _toolTip.SetToolTip(_iconButton, _text["Folder.ChooseIcon"]);

            _picker = new FolderPickerControl(_selectedIcon, _selectedColor, _text)
            {
                AccessibleName = _text["Folder.ChooseIcon"]
            };
            _picker.SelectionChanged += HandlePickerSelectionChanged;
            _picker.DoneClicked += (sender, args) => _pickerDropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            var host = new ToolStripControlHost(_picker)
            {
                AutoSize = false,
                Size = _picker.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _pickerDropDown.AutoClose = true;
            _pickerDropDown.AutoSize = true;
            _pickerDropDown.DropShadowEnabled = true;
            _pickerDropDown.Margin = Padding.Empty;
            _pickerDropDown.Padding = Padding.Empty;
            _pickerDropDown.Items.Add(host);
            _pickerDropDown.Opened += (sender, args) =>
            {
                FluentGeometry.ApplyRoundedRegion(_pickerDropDown, 9f);
                _picker.FocusSelectedItem();
            };
            _pickerDropDown.SizeChanged += (sender, args) => FluentGeometry.ApplyRoundedRegion(_pickerDropDown, 9f);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 12, 12, 6),
                ColumnCount = 2,
                RowCount = 1
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(new Label
            {
                Text = _text["Folder.Name"],
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(2, 7, 6, 3)
            }, 0, 0);
            _name.Dock = DockStyle.Fill;
            _name.Margin = new Padding(2, 1, 2, 3);
            content.Controls.Add(_name, 1, 0);

            var save = FluentDialogButtons.Primary(_text["Common.Save"]);
            var cancel = FluentDialogButtons.Secondary(_text["Common.Cancel"], DialogResult.Cancel);
            save.Click += SaveAndClose;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(content, 0, 0);
            layout.Controls.Add(FluentDialogButtons.Footer(save, cancel), 0, 1);
            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;

            var palette = AppThemeManager.Resolve(theme);
            AppThemeManager.ApplyWindow(this, theme);
            AppThemeManager.ApplyToolStrip(_pickerDropDown, palette);
            _picker.ApplyPalette(palette);
            AppThemeManager.ApplyWindowCorners(this);
        }

        public ScriptFolderDefinition Result { get; private set; }

        internal FluentTextBox NameEditor => _name;
        internal Control IconSelector => _iconButton;
        internal FolderPickerControl Picker => _picker;

        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            if (_pickerDropDown.Visible) _pickerDropDown.Close();
            base.OnFormClosed(args);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
                _pickerDropDown.Dispose();
            }
            base.Dispose(disposing);
        }

        private void LayoutIconButton()
        {
            _iconButton.SetBounds(1, 1, 40, Math.Max(1, _name.Height - 2));
        }

        private void ShowIconPicker(object sender, EventArgs args)
        {
            if (_pickerDropDown.Visible)
            {
                _pickerDropDown.Close();
                return;
            }

            _picker.SetSelection(_selectedIcon, _selectedColor);
            _pickerDropDown.Show(_name, new Point(0, _name.Height + 3), ToolStripDropDownDirection.BelowRight);
        }

        private void HandlePickerSelectionChanged(object sender, EventArgs args)
        {
            _selectedIcon = _picker.SelectedIcon;
            _selectedColor = _picker.SelectedColor;
            _iconButton.Icon = _selectedIcon;
            _iconButton.IconColor = _selectedColor;
            _iconButton.Invalidate();
        }

        private void SaveAndClose(object sender, EventArgs args)
        {
            var name = _name.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, _text["Folder.NameRequired"], Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return;
            }

            Result = new ScriptFolderDefinition
            {
                Id = _id,
                Name = name,
                ParentFolderId = _parentFolderId,
                SortOrder = _sortOrder,
                Icon = _selectedIcon,
                IconColor = _selectedColor,
                IsExpanded = _isExpanded
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class FolderIconButton : Control, IFluentThemedControl
        {
            private AppThemePalette _palette = AppThemePalette.Light();
            private bool _hot;

            internal FolderIconButton()
            {
                Cursor = Cursors.Hand;
                TabStop = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            }

            internal FolderIconKind Icon { get; set; }
            internal string IconColor { get; set; }

            public void ApplyPalette(AppThemePalette palette)
            {
                _palette = palette ?? AppThemePalette.Light();
                BackColor = _palette.Input;
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs args)
            {
                base.OnMouseEnter(args);
                _hot = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs args)
            {
                base.OnMouseLeave(args);
                _hot = false;
                Invalidate();
            }

            protected override void OnGotFocus(EventArgs args)
            {
                base.OnGotFocus(args);
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs args)
            {
                base.OnLostFocus(args);
                Invalidate();
            }

            protected override void OnKeyDown(KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter || args.KeyCode == Keys.Space)
                {
                    OnClick(EventArgs.Empty);
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
                base.OnKeyDown(args);
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                args.Graphics.Clear(_hot || Focused ? _palette.Hover : _palette.SurfaceAlternate);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var divider = new Pen(_palette.Border))
                    args.Graphics.DrawLine(divider, Width - 1, 4, Width - 1, Height - 5);
                FolderIconRenderer.Draw(args.Graphics,
                    new Rectangle((Width - 21) / 2, (Height - 21) / 2, 21, 21), Icon,
                    FolderIconRenderer.ParseColor(IconColor, _palette.Accent));
                if (Focused && ShowFocusCues)
                {
                    var focus = ClientRectangle;
                    focus.Inflate(-3, -3);
                    using (var path = FluentGeometry.RoundedRectangle(focus, 5f))
                    using (var pen = new Pen(_palette.Accent, 1.25f))
                        args.Graphics.DrawPath(pen, path);
                }
            }
        }

        internal sealed class FolderPickerControl : Control, IFluentThemedControl
        {
            private const int GridLeft = 14;
            private const int GridWidth = 258;
            private const int Columns = 6;
            private const int ColorTop = 10;
            private const int ColorCellHeight = 34;
            private const int DividerY = 89;
            private const int IconTop = 101;
            private const int IconCellHeight = 40;
            private readonly LocalizationService _text;
            private readonly FluentButton _done;
            private readonly ToolTip _toolTip = new ToolTip();
            private AppThemePalette _palette = AppThemePalette.Light();
            private int _hoverColor = -1;
            private int _hoverIcon = -1;
            private int _focusedItem = -1;

            internal FolderPickerControl(FolderIconKind icon, string color, LocalizationService text)
            {
                _text = text ?? throw new ArgumentNullException(nameof(text));
                SelectedIcon = icon;
                SelectedColor = color;
                Size = new Size(286, 354);
                MinimumSize = Size;
                MaximumSize = Size;
                Cursor = Cursors.Default;
                TabStop = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);

                _done = FluentDialogButtons.Secondary(_text["Folder.Done"]);
                _done.AutoSize = false;
                _done.Size = new Size(78, 29);
                _done.Location = new Point(194, 313);
                _done.Click += (sender, args) => DoneClicked?.Invoke(this, EventArgs.Empty);
                Controls.Add(_done);
            }

            internal FolderIconKind SelectedIcon { get; private set; }
            internal string SelectedColor { get; private set; }
            internal int ColorChoiceCount => FolderIconRenderer.ColorPalette.Length;
            internal int IconChoiceCount => FolderIconRenderer.PickerIcons.Length;
            internal int ColorColumnCount => Columns;
            internal int IconColumnCount => Columns;
            internal string DoneText => _done.Text;

            internal event EventHandler SelectionChanged;
            internal event EventHandler DoneClicked;

            internal void SetSelection(FolderIconKind icon, string color)
            {
                SelectedIcon = icon;
                SelectedColor = color;
                Invalidate();
            }

            internal void FocusSelectedItem()
            {
                var iconIndex = Array.IndexOf(FolderIconRenderer.PickerIcons, SelectedIcon);
                _focusedItem = FolderIconRenderer.ColorPalette.Length + Math.Max(0, iconIndex);
                Focus();
                Invalidate();
            }

            public void ApplyPalette(AppThemePalette palette)
            {
                _palette = palette ?? AppThemePalette.Light();
                BackColor = _palette.Surface;
                ForeColor = _palette.Text;
                _done.ApplyPalette(_palette);
                _done.BackColor = _palette.Hover;
                Invalidate(true);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _toolTip.Dispose();
                base.Dispose(disposing);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                var key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
                    base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs args)
            {
                var colorCount = FolderIconRenderer.ColorPalette.Length;
                var itemCount = colorCount + FolderIconRenderer.PickerIcons.Length;
                if (_focusedItem < 0) _focusedItem = colorCount;

                if (args.KeyCode == Keys.Left) _focusedItem = Math.Max(0, _focusedItem - 1);
                else if (args.KeyCode == Keys.Right) _focusedItem = Math.Min(itemCount - 1, _focusedItem + 1);
                else if (args.KeyCode == Keys.Up) _focusedItem = Math.Max(0, _focusedItem - Columns);
                else if (args.KeyCode == Keys.Down) _focusedItem = Math.Min(itemCount - 1, _focusedItem + Columns);
                else if (args.KeyCode == Keys.Enter || args.KeyCode == Keys.Space)
                {
                    ActivateFocusedItem();
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                    return;
                }
                else
                {
                    base.OnKeyDown(args);
                    return;
                }

                args.Handled = true;
                args.SuppressKeyPress = true;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs args)
            {
                base.OnMouseMove(args);
                var color = HitColor(args.Location);
                var icon = HitIcon(args.Location);
                if (_hoverColor == color && _hoverIcon == icon) return;
                _hoverColor = color;
                _hoverIcon = icon;
                Cursor = color >= 0 || icon >= 0 ? Cursors.Hand : Cursors.Default;
                if (color >= 0) _toolTip.SetToolTip(this, FolderIconRenderer.ColorPalette[color]);
                else if (icon >= 0) _toolTip.SetToolTip(this,
                    _text["Folder.Icon." + FolderIconRenderer.PickerIcons[icon]]);
                else _toolTip.SetToolTip(this, string.Empty);
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs args)
            {
                base.OnMouseLeave(args);
                _hoverColor = -1;
                _hoverIcon = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs args)
            {
                base.OnMouseDown(args);
                if (args.Button != MouseButtons.Left) return;
                var color = HitColor(args.Location);
                if (color >= 0)
                {
                    Focus();
                    _focusedItem = color;
                    SelectColor(color);
                    return;
                }

                var icon = HitIcon(args.Location);
                if (icon < 0) return;
                Focus();
                _focusedItem = FolderIconRenderer.ColorPalette.Length + icon;
                SelectIcon(icon);
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                args.Graphics.Clear(_palette.Surface);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var divider = new Pen(_palette.Border))
                    args.Graphics.DrawLine(divider, GridLeft, DividerY, GridLeft + GridWidth, DividerY);

                for (var index = 0; index < FolderIconRenderer.ColorPalette.Length; index++)
                    PaintColor(args.Graphics, index);
                for (var index = 0; index < FolderIconRenderer.PickerIcons.Length; index++)
                    PaintIcon(args.Graphics, index);
            }

            private void ActivateFocusedItem()
            {
                var colorCount = FolderIconRenderer.ColorPalette.Length;
                if (_focusedItem < colorCount) SelectColor(_focusedItem);
                else SelectIcon(_focusedItem - colorCount);
            }

            private void SelectColor(int index)
            {
                if (index < 0 || index >= FolderIconRenderer.ColorPalette.Length) return;
                SelectedColor = FolderIconRenderer.ColorPalette[index];
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }

            private void SelectIcon(int index)
            {
                if (index < 0 || index >= FolderIconRenderer.PickerIcons.Length) return;
                SelectedIcon = FolderIconRenderer.PickerIcons[index];
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }

            private void PaintColor(Graphics graphics, int index)
            {
                var cell = ColorCell(index);
                var selected = string.Equals(FolderIconRenderer.ColorPalette[index], SelectedColor,
                    StringComparison.OrdinalIgnoreCase);
                var hovered = index == _hoverColor;
                var diameter = selected ? 28 : hovered ? 24 : 20;
                var circle = new Rectangle(cell.Left + (cell.Width - diameter) / 2,
                    cell.Top + (cell.Height - diameter) / 2, diameter, diameter);
                if (selected)
                {
                    using (var ring = new Pen(_palette.Text, 2f))
                        graphics.DrawEllipse(ring, circle);
                    circle.Inflate(-5, -5);
                }
                using (var brush = new SolidBrush(FolderIconRenderer.ParseColor(
                    FolderIconRenderer.ColorPalette[index], _palette.Accent)))
                    graphics.FillEllipse(brush, circle);
                if (Focused && _focusedItem == index)
                {
                    var focus = cell;
                    focus.Inflate(-5, -3);
                    using (var path = FluentGeometry.RoundedRectangle(focus, 7f))
                    using (var pen = new Pen(_palette.Accent, 1.25f))
                        graphics.DrawPath(pen, path);
                }
            }

            private void PaintIcon(Graphics graphics, int index)
            {
                var cell = IconCell(index);
                var selected = FolderIconRenderer.PickerIcons[index] == SelectedIcon;
                if (selected || index == _hoverIcon)
                {
                    var tile = cell;
                    tile.Inflate(-5, -3);
                    using (var path = FluentGeometry.RoundedRectangle(tile, 7f))
                    using (var brush = new SolidBrush(selected ? _palette.Hover : _palette.SurfaceAlternate))
                        graphics.FillPath(brush, path);
                }
                FolderIconRenderer.Draw(graphics,
                    new Rectangle(cell.Left + (cell.Width - 23) / 2, cell.Top + (cell.Height - 23) / 2, 23, 23),
                    FolderIconRenderer.PickerIcons[index],
                    FolderIconRenderer.ParseColor(SelectedColor, _palette.Accent));
                if (Focused && _focusedItem == FolderIconRenderer.ColorPalette.Length + index)
                {
                    var focus = cell;
                    focus.Inflate(-4, -2);
                    using (var path = FluentGeometry.RoundedRectangle(focus, 7f))
                    using (var pen = new Pen(_palette.Accent, 1.25f))
                        graphics.DrawPath(pen, path);
                }
            }

            private static Rectangle ColorCell(int index)
            {
                var width = GridWidth / Columns;
                return new Rectangle(GridLeft + index % Columns * width,
                    ColorTop + index / Columns * ColorCellHeight, width, ColorCellHeight);
            }

            private static Rectangle IconCell(int index)
            {
                var width = GridWidth / Columns;
                return new Rectangle(GridLeft + index % Columns * width,
                    IconTop + index / Columns * IconCellHeight, width, IconCellHeight);
            }

            private static int HitColor(Point point)
            {
                for (var index = 0; index < FolderIconRenderer.ColorPalette.Length; index++)
                    if (ColorCell(index).Contains(point)) return index;
                return -1;
            }

            private static int HitIcon(Point point)
            {
                for (var index = 0; index < FolderIconRenderer.PickerIcons.Length; index++)
                    if (IconCell(index).Contains(point)) return index;
                return -1;
            }
        }
    }
}
