using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
        private readonly TableLayoutPanel _icons = ChoiceGrid(80);
        private readonly TableLayoutPanel _colors = ChoiceGrid(68);
        private readonly FolderPreviewControl _preview;
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
            _preview = new FolderPreviewControl
            {
                Dock = DockStyle.Fill,
                Height = 44,
                NameText = model.Name,
                Icon = _selectedIcon,
                IconColor = _selectedColor
            };

            Text = source == null ? _text["Folder.Title.Add"] : _text["Folder.Title.Edit"];
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(474, 318);
            Icon = ApplicationResources.Icon;

            var palette = AppThemeManager.Resolve(theme);
            _name.Text = model.Name;
            _name.TextChanged += (sender, args) => { _preview.NameText = _name.Text; _preview.Invalidate(); };

            var iconIndex = 0;
            foreach (FolderIconKind icon in Enum.GetValues(typeof(FolderIconKind)))
            {
                var option = new FolderChoiceControl
                {
                    ChoiceKind = FolderChoiceKind.Icon,
                    Icon = icon,
                    Width = 38,
                    Height = 38,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(2),
                    Palette = palette,
                    Selected = icon == _selectedIcon,
                    AccessibleName = _text["Folder.Icon." + icon]
                };
                _toolTip.SetToolTip(option, option.AccessibleName);
                option.Click += (sender, args) => SelectIcon(((FolderChoiceControl)sender).Icon);
                _icons.Controls.Add(option, iconIndex % 5, iconIndex / 5);
                iconIndex++;
            }

            var colorIndex = 0;
            foreach (var color in FolderIconRenderer.ColorPalette)
            {
                var option = new FolderChoiceControl
                {
                    ChoiceKind = FolderChoiceKind.Color,
                    ColorValue = color,
                    Width = 32,
                    Height = 32,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(3),
                    Palette = palette,
                    Selected = string.Equals(color, _selectedColor, StringComparison.OrdinalIgnoreCase),
                    AccessibleName = color
                };
                _toolTip.SetToolTip(option, color);
                option.Click += (sender, args) => SelectColor(((FolderChoiceControl)sender).ColorValue);
                _colors.Controls.Add(option, colorIndex % 5, colorIndex / 5);
                colorIndex++;
            }

            foreach (FolderChoiceControl icon in _icons.Controls) icon.IconColor = _selectedColor;
            _preview.Palette = palette;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 6),
                ColumnCount = 2,
                RowCount = 4
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(content, 0, _text["Folder.Name"], _name);
            AddRow(content, 1, _text["Folder.Icon"], _icons);
            AddRow(content, 2, _text["Folder.Color"], _colors);
            AddRow(content, 3, _text["Folder.Preview"], _preview);

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

            AppThemeManager.ApplyWindow(this, theme);
            AppThemeManager.ApplyWindowCorners(this);
        }

        public ScriptFolderDefinition Result { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTip.Dispose();
            base.Dispose(disposing);
        }

        private void SelectIcon(FolderIconKind icon)
        {
            _selectedIcon = icon;
            foreach (FolderChoiceControl option in _icons.Controls)
            {
                option.Selected = option.Icon == icon;
                option.Invalidate();
            }
            _preview.Icon = icon;
            _preview.Invalidate();
        }

        private void SelectColor(string color)
        {
            _selectedColor = color;
            foreach (FolderChoiceControl option in _colors.Controls)
            {
                option.Selected = string.Equals(option.ColorValue, color, StringComparison.OrdinalIgnoreCase);
                option.Invalidate();
            }
            foreach (FolderChoiceControl option in _icons.Controls)
            {
                option.IconColor = color;
                option.Invalidate();
            }
            _preview.IconColor = color;
            _preview.Invalidate();
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

        private static TableLayoutPanel ChoiceGrid(int height)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = height,
                ColumnCount = 5,
                RowCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = Padding.Empty
            };
            for (var index = 0; index < 5; index++)
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(2, 8, 6, 3)
            }, 0, row);
            control.Margin = new Padding(2, 2, 2, 4);
            table.Controls.Add(control, 1, row);
        }

        private enum FolderChoiceKind { Icon, Color }

        private sealed class FolderChoiceControl : Control
        {
            private bool _hovered;

            internal FolderChoiceKind ChoiceKind { get; set; }
            internal FolderIconKind Icon { get; set; }
            internal string IconColor { get; set; }
            internal string ColorValue { get; set; }
            internal bool Selected { get; set; }
            internal AppThemePalette Palette { get; set; }

            internal FolderChoiceControl()
            {
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs eventArgs) { _hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
            protected override void OnMouseLeave(EventArgs eventArgs) { _hovered = false; Invalidate(); base.OnMouseLeave(eventArgs); }
            protected override void OnGotFocus(EventArgs eventArgs) { Invalidate(); base.OnGotFocus(eventArgs); }
            protected override void OnLostFocus(EventArgs eventArgs) { Invalidate(); base.OnLostFocus(eventArgs); }
            protected override void OnKeyDown(KeyEventArgs eventArgs)
            {
                if (eventArgs.KeyCode == Keys.Space || eventArgs.KeyCode == Keys.Enter)
                {
                    OnClick(EventArgs.Empty);
                    eventArgs.Handled = true;
                }
                base.OnKeyDown(eventArgs);
            }

            protected override void OnPaint(PaintEventArgs eventArgs)
            {
                base.OnPaint(eventArgs);
                var palette = Palette ?? AppThemePalette.Light();
                eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var outer = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = RoundedPath(outer, 7))
                using (var background = new SolidBrush(Selected ? palette.Selection : _hovered ? palette.Hover : palette.Surface))
                using (var border = new Pen(Selected || Focused ? palette.Accent : palette.Border, Selected ? 2f : 1f))
                {
                    eventArgs.Graphics.FillPath(background, path);
                    eventArgs.Graphics.DrawPath(border, path);
                }

                if (ChoiceKind == FolderChoiceKind.Icon)
                {
                    var color = FolderIconRenderer.ParseColor(IconColor, palette.Accent);
                    FolderIconRenderer.Draw(eventArgs.Graphics, new Rectangle(8, 8, Width - 16, Height - 16), Icon, color);
                }
                else
                {
                    var color = FolderIconRenderer.ParseColor(ColorValue, palette.Accent);
                    using (var brush = new SolidBrush(color))
                        eventArgs.Graphics.FillEllipse(brush, 8, 8, Width - 16, Height - 16);
                }
            }
        }

        private sealed class FolderPreviewControl : Control
        {
            internal string NameText { get; set; }
            internal FolderIconKind Icon { get; set; }
            internal string IconColor { get; set; }
            internal AppThemePalette Palette { get; set; }

            internal FolderPreviewControl()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs eventArgs)
            {
                base.OnPaint(eventArgs);
                var palette = Palette ?? AppThemePalette.Light();
                eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = RoundedPath(bounds, 8))
                using (var brush = new SolidBrush(palette.SurfaceAlternate))
                using (var pen = new Pen(palette.Border))
                {
                    eventArgs.Graphics.FillPath(brush, path);
                    eventArgs.Graphics.DrawPath(pen, path);
                }
                FolderIconRenderer.Draw(eventArgs.Graphics, new Rectangle(11, 10, 24, 24), Icon,
                    FolderIconRenderer.ParseColor(IconColor, palette.Accent));
                TextRenderer.DrawText(eventArgs.Graphics, string.IsNullOrWhiteSpace(NameText) ? "…" : NameText,
                    Font, new Rectangle(43, 0, Math.Max(1, Width - 52), Height), palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
