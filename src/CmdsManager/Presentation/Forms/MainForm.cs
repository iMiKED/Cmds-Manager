using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Infrastructure.Windows;
using CmdsManager.Presentation.Controls;
using CmdsManager.Presentation.Theming;
using Microsoft.Win32;

namespace CmdsManager.Presentation.Forms
{
    public sealed class MainForm : Form
    {
        private readonly ConfigurationState _state;
        private readonly ConfigurationStore _store;
        private readonly ProcessSupervisor _supervisor;
        private readonly IScriptEditorLauncher _editor;
        private readonly IApplicationStartupRegistration _startup;
        private readonly ShowAppHotkeyManager _showAppHotkey;
        private readonly IExecutionLog _log;
        private readonly LocalizationService _text;
        private readonly DataGridView _grid = new DoubleBufferedDataGridView();
        private readonly Font _activityFont = new Font("Segoe UI Symbol", 11f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _gridHeaderFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly ToolStripTextBox _filter = new ToolStripTextBox();
        private readonly ToolStrip _toolbar;
        private readonly Dictionary<ToolStripButton, ToolbarIcon> _toolbarIcons =
            new Dictionary<ToolStripButton, ToolbarIcon>();
        private readonly Dictionary<Guid, Guid> _managedChildParents = new Dictionary<Guid, Guid>();
        private readonly Dictionary<string, Guid> _managedChildIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        private readonly ConsoleTabsControl _console;
        private readonly SplitContainer _mainSplit;
        private readonly System.Windows.Forms.Timer _layoutSaveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        private readonly ToolStripButton _addButton;
        private readonly ToolStripButton _addFolderButton;
        private readonly ToolStripButton _editButton;
        private readonly ToolStripButton _deleteButton;
        private readonly ToolStripButton _startButton;
        private readonly ToolStripButton _stopButton;
        private readonly ToolStripButton _startAllButton;
        private readonly ToolStripButton _stopAllButton;
        private readonly ToolStripButton _reloadButton;
        private readonly ToolStripButton _settingsButton;
        private readonly ToolStripButton _aboutButton;
        private readonly ToolStripButton _exitButton;
        private readonly ToolStripLabel _filterLabel = new ToolStripLabel();
        private readonly ToolStripMenuItem _contextStart = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextStop = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextAddFolder = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextEdit = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextEditFile = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextShowInFolder = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextDelete = new ToolStripMenuItem();
        private Point _dragStart = Point.Empty;
        private HierarchyItemKey _draggedItem;
        private HierarchyDropIndicator _dropIndicator;
        private Guid? _dragHoverFolderId;
        private DateTime _dragHoverStartedUtc;
        private QuickLaunchForm _quickLauncher;
        private bool _refreshingGrid;
        private bool _restoringPaneLayout;
        private bool _restoringWindowPlacement;
        private bool _windowPlacementReady;
        private bool _restoreWindowMaximized;
        private bool _consolePaneMaximized;
        private int _normalConsolePaneHeight;
        private FormWindowState _lastNonMinimizedWindowState = FormWindowState.Normal;
        private AppThemePalette _palette = AppThemePalette.Light();

        public MainForm(ConfigurationState state, ConfigurationStore store, ProcessSupervisor supervisor,
            IScriptEditorLauncher editor, IApplicationStartupRegistration startup, ShowAppHotkeyManager showAppHotkey,
            IExecutionLog log, LocalizationService text)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _startup = startup ?? throw new ArgumentNullException(nameof(startup));
            _showAppHotkey = showAppHotkey ?? throw new ArgumentNullException(nameof(showAppHotkey));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _console = new ConsoleTabsControl(_text, () => Configuration.Application,
                () => Configuration.Application.Hotkeys, ResolveConsoleWordWrap,
                Path.Combine(Path.GetDirectoryName(_store.ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    "logs", "console"), ResolveConsoleLaunchBehavior)
                { Dock = DockStyle.Fill };

            Text = ApplicationResources.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 520);
            Size = new Size(1120, 680);
            Icon = ApplicationResources.Icon;
            KeyPreview = true;
            ApplyWindowPlacement();

            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Height = 42,
                Padding = new Padding(7, 4, 7, 4),
                ImageScalingSize = new Size(16, 16),
                CanOverflow = true
            };
            _addButton = Button((sender, args) => AddScript(), ToolbarIcon.Add);
            _addFolderButton = Button((sender, args) => AddFolder(), ToolbarIcon.FolderAdd);
            _editButton = Button((sender, args) => EditSelected(), ToolbarIcon.Edit);
            _deleteButton = Button(async (sender, args) => await DeleteSelectedAsync(), ToolbarIcon.Delete, FluentToolRole.Danger);
            _startButton = Button((sender, args) => StartSelected(), ToolbarIcon.Start, FluentToolRole.Primary);
            _stopButton = Button(async (sender, args) => await StopSelectedAsync(), ToolbarIcon.Stop);
            _startAllButton = Button((sender, args) => RunAllEnabled(), ToolbarIcon.StartAll);
            _stopAllButton = Button(async (sender, args) => await StopAllAsync(), ToolbarIcon.StopAll);
            _reloadButton = Button((sender, args) => ReloadConfiguration(), ToolbarIcon.Reload);
            _settingsButton = Button((sender, args) => OpenSettings(), ToolbarIcon.Settings);
            _aboutButton = Button((sender, args) => ShowAbout(), ToolbarIcon.About);
            _exitButton = Button((sender, args) => ExitRequested?.Invoke(this, EventArgs.Empty), ToolbarIcon.Exit, FluentToolRole.Danger);
            UseCompactImageOnly(_reloadButton, _settingsButton, _aboutButton, _exitButton);
            _filter.AutoSize = false;
            _filter.Width = 160;
            _filter.Height = 24;
            _filter.TextChanged += (sender, args) => RefreshGrid();
            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _addButton, _addFolderButton, _editButton, _deleteButton, new ToolStripSeparator(),
                _startButton, _stopButton, _startAllButton, _stopAllButton, new ToolStripSeparator(),
                _reloadButton, _settingsButton, _aboutButton, new ToolStripSeparator(),
                _filterLabel, _filter, new ToolStripSeparator(), _exitButton
            });

            ConfigureGrid();
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel2,
                Panel1MinSize = OneScriptPanelHeight,
                Panel2MinSize = 100
            };
            _mainSplit.Panel1.Controls.Add(_grid);
            _mainSplit.Panel2.Controls.Add(_console);
            Controls.Add(_mainSplit);
            Controls.Add(_toolbar);
            _toolbar.Dock = DockStyle.Top;

            _grid.SelectionChanged += HandleGridSelectionChanged;
            _grid.RowPostPaint += HandleGridRowPostPaint;
            _grid.CellPainting += HandleGridCellPainting;
            _grid.CellMouseClick += HandleGridCellMouseClick;
            _grid.CellDoubleClick += HandleGridCellDoubleClick;
            _grid.MouseDown += HandleGridMouseDown;
            _grid.MouseMove += HandleGridMouseMove;
            _grid.MouseUp += (sender, args) => _dragStart = Point.Empty;
            _grid.DragOver += HandleGridDragOver;
            _grid.DragDrop += HandleGridDragDrop;
            _grid.DragLeave += HandleGridDragLeave;
            _grid.Paint += HandleGridPaint;
            _mainSplit.SplitterMoved += HandleSplitterMoved;
            _mainSplit.DoubleClick += (sender, args) => ToggleConsolePaneMaximized();
            _mainSplit.SizeChanged += HandleSplitSizeChanged;
            _layoutSaveTimer.Tick += HandleLayoutSaveTimer;
            FormClosing += HandleFormClosing;
            Shown += HandleMainShown;
            Resize += HandleMainResize;
            ResizeEnd += HandleWindowResizeEnd;

            _supervisor.StateChanged += HandleStateChanged;
            _supervisor.OutputReceived += HandleOutputReceived;
            _supervisor.InstanceStarted += HandleInstanceStarted;
            _supervisor.InstanceExited += HandleInstanceExited;
            _text.Changed += HandleLocalizationChanged;
            SystemEvents.UserPreferenceChanged += HandleSystemPreferenceChanged;
            _console.CloseRequested += HandleConsoleCloseRequested;
            _console.PaneMaximizeRequested += HandleConsolePaneMaximizeRequested;
            _console.WordWrapChanged += HandleConsoleWordWrapChanged;
            ApplyLocalization();
        }

        public event EventHandler ExitRequested;
        public bool AllowClose { get; set; }
        public AppConfiguration Configuration => _state.Current;

        public void ShowFromTray()
        {
            var targetState = WindowState == FormWindowState.Maximized ||
                _lastNonMinimizedWindowState == FormWindowState.Maximized
                ? FormWindowState.Maximized
                : FormWindowState.Normal;
            _restoringWindowPlacement = true;
            try
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (!Visible) Show();
                EnsureRestoredWindowOnScreen();
                if (WindowState != targetState) WindowState = targetState;
                if (!Visible) Show();
            }
            finally
            {
                _restoringWindowPlacement = false;
            }

            Activate();
            BringToFront();
            ScheduleWindowPlacementSave();
        }

        public void ToggleFromTray()
        {
            if (Visible && WindowState != FormWindowState.Minimized && IsWindowOnScreen(Bounds)) Hide();
            else ShowFromTray();
        }

        public void RunAllEnabled()
        {
            StartScripts(ScriptHierarchy.GetAllScriptsInDisplayOrder(Configuration));
        }

        public void RunScript(string selector)
        {
            selector = (selector ?? string.Empty).Trim();
            Guid identifier;
            var byId = Guid.TryParse(selector, out identifier);
            var script = Configuration.Scripts.FirstOrDefault(item =>
                (byId && item.Id == identifier) || item.Name.Equals(selector, StringComparison.CurrentCultureIgnoreCase));
            if (script == null)
            {
                MessageBox.Show(this, _text.Get("Main.ScriptNotFound", selector), _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!script.Enabled)
            {
                MessageBox.Show(this, _text["Main.Disabled"], _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { _supervisor.Start(script, Configuration.PowerShell7Path); }
            catch (Exception exception) { ShowError(_text.Get("Main.StartFailed", script.Name), exception); }
        }

        public void RunManagedChild(string parentWorkingDirectory, string[] startArguments)
        {
            RunManagedChild(Guid.Empty, parentWorkingDirectory, startArguments);
        }

        public void RunManagedChild(Guid parentScriptId, string parentWorkingDirectory, string[] startArguments)
        {
            try
            {
                var request = ManagedStartRequestParser.Parse(parentWorkingDirectory, startArguments);
                var script = request.ToScriptDefinition();
                var identity = parentScriptId.ToString("D") + "\0" + script.Path.ToUpperInvariant() + "\0" +
                    script.Launch.WorkingDirectory.ToUpperInvariant() + "\0" + script.Launch.Arguments;
                Guid childId;
                if (!_managedChildIds.TryGetValue(identity, out childId))
                    _managedChildIds[identity] = childId = script.Id;
                script.Id = childId;
                var owner = ResolveWordWrapOwner(parentScriptId);
                var ownerScript = Configuration.Scripts.FirstOrDefault(item => item.Id == owner);
                script.Launch.WordWrap = ownerScript?.Launch.WordWrap ?? Configuration.Defaults.WordWrap;
                if (ownerScript != null) _managedChildParents[script.Id] = ownerScript.Id;
                _supervisor.Start(script, Configuration.PowerShell7Path);
            }
            catch (Exception exception)
            {
                ShowError(_text["Main.ChildStartFailed"], exception);
            }
        }

        public async Task StopAllAsync()
        {
            try { await _supervisor.StopAllAsync(); }
            catch (Exception exception) { ShowError(_text["Main.StopAllFailed"], exception); }
        }

        public void ShowAbout()
        {
            using (var form = new AboutForm(_text, Configuration.Application.Theme)) form.ShowDialog(this);
        }

        public void ShowQuickLauncher()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)ShowQuickLauncher);
                return;
            }

            if (_quickLauncher != null && !_quickLauncher.IsDisposed)
            {
                _quickLauncher.DialogResult = DialogResult.Cancel;
                _quickLauncher.Close();
                return;
            }

            var action = QuickLaunchSelectionAction.None;
            var scriptId = Guid.Empty;
            using (var form = new QuickLaunchForm(Configuration.Scripts, _text,
                Configuration.Application.Theme, id => _supervisor.GetSnapshot(id)))
            {
                _quickLauncher = form;
                try
                {
                    if (Visible && WindowState != FormWindowState.Minimized)
                        form.ShowDialog(this);
                    else
                        form.ShowDialog();
                    action = form.SelectedAction;
                    scriptId = form.SelectedScriptId;
                }
                finally
                {
                    if (ReferenceEquals(_quickLauncher, form)) _quickLauncher = null;
                }
            }
            HandleQuickLaunchSelection(action, scriptId);
        }

        internal void HandleQuickLaunchSelection(QuickLaunchSelectionAction action, Guid scriptId)
        {
            if (action == QuickLaunchSelectionAction.AddScript)
            {
                ShowFromTray();
                AddScript();
                return;
            }
            if (action != QuickLaunchSelectionAction.ActivateScript || scriptId == Guid.Empty) return;
            if (_supervisor.IsRunning(scriptId))
            {
                RevealRunningScript(scriptId);
                return;
            }
            RunScript(scriptId.ToString("D"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _supervisor.StateChanged -= HandleStateChanged;
                _supervisor.OutputReceived -= HandleOutputReceived;
                _supervisor.InstanceStarted -= HandleInstanceStarted;
                _supervisor.InstanceExited -= HandleInstanceExited;
                _text.Changed -= HandleLocalizationChanged;
                SystemEvents.UserPreferenceChanged -= HandleSystemPreferenceChanged;
                _console.CloseRequested -= HandleConsoleCloseRequested;
                _console.PaneMaximizeRequested -= HandleConsolePaneMaximizeRequested;
                _console.WordWrapChanged -= HandleConsoleWordWrapChanged;
                _layoutSaveTimer.Stop();
                _layoutSaveTimer.Dispose();
                foreach (var button in _toolbarIcons.Keys)
                {
                    var image = button.Image;
                    button.Image = null;
                    image?.Dispose();
                }
                _activityFont.Dispose();
                _gridHeaderFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowDrop = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.RowTemplate.Height = 38;
            _grid.ColumnHeadersHeight = 34;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.ColumnHeadersDefaultCellStyle.Font = _gridHeaderFont;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.None;
            var activityColumn = Column("Activity", 40);
            activityColumn.MinimumWidth = 40;
            activityColumn.Resizable = DataGridViewTriState.False;
            activityColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            activityColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Padding = Padding.Empty;
            activityColumn.DefaultCellStyle.Font = _activityFont;
            _grid.Columns.Add(activityColumn);
            _grid.Columns.Add(Column("Name", 170));
            _grid.Columns.Add(Column("Type", 65));
            _grid.Columns.Add(Column("Interpreter", 150));
            _grid.Columns.Add(Column("AutoStart", 55));
            _grid.Columns.Add(Column("State", 105));
            _grid.Columns.Add(Column("Pid", 70));
            _grid.Columns.Add(Column("Started", 130));
            _grid.Columns.Add(Column("ExitCode", 55));
            var pathColumn = Column("Path", 260);
            pathColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns.Add(pathColumn);

            var context = new ContextMenuStrip();
            _contextStart.Click += (sender, args) => StartSelected();
            _contextStop.Click += async (sender, args) => await StopSelectedAsync();
            _contextAddFolder.Click += (sender, args) => AddFolder();
            _contextEdit.Click += (sender, args) => EditSelected();
            _contextEditFile.Click += (sender, args) => EditSelectedFile();
            _contextShowInFolder.Click += (sender, args) => ShowSelectedInFolder();
            _contextDelete.Click += async (sender, args) => await DeleteSelectedAsync();
            context.Opening += HandleContextOpening;
            context.Items.AddRange(new ToolStripItem[]
            {
                _contextStart, _contextStop, new ToolStripSeparator(),
                _contextAddFolder, _contextEdit, _contextEditFile, _contextShowInFolder,
                new ToolStripSeparator(), _contextDelete
            });
            _grid.ContextMenuStrip = context;
        }

        private void ApplyLocalization()
        {
            _addButton.Text = _text["Main.Add"];
            _addFolderButton.Text = _text["Main.AddFolder"];
            _editButton.Text = _text["Main.Edit"];
            _deleteButton.Text = _text["Main.Delete"];
            _startButton.Text = _text["Main.Start"];
            _stopButton.Text = _text["Main.Stop"];
            _startAllButton.Text = _text["Main.StartAll"];
            _stopAllButton.Text = _text["Main.StopAll"];
            _reloadButton.Text = _text["Main.Reload"];
            _settingsButton.Text = _text["Main.Settings"];
            _aboutButton.Text = _text["Main.About"];
            _exitButton.Text = _text["Main.Exit"];
            foreach (var button in _toolbarIcons.Keys) button.ToolTipText = button.Text;
            _filterLabel.Text = _text["Main.Filter"];
            _filter.ToolTipText = _text["Main.FilterHint"];
            _grid.Columns["Activity"].HeaderText = "●";
            _grid.Columns["Activity"].ToolTipText = _text["Main.Column.ActivityHint"];
            _grid.Columns["Name"].HeaderText = _text["Main.Column.Name"];
            _grid.Columns["Type"].HeaderText = _text["Main.Column.Type"];
            _grid.Columns["Interpreter"].HeaderText = _text["Main.Column.Interpreter"];
            _grid.Columns["AutoStart"].HeaderText = _text["Main.Column.AutoStart"];
            _grid.Columns["State"].HeaderText = _text["Main.Column.State"];
            _grid.Columns["Pid"].HeaderText = "PID";
            _grid.Columns["Started"].HeaderText = _text["Main.Column.Started"];
            _grid.Columns["ExitCode"].HeaderText = _text["Main.Column.ExitCode"];
            _grid.Columns["Path"].HeaderText = _text["Main.Column.Path"];
            _contextStart.Text = _text["Main.Start"];
            _contextStop.Text = _text["Main.Stop"];
            _contextAddFolder.Text = _text["Main.Context.NewFolder"];
            _contextEdit.Text = _text["Main.Context.EditEntry"];
            _contextEditFile.Text = _text["Main.Context.EditFile"];
            _contextShowInFolder.Text = _text["Main.Context.ShowFolder"];
            _contextDelete.Text = _text["Main.Context.DeleteEntry"];
            RefreshGrid();
            ApplyTheme();
        }

        private void RefreshGrid()
        {
            var selectedItem = SelectedHierarchyItem;
            var filter = _filter.Text?.Trim() ?? string.Empty;
            _refreshingGrid = true;
            try
            {
                _grid.Rows.Clear();
                AppendHierarchyRows(null, 0, new List<Guid>(), filter, selectedItem);
            }
            finally { _refreshingGrid = false; }
            UpdateScriptPanelMinimum();
            UpdateButtons();
        }

        private void AppendHierarchyRows(Guid? parentFolderId, int depth, IList<Guid> ancestors, string filter,
            HierarchyItemKey selectedItem)
        {
            foreach (var item in ScriptHierarchy.GetChildren(Configuration, parentFolderId))
            {
                if (item.Kind == HierarchyItemKind.Folder)
                {
                    var folder = Configuration.Folders.FirstOrDefault(value => value.Id == item.Id);
                    if (folder == null || (filter.Length > 0 && !FolderMatchesFilter(folder.Id, filter, new HashSet<Guid>())))
                        continue;
                    AddFolderRow(folder, depth, ancestors, selectedItem);
                    if (filter.Length > 0 || folder.IsExpanded)
                    {
                        var childAncestors = ancestors.Concat(new[] { folder.Id }).ToList();
                        AppendHierarchyRows(folder.Id, depth + 1, childAncestors, filter, selectedItem);
                    }
                }
                else
                {
                    var script = Configuration.Scripts.FirstOrDefault(value => value.Id == item.Id);
                    if (script == null || (filter.Length > 0 && !ScriptMatchesFilter(script, filter))) continue;
                    AddScriptRow(script, depth, ancestors, selectedItem);
                }
            }
        }

        private void AddFolderRow(ScriptFolderDefinition folder, int depth, IList<Guid> ancestors,
            HierarchyItemKey selectedItem)
        {
            var scripts = ScriptHierarchy.GetDescendantScripts(Configuration, folder.Id);
            var runtime = AggregateFolderRuntime(folder.Id, scripts);
            var rowIndex = _grid.Rows.Add(ActivityGlyph(runtime.State), folder.Name, _text["Main.Folder.Type"],
                _text.Get("Main.Folder.ScriptCount", scripts.Count), "—", scripts.Count == 0 ? _text["Main.Folder.Empty"] : StateText(runtime),
                "-", runtime.StartedAt?.ToString("g") ?? "-", "-", FolderPath(folder));
            var row = _grid.Rows[rowIndex];
            row.Tag = new FolderGridRowTag(folder.Id);
            row.Cells["Name"].Tag = new HierarchyRowMetadata(HierarchyItemKey.Folder(folder.Id), depth,
                folder.ParentFolderId, ancestors, folder.IsExpanded);
            ApplyFolderRuntimeVisual(row, runtime, scripts.Count == 0);
            SelectRowIfNeeded(row, selectedItem);
        }

        private void AddScriptRow(ScriptDefinition script, int depth, IList<Guid> ancestors,
            HierarchyItemKey selectedItem)
        {
            var type = Path.GetExtension(script.Path).TrimStart('.').ToUpperInvariant();
            var runtime = _supervisor.GetSnapshot(script.Id);
            var rowIndex = _grid.Rows.Add(ActivityGlyph(runtime.State), script.Name, type, InterpreterText(script),
                script.Launch.AutoStartWithApplication ? _text["Common.Yes"] : _text["Common.No"], StateText(runtime),
                runtime.ProcessId?.ToString() ?? "-", runtime.StartedAt?.ToString("g") ?? "-",
                runtime.LastExitCode?.ToString() ?? "-", script.Path);
            var row = _grid.Rows[rowIndex];
            row.Tag = script.Id;
            row.Cells["Name"].Tag = new HierarchyRowMetadata(HierarchyItemKey.Script(script.Id), depth,
                script.FolderId, ancestors, false);
            ApplyRuntimeVisual(row, script, runtime);
            SelectRowIfNeeded(row, selectedItem);
        }

        private void SelectRowIfNeeded(DataGridViewRow row, HierarchyItemKey selectedItem)
        {
            var metadata = row.Cells["Name"].Tag as HierarchyRowMetadata;
            if (selectedItem == null || metadata == null || !selectedItem.Equals(metadata.Item)) return;
            row.Selected = true;
            _grid.CurrentCell = row.Cells["Name"];
        }

        private bool FolderMatchesFilter(Guid folderId, string filter, ISet<Guid> visited)
        {
            if (!visited.Add(folderId)) return false;
            var folder = Configuration.Folders.FirstOrDefault(item => item.Id == folderId);
            if (folder == null) return false;
            if (folder.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            foreach (var child in ScriptHierarchy.GetChildren(Configuration, folderId))
            {
                if (child.Kind == HierarchyItemKind.Folder)
                {
                    if (FolderMatchesFilter(child.Id, filter, visited)) return true;
                }
                else
                {
                    var script = Configuration.Scripts.FirstOrDefault(item => item.Id == child.Id);
                    if (script != null && ScriptMatchesFilter(script, filter)) return true;
                }
            }
            return false;
        }

        private static bool ScriptMatchesFilter(ScriptDefinition script, string filter)
        {
            var type = Path.GetExtension(script.Path).TrimStart('.').ToUpperInvariant();
            return script.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                script.Path.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                InterpreterText(script).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private ScriptRuntimeSnapshot AggregateFolderRuntime(Guid folderId, IEnumerable<ScriptDefinition> scripts)
        {
            var snapshots = scripts.Select(script => _supervisor.GetSnapshot(script.Id)).ToArray();
            var state = ScriptRuntimeState.Stopped;
            if (snapshots.Any(item => item.State == ScriptRuntimeState.Stopping)) state = ScriptRuntimeState.Stopping;
            else if (snapshots.Any(item => item.State == ScriptRuntimeState.Starting)) state = ScriptRuntimeState.Starting;
            else if (snapshots.Any(item => item.State == ScriptRuntimeState.Running)) state = ScriptRuntimeState.Running;
            else if (snapshots.Any(item => item.State == ScriptRuntimeState.Failed)) state = ScriptRuntimeState.Failed;
            else if (snapshots.Any(item => item.State == ScriptRuntimeState.Exited)) state = ScriptRuntimeState.Exited;
            return new ScriptRuntimeSnapshot
            {
                ScriptId = folderId,
                State = state,
                ActiveCount = snapshots.Sum(item => item.ActiveCount),
                StartedAt = snapshots.Where(item => item.StartedAt.HasValue).Select(item => item.StartedAt).Min(),
                Error = snapshots.Select(item => item.Error).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty
            };
        }

        private string FolderPath(ScriptFolderDefinition folder)
        {
            var names = ScriptHierarchy.GetAncestorFolderIds(Configuration, folder.ParentFolderId)
                .Select(id => Configuration.Folders.FirstOrDefault(item => item.Id == id)?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            names.Add(folder.Name);
            return string.Join(" / ", names);
        }

        private void AddScript()
        {
            using (var form = new ScriptEditorForm(null, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text,
                Configuration.Application.Theme, Configuration.Folders, SuggestedFolderId))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                ScriptHierarchy.AppendScript(candidate, form.Result, form.Result.FolderId);
                SaveConfiguration(candidate);
            }
        }

        private void AddFolder()
        {
            var parentFolderId = SuggestedFolderId;
            using (var form = new FolderEditorForm(null, parentFolderId, _text, Configuration.Application.Theme))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                ScriptHierarchy.AppendFolder(candidate, form.Result, parentFolderId);
                var parent = candidate.Folders.FirstOrDefault(item => item.Id == parentFolderId);
                if (parent != null) parent.IsExpanded = true;
                SaveConfiguration(candidate);
            }
        }

        private void EditSelected()
        {
            var selectedFolder = SelectedFolder;
            if (selectedFolder != null)
            {
                EditFolder(selectedFolder);
                return;
            }
            var selected = SelectedScript;
            if (selected == null) return;
            using (var form = new ScriptEditorForm(selected, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text,
                Configuration.Application.Theme, Configuration.Folders, selected.FolderId))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                var index = candidate.Scripts.FindIndex(item => item.Id == selected.Id);
                var targetFolderId = form.Result.FolderId;
                form.Result.FolderId = selected.FolderId;
                form.Result.SortOrder = selected.SortOrder;
                candidate.Scripts[index] = form.Result;
                if (!Nullable.Equals(selected.FolderId, targetFolderId))
                    ScriptHierarchy.MoveItem(candidate, HierarchyItemKey.Script(selected.Id), targetFolderId, int.MaxValue);
                SaveConfiguration(candidate);
            }
        }

        private void EditFolder(ScriptFolderDefinition selected)
        {
            using (var form = new FolderEditorForm(selected, selected.ParentFolderId, _text, Configuration.Application.Theme))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                var index = candidate.Folders.FindIndex(item => item.Id == selected.Id);
                candidate.Folders[index] = form.Result;
                SaveConfiguration(candidate);
            }
        }

        private async Task DeleteSelectedAsync()
        {
            var selectedFolder = SelectedFolder;
            if (selectedFolder != null)
            {
                await DeleteFolderAsync(selectedFolder);
                return;
            }
            var selected = SelectedScript;
            if (selected == null) return;
            if (_supervisor.IsRunning(selected.Id))
            {
                MessageBox.Show(this, _text["Main.DeleteRunning"], _text["Main.DeleteTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Configuration.Application.ConfirmBeforeDelete)
            {
                var answer = MessageBox.Show(this, _text.Get("Main.DeleteConfirm", selected.Name), _text["Main.DeleteTitle"],
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            await Task.Yield();
            var candidate = Configuration.Clone();
            candidate.Scripts.RemoveAll(item => item.Id == selected.Id);
            SaveConfiguration(candidate);
        }

        private async Task DeleteFolderAsync(ScriptFolderDefinition folder)
        {
            if (Configuration.Application.ConfirmBeforeDelete)
            {
                var answer = MessageBox.Show(this, _text.Get("Main.DeleteFolderConfirm", folder.Name),
                    _text["Main.DeleteFolderTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            await Task.Yield();
            var candidate = Configuration.Clone();
            ScriptHierarchy.RemoveFolderKeepingContents(candidate, folder.Id);
            SaveConfiguration(candidate);
        }

        private void StartSelected()
        {
            var folder = SelectedFolder;
            if (folder != null)
            {
                StartScripts(ScriptHierarchy.GetDescendantScripts(Configuration, folder.Id));
                return;
            }
            var selected = SelectedScript;
            if (selected == null) return;
            if (!selected.Enabled)
            {
                MessageBox.Show(this, _text["Main.Disabled"], _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { _supervisor.Start(selected, Configuration.PowerShell7Path); }
            catch (Exception exception) { ShowError(_text.Get("Main.StartFailed", selected.Name), exception); }
        }

        private void StartScripts(IEnumerable<ScriptDefinition> scripts)
        {
            var errors = new List<string>();
            foreach (var script in scripts.Where(item => item.Enabled))
            {
                if (_supervisor.IsRunning(script.Id) && !script.Launch.AllowParallelInstances) continue;
                try { _supervisor.Start(script, Configuration.PowerShell7Path); }
                catch (Exception exception) { errors.Add(script.Name + ": " + exception.Message); }
            }
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), _text["Main.RunTitle"],
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async Task StopSelectedAsync()
        {
            var folder = SelectedFolder;
            if (folder != null)
            {
                await StopScriptsAsync(ScriptHierarchy.GetDescendantScripts(Configuration, folder.Id));
                return;
            }
            var selected = SelectedScript;
            if (selected == null) return;
            try { await _supervisor.StopAsync(selected.Id); }
            catch (Exception exception) { ShowError(_text.Get("Main.StopFailed", selected.Name), exception); }
        }

        private async Task StopScriptsAsync(IEnumerable<ScriptDefinition> scripts)
        {
            var errors = new List<string>();
            foreach (var script in scripts.Where(item => _supervisor.IsRunning(item.Id)).Reverse())
            {
                try { await _supervisor.StopAsync(script.Id); }
                catch (Exception exception) { errors.Add(script.Name + ": " + exception.Message); }
            }
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), _text["Main.StopAllFailed"],
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async Task RestartSelectedAsync()
        {
            var folder = SelectedFolder;
            if (folder != null)
            {
                var scripts = ScriptHierarchy.GetDescendantScripts(Configuration, folder.Id).ToArray();
                await StopScriptsAsync(scripts);
                StartScripts(scripts);
                return;
            }
            var selected = SelectedScript;
            if (selected == null) return;
            try
            {
                if (_supervisor.IsRunning(selected.Id)) await _supervisor.StopAsync(selected.Id);
                var current = Configuration.Scripts.FirstOrDefault(item => item.Id == selected.Id);
                if (current == null || !current.Enabled) return;
                _supervisor.Start(current, Configuration.PowerShell7Path);
            }
            catch (Exception exception)
            {
                ShowError(_text.Get("Main.RestartFailed", selected.Name), exception);
            }
        }

        private void RevealRunningScript(Guid scriptId)
        {
            ShowFromTray();
            ExpandAncestorsForScript(scriptId);
            var row = _grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(item => item.Tag is Guid && (Guid)item.Tag == scriptId);
            if (row == null && !string.IsNullOrWhiteSpace(_filter.Text))
            {
                _filter.Text = string.Empty;
                row = _grid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(item => item.Tag is Guid && (Guid)item.Tag == scriptId);
            }
            if (row == null)
            {
                RefreshGrid();
                row = _grid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(item => item.Tag is Guid && (Guid)item.Tag == scriptId);
            }
            if (row != null)
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Name"];
                if (row.Index >= 0) _grid.FirstDisplayedScrollingRowIndex = row.Index;
            }
            _console.SelectScript(scriptId);
        }

        private void ExpandAncestorsForScript(Guid scriptId)
        {
            var script = Configuration.Scripts.FirstOrDefault(item => item.Id == scriptId);
            if (script == null || !script.FolderId.HasValue) return;
            var ancestorIds = ScriptHierarchy.GetAncestorFolderIds(Configuration, script.FolderId);
            if (ancestorIds.All(id => Configuration.Folders.First(item => item.Id == id).IsExpanded)) return;
            var candidate = Configuration.Clone();
            foreach (var id in ancestorIds)
            {
                var folder = candidate.Folders.FirstOrDefault(item => item.Id == id);
                if (folder != null) folder.IsExpanded = true;
            }
            SaveConfiguration(candidate);
        }

        private void EditSelectedFile()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            try { _editor.Edit(selected.Path, Configuration.Application); }
            catch (Exception exception) { ShowError(_text["Main.EditorFailed"], exception); }
        }

        private void ShowSelectedInFolder()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            try { _editor.ShowInFolder(selected.Path); }
            catch (Exception exception) { ShowError(_text["Main.FolderFailed"], exception); }
        }

        private void OpenSettings()
        {
            CaptureWindowPlacement();
            using (var form = new SettingsForm(Configuration.Application, Configuration.PowerShell7Path, Configuration.Localization, _text))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                candidate.Application = form.SettingsResult;
                candidate.PowerShell7Path = form.PowerShell7PathResult;
                candidate.Localization.Language = form.LanguageResult;
                var previousStartup = Configuration.Application.StartWithWindows;
                var previousApplication = Configuration.Application.Clone();
                try
                {
                    _startup.Synchronize(candidate.Application.StartWithWindows);
                    _showAppHotkey.Apply(candidate.Application);
                    _store.Save(candidate);
                    _state.Current = candidate;
                    _console.ApplySettings();
                    ApplyTheme();
                    ApplyConsolePaneHeight();
                    MessageBox.Show(this, _text["Main.SettingsSaved"], _text["Main.Settings"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception exception)
                {
                    try { _startup.Synchronize(previousStartup); }
                    catch (Exception rollbackException) { _log.Error("Unable to roll back the startup registration.", rollbackException); }
                    try { _showAppHotkey.Apply(previousApplication); }
                    catch (Exception rollbackException) { _log.Error("Unable to roll back the Show App Hotkey.", rollbackException); }
                    ShowError(_text["Main.SettingsSaveFailed"], LocalizeHotkeyException(exception));
                }
            }
        }

        private void ReloadConfiguration()
        {
            if (_supervisor.HasRunningProcesses)
            {
                MessageBox.Show(this, _text["Main.ReloadRunning"], _text["Main.Reload"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var previousStartup = Configuration.Application.StartWithWindows;
            var previousApplication = Configuration.Application.Clone();
            try
            {
                var reloaded = _store.Reload();
                _startup.Synchronize(reloaded.Application.StartWithWindows);
                _showAppHotkey.Apply(reloaded.Application);
                _state.Current = reloaded;
                _console.ApplySettings();
                ApplyTheme();
                ApplyWindowPlacement();
                ApplyConsolePaneHeight();
                _log.Information("Configuration reloaded from disk.");
            }
            catch (Exception exception)
            {
                try { _startup.Synchronize(previousStartup); }
                catch (Exception rollbackException) { _log.Error("Unable to roll back the startup registration.", rollbackException); }
                try { _showAppHotkey.Apply(previousApplication); }
                catch (Exception rollbackException) { _log.Error("Unable to roll back the Show App Hotkey.", rollbackException); }
                ShowError(_text["Main.ReloadFailed"], LocalizeHotkeyException(exception));
            }
        }

        private Exception LocalizeHotkeyException(Exception exception)
        {
            var registration = exception as ShowAppHotkeyRegistrationException;
            return registration == null
                ? exception
                : new InvalidOperationException(
                    _text.Get("Settings.GlobalHotkeyUnavailable",
                        _text["Hotkey." + registration.Action], registration.Gesture), registration);
        }

        private void SaveConfiguration(AppConfiguration candidate)
        {
            try { _store.Save(candidate); _state.Current = candidate; }
            catch (Exception exception) { ShowError(_text["Main.SaveFailed"], exception); }
        }

        private void HandleStateChanged(object sender, ScriptStateChangedEventArgs args)
        {
            if (!Configuration.Scripts.Any(item => item.Id == args.Snapshot.ScriptId)) return;
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshGrid);
        }

        private void HandleGridSelectionChanged(object sender, EventArgs args)
        {
            if (_refreshingGrid) return;
            UpdateButtons();
            var selected = SelectedScript;
            if (selected != null) _console.SelectScript(selected.Id);
        }

        private void HandleGridCellPainting(object sender, DataGridViewCellPaintingEventArgs args)
        {
            if (args.RowIndex < 0 || args.ColumnIndex != _grid.Columns["Name"].Index) return;
            var row = _grid.Rows[args.RowIndex];
            var metadata = row.Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata == null) return;

            args.PaintBackground(args.ClipBounds, true);
            var textColor = row.Selected ? _palette.SelectionText : row.DefaultCellStyle.ForeColor;
            if (textColor.IsEmpty) textColor = _palette.Text;
            var hierarchyLeft = args.CellBounds.Left + 8 + metadata.Depth * 18;
            var x = args.CellBounds.Left + HierarchyNameTextOffset(metadata);
            var centerY = args.CellBounds.Top + args.CellBounds.Height / 2;
            if (metadata.Item.Kind == HierarchyItemKind.Folder)
            {
                using (var pen = new Pen(row.Selected ? _palette.SelectionText : _palette.MutedText, 1.5f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    if (metadata.IsExpanded)
                    {
                        args.Graphics.DrawLine(pen, hierarchyLeft + 2, centerY - 2, hierarchyLeft + 6, centerY + 2);
                        args.Graphics.DrawLine(pen, hierarchyLeft + 6, centerY + 2, hierarchyLeft + 10, centerY - 2);
                    }
                    else
                    {
                        args.Graphics.DrawLine(pen, hierarchyLeft + 3, centerY - 4, hierarchyLeft + 7, centerY);
                        args.Graphics.DrawLine(pen, hierarchyLeft + 7, centerY, hierarchyLeft + 3, centerY + 4);
                    }
                }
                var folder = Configuration.Folders.FirstOrDefault(item => item.Id == metadata.Item.Id);
                var iconColor = FolderIconRenderer.ParseColor(folder?.IconColor, _palette.Accent);
                FolderIconRenderer.Draw(args.Graphics, new Rectangle(hierarchyLeft + 15, centerY - 9, 18, 18),
                    folder?.Icon ?? FolderIconKind.Folder, iconColor);
            }
            var textBounds = new Rectangle(x, args.CellBounds.Top,
                Math.Max(1, args.CellBounds.Right - x - 6), args.CellBounds.Height);
            TextRenderer.DrawText(args.Graphics, Convert.ToString(args.FormattedValue),
                args.CellStyle.Font,
                textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            args.Paint(args.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
            args.Handled = true;
        }

        private static int HierarchyNameTextOffset(HierarchyRowMetadata metadata)
        {
            if (metadata == null) return 8;
            return 8 + metadata.Depth * 18 +
                (metadata.Item.Kind == HierarchyItemKind.Folder ? 39 : 2);
        }

        private void HandleGridCellMouseClick(object sender, DataGridViewCellMouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || args.RowIndex < 0 ||
                args.ColumnIndex != _grid.Columns["Name"].Index) return;
            var row = _grid.Rows[args.RowIndex];
            var metadata = row.Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata == null || metadata.Item.Kind != HierarchyItemKind.Folder) return;
            var chevronLeft = 8 + metadata.Depth * 18;
            if (args.X >= chevronLeft - 2 && args.X <= chevronLeft + 13) ToggleFolder(metadata.Item.Id, null);
        }

        private void HandleGridCellDoubleClick(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex < 0) return;
            var metadata = _grid.Rows[args.RowIndex].Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata != null && metadata.Item.Kind == HierarchyItemKind.Folder)
                ToggleFolder(metadata.Item.Id, null);
            else
                EditSelected();
        }

        private void ToggleFolder(Guid folderId, bool? expanded)
        {
            var candidate = Configuration.Clone();
            var folder = candidate.Folders.FirstOrDefault(item => item.Id == folderId);
            if (folder == null) return;
            folder.IsExpanded = expanded ?? !folder.IsExpanded;
            SaveConfiguration(candidate);
        }

        private void HandleGridMouseDown(object sender, MouseEventArgs args)
        {
            _dragStart = Point.Empty;
            _draggedItem = null;
            if (args.Button != MouseButtons.Left) return;
            var hit = _grid.HitTest(args.X, args.Y);
            if (hit.RowIndex < 0) return;
            var row = _grid.Rows[hit.RowIndex];
            var metadata = row.Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata == null) return;
            _grid.CurrentCell = row.Cells[Math.Max(0, hit.ColumnIndex)];
            row.Selected = true;
            _dragStart = args.Location;
            _draggedItem = metadata.Item;
        }

        private void HandleGridMouseMove(object sender, MouseEventArgs args)
        {
            if (_draggedItem == null || _dragStart == Point.Empty || (args.Button & MouseButtons.Left) == 0) return;
            var dragSize = SystemInformation.DragSize;
            var dragBounds = new Rectangle(_dragStart.X - dragSize.Width / 2,
                _dragStart.Y - dragSize.Height / 2, dragSize.Width, dragSize.Height);
            if (dragBounds.Contains(args.Location)) return;
            var item = _draggedItem;
            try
            {
                _grid.DoDragDrop(item, DragDropEffects.Move);
            }
            finally
            {
                _dragStart = Point.Empty;
                _draggedItem = null;
                ClearDropIndicator();
            }
        }

        private void HandleGridDragOver(object sender, DragEventArgs args)
        {
            var item = args.Data.GetData(typeof(HierarchyItemKey)) as HierarchyItemKey;
            if (item == null)
            {
                args.Effect = DragDropEffects.None;
                ClearDropIndicator();
                return;
            }

            var point = _grid.PointToClient(new Point(args.X, args.Y));
            AutoScrollGrid(point);
            var indicator = CalculateDropIndicator(point, item);
            if (indicator == null || !CanDrop(item, indicator.ParentFolderId))
            {
                args.Effect = DragDropEffects.None;
                ClearDropIndicator();
                return;
            }

            args.Effect = DragDropEffects.Move;
            HandleDragHoverExpansion(indicator.HoverFolderId);
            SetDropIndicator(indicator);
        }

        private void HandleGridDragDrop(object sender, DragEventArgs args)
        {
            var item = args.Data.GetData(typeof(HierarchyItemKey)) as HierarchyItemKey;
            var indicator = _dropIndicator;
            ClearDropIndicator();
            if (item == null || indicator == null || !CanDrop(item, indicator.ParentFolderId)) return;
            try
            {
                var candidate = Configuration.Clone();
                ScriptHierarchy.MoveItem(candidate, item, indicator.ParentFolderId, indicator.InsertIndex);
                var parent = candidate.Folders.FirstOrDefault(folder => folder.Id == indicator.ParentFolderId);
                if (parent != null) parent.IsExpanded = true;
                SaveConfiguration(candidate);
            }
            catch (Exception exception)
            {
                ShowError(_text["Main.MoveFailed"], exception);
            }
        }

        private void HandleGridDragLeave(object sender, EventArgs args)
        {
            ClearDropIndicator();
        }

        private void HandleGridPaint(object sender, PaintEventArgs args)
        {
            var indicator = _dropIndicator;
            if (indicator == null) return;
            var startX = Math.Max(2, indicator.LineX);
            var endX = Math.Max(startX + 12, _grid.ClientSize.Width - 4);
            using (var pen = new Pen(_palette.Accent, 2f))
            using (var brush = new SolidBrush(_palette.Accent))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                args.Graphics.DrawLine(pen, startX, indicator.LineY, endX, indicator.LineY);
                args.Graphics.FillEllipse(brush, startX - 3, indicator.LineY - 3, 6, 6);
            }
        }

        private HierarchyDropIndicator CalculateDropIndicator(Point point, HierarchyItemKey draggedItem)
        {
            var nameColumn = _grid.Columns["Name"];
            if (_grid.Rows.Count == 0)
                return new HierarchyDropIndicator(null, 0, _grid.ColumnHeadersHeight + 1, nameColumn.DisplayIndex + 8, null);

            var hit = _grid.HitTest(point.X, point.Y);
            if (hit.RowIndex < 0)
            {
                if (point.Y <= _grid.ColumnHeadersHeight) return null;
                var lastRow = _grid.Rows[_grid.Rows.Count - 1];
                var lastBounds = _grid.GetRowDisplayRectangle(lastRow.Index, false);
                return new HierarchyDropIndicator(null,
                    CountChildrenExcluding(null, draggedItem), lastBounds.Bottom - 1,
                    _grid.GetCellDisplayRectangle(nameColumn.Index, lastRow.Index, false).Left + 8, null);
            }

            var row = _grid.Rows[hit.RowIndex];
            var metadata = row.Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata == null) return null;
            var rowBounds = _grid.GetRowDisplayRectangle(row.Index, false);
            var nameBounds = _grid.GetCellDisplayRectangle(nameColumn.Index, row.Index, false);
            var relativeY = rowBounds.Height <= 0 ? 0.5f : (point.Y - rowBounds.Top) / (float)rowBounds.Height;

            if (metadata.Item.Kind == HierarchyItemKind.Folder && relativeY >= 0.27f && relativeY <= 0.73f)
            {
                var bottomIndex = LastVisibleDescendantRow(row.Index, metadata.Depth);
                var bottomBounds = _grid.GetRowDisplayRectangle(bottomIndex, false);
                return new HierarchyDropIndicator(metadata.Item.Id,
                    CountChildrenExcluding(metadata.Item.Id, draggedItem), bottomBounds.Bottom - 1,
                    nameBounds.Left + 8 + (metadata.Depth + 1) * 18, metadata.Item.Id);
            }

            var outdentThreshold = nameBounds.Left + 8 + metadata.Depth * 18 - 5;
            if (metadata.Depth > 0 && point.X < outdentThreshold && metadata.Ancestors.Count > 0)
            {
                var desiredDepth = Math.Max(0, Math.Min(metadata.Depth - 1,
                    (point.X - nameBounds.Left - 8) / 18));
                var anchorFolderId = metadata.Ancestors[desiredDepth];
                var parentFolderId = desiredDepth == 0 ? (Guid?)null : metadata.Ancestors[desiredDepth - 1];
                var anchor = HierarchyItemKey.Folder(anchorFolderId);
                var insertIndex = IndexAfter(parentFolderId, anchor, draggedItem);
                var anchorRow = FindFolderRow(anchorFolderId);
                var lineRowIndex = anchorRow == null ? row.Index : LastVisibleDescendantRow(anchorRow.Index, desiredDepth);
                var lineBounds = _grid.GetRowDisplayRectangle(lineRowIndex, false);
                return new HierarchyDropIndicator(parentFolderId, insertIndex, lineBounds.Bottom - 1,
                    nameBounds.Left + 8 + desiredDepth * 18, null);
            }

            var after = relativeY >= 0.5f;
            var lineIndex = after && metadata.Item.Kind == HierarchyItemKind.Folder
                ? LastVisibleDescendantRow(row.Index, metadata.Depth)
                : row.Index;
            var lineBoundsNormal = _grid.GetRowDisplayRectangle(lineIndex, false);
            return new HierarchyDropIndicator(metadata.ParentFolderId,
                IndexRelativeTo(metadata.ParentFolderId, metadata.Item, draggedItem, after),
                after ? lineBoundsNormal.Bottom - 1 : rowBounds.Top,
                nameBounds.Left + 8 + metadata.Depth * 18, null);
        }

        private int IndexRelativeTo(Guid? parentFolderId, HierarchyItemKey anchor, HierarchyItemKey draggedItem, bool after)
        {
            var children = ScriptHierarchy.GetChildren(Configuration, parentFolderId).ToList();
            var anchorIndex = children.FindIndex(item => item.Equals(anchor));
            if (anchorIndex < 0) return CountChildrenExcluding(parentFolderId, draggedItem);
            var boundary = anchorIndex + (after ? 1 : 0);
            return children.Take(boundary).Count(item => !item.Equals(draggedItem));
        }

        private int IndexAfter(Guid? parentFolderId, HierarchyItemKey anchor, HierarchyItemKey draggedItem)
        {
            return IndexRelativeTo(parentFolderId, anchor, draggedItem, true);
        }

        private int CountChildrenExcluding(Guid? parentFolderId, HierarchyItemKey draggedItem)
        {
            return ScriptHierarchy.GetChildren(Configuration, parentFolderId).Count(item => !item.Equals(draggedItem));
        }

        private int LastVisibleDescendantRow(int rowIndex, int depth)
        {
            var last = rowIndex;
            for (var index = rowIndex + 1; index < _grid.Rows.Count; index++)
            {
                var metadata = _grid.Rows[index].Cells["Name"].Tag as HierarchyRowMetadata;
                if (metadata == null || metadata.Depth <= depth) break;
                last = index;
            }
            return last;
        }

        private DataGridViewRow FindFolderRow(Guid folderId)
        {
            return _grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(row => (row.Tag as FolderGridRowTag)?.Id == folderId);
        }

        private bool CanDrop(HierarchyItemKey item, Guid? parentFolderId)
        {
            if (item.Kind != HierarchyItemKind.Folder) return true;
            if (parentFolderId == item.Id) return false;
            return !parentFolderId.HasValue || !ScriptHierarchy.IsDescendantFolder(Configuration,
                parentFolderId.Value, item.Id);
        }

        private void HandleDragHoverExpansion(Guid? folderId)
        {
            if (!folderId.HasValue)
            {
                _dragHoverFolderId = null;
                return;
            }
            var folder = Configuration.Folders.FirstOrDefault(item => item.Id == folderId.Value);
            if (folder == null || folder.IsExpanded)
            {
                _dragHoverFolderId = null;
                return;
            }
            if (_dragHoverFolderId != folderId)
            {
                _dragHoverFolderId = folderId;
                _dragHoverStartedUtc = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - _dragHoverStartedUtc < TimeSpan.FromMilliseconds(650)) return;
            folder.IsExpanded = true;
            try
            {
                _store.Save(Configuration);
                RefreshGrid();
            }
            catch (Exception exception)
            {
                folder.IsExpanded = false;
                _log.Warning("Unable to persist drag-hover folder expansion: " + exception.Message);
            }
            _dragHoverFolderId = null;
        }

        private void AutoScrollGrid(Point point)
        {
            if (_grid.Rows.Count == 0) return;
            if (point.Y < _grid.ColumnHeadersHeight + 22 && _grid.FirstDisplayedScrollingRowIndex > 0)
                _grid.FirstDisplayedScrollingRowIndex--;
            else if (point.Y > _grid.ClientSize.Height - 22)
            {
                var first = _grid.FirstDisplayedScrollingRowIndex;
                var displayed = _grid.DisplayedRowCount(false);
                if (first >= 0 && first + displayed < _grid.Rows.Count)
                    _grid.FirstDisplayedScrollingRowIndex++;
            }
        }

        private void ClearDropIndicator()
        {
            var previous = _dropIndicator;
            _dropIndicator = null;
            _dragHoverFolderId = null;
            InvalidateDropIndicator(previous);
        }

        private void SetDropIndicator(HierarchyDropIndicator indicator)
        {
            if (DropIndicatorsEqual(_dropIndicator, indicator)) return;
            var previous = _dropIndicator;
            _dropIndicator = indicator;
            InvalidateDropIndicator(previous);
            InvalidateDropIndicator(indicator);
        }

        private void InvalidateDropIndicator(HierarchyDropIndicator indicator)
        {
            if (indicator == null || _grid.IsDisposed) return;
            _grid.Invalidate(new Rectangle(0, Math.Max(0, indicator.LineY - 4),
                Math.Max(1, _grid.ClientSize.Width), 9));
        }

        private static bool DropIndicatorsEqual(HierarchyDropIndicator first, HierarchyDropIndicator second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            return first.ParentFolderId == second.ParentFolderId &&
                first.InsertIndex == second.InsertIndex &&
                first.LineY == second.LineY && first.LineX == second.LineX &&
                first.HoverFolderId == second.HoverFolderId;
        }

        private void ApplyRuntimeVisual(DataGridViewRow row, ScriptDefinition script, ScriptRuntimeSnapshot runtime)
        {
            ApplyRuntimeVisualCore(row, runtime, script.Enabled, null);
        }

        private void ApplyFolderRuntimeVisual(DataGridViewRow row, ScriptRuntimeSnapshot runtime, bool empty)
        {
            ApplyRuntimeVisualCore(row, runtime, true, empty ? _text["Main.Folder.Empty"] : null);
        }

        private void ApplyRuntimeVisualCore(DataGridViewRow row, ScriptRuntimeSnapshot runtime, bool enabled,
            string stateTextOverride)
        {
            var active = runtime.State == ScriptRuntimeState.Starting ||
                runtime.State == ScriptRuntimeState.Running || runtime.State == ScriptRuntimeState.Stopping;
            var rowColor = enabled || active ? _palette.Text : _palette.DisabledText;
            if (row.DefaultCellStyle.ForeColor != rowColor) row.DefaultCellStyle.ForeColor = rowColor;

            var stateText = stateTextOverride ?? StateText(runtime);
            SetCellText(row.Cells["State"], stateText);
            SetCellText(row.Cells["Pid"], runtime.ProcessId?.ToString() ?? "-");
            SetCellText(row.Cells["Started"], runtime.StartedAt?.ToString("g") ?? "-");
            SetCellText(row.Cells["ExitCode"], runtime.LastExitCode?.ToString() ?? "-");

            Color backgroundColor;
            switch (runtime.State)
            {
                case ScriptRuntimeState.Starting:
                    backgroundColor = _palette.StartingBackground;
                    break;
                case ScriptRuntimeState.Running:
                    backgroundColor = _palette.RunningBackground;
                    break;
                case ScriptRuntimeState.Stopping:
                    backgroundColor = _palette.StoppingBackground;
                    break;
                case ScriptRuntimeState.Failed:
                    backgroundColor = _palette.FailedBackground;
                    break;
                default:
                    backgroundColor = row.Index % 2 == 0 ? _palette.Surface : _palette.SurfaceAlternate;
                    break;
            }
            if (row.DefaultCellStyle.BackColor != backgroundColor)
                row.DefaultCellStyle.BackColor = backgroundColor;

            var indicator = row.Cells["Activity"];
            SetCellText(indicator, ActivityGlyph(runtime.State));
            var indicatorColor = ActivityColor(runtime.State);
            if (indicator.Style.ForeColor != indicatorColor) indicator.Style.ForeColor = indicatorColor;
            if (indicator.Style.SelectionForeColor != indicatorColor) indicator.Style.SelectionForeColor = indicatorColor;
            if (!string.Equals(indicator.ToolTipText, stateText, StringComparison.Ordinal))
                indicator.ToolTipText = stateText;

            var state = row.Cells["State"];
            var stateToolTip = runtime.State == ScriptRuntimeState.Failed && !string.IsNullOrWhiteSpace(runtime.Error)
                ? runtime.Error
                : stateText;
            if (!string.Equals(state.ToolTipText, stateToolTip, StringComparison.Ordinal))
                state.ToolTipText = stateToolTip;
        }

        private static void SetCellText(DataGridViewCell cell, string value)
        {
            if (!string.Equals(Convert.ToString(cell.Value), value, StringComparison.Ordinal))
                cell.Value = value;
        }

        private static string ActivityGlyph(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                    return "◐";
                case ScriptRuntimeState.Stopping:
                    return "◓";
                case ScriptRuntimeState.Running:
                    return "●";
                case ScriptRuntimeState.Failed:
                    return "●";
                default:
                    return "○";
            }
        }

        private static Color ActivityColor(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                    return Color.Goldenrod;
                case ScriptRuntimeState.Running:
                    return Color.FromArgb(24, 160, 88);
                case ScriptRuntimeState.Stopping:
                    return Color.DarkOrange;
                case ScriptRuntimeState.Failed:
                    return Color.Firebrick;
                default:
                    return Color.Gray;
            }
        }
        private void HandleOutputReceived(object sender, ScriptOutputEventArgs args) { _console.EnqueueOutput(args); }
        private void HandleInstanceStarted(object sender, ScriptInstanceEventArgs args) { _console.EnqueueStarted(args); }
        private void HandleInstanceExited(object sender, ScriptInstanceEventArgs args) { _console.EnqueueExited(args); }

        private async void HandleConsoleCloseRequested(object sender, ConsoleTabCloseRequestedEventArgs args)
        {
            if (!args.IsRunning) return;
            try { await _supervisor.StopInstanceAsync(args.ScriptId, args.ProcessId); }
            catch (Exception exception) { ShowError(_text["Console.StopFailed"], exception); }
        }

        private void HandleConsolePaneMaximizeRequested(object sender, EventArgs args)
        {
            ToggleConsolePaneMaximized();
        }

        private void HandleConsoleWordWrapChanged(object sender, ConsoleWordWrapChangedEventArgs args)
        {
            var owner = ResolveWordWrapOwner(args.ScriptId);
            var script = Configuration.Scripts.FirstOrDefault(item => item.Id == owner);
            if (script == null) return;

            script.Launch.WordWrap = args.WordWrap;
            var related = _managedChildParents.Where(pair => pair.Value == owner)
                .Select(pair => pair.Key)
                .Concat(new[] { owner });
            _console.ApplyWordWrap(related, args.WordWrap);
            try { _store.Save(Configuration); }
            catch (Exception exception)
            {
                _log.Warning("Unable to persist the console word-wrap setting: " + exception.Message);
            }
        }

        private bool ResolveConsoleWordWrap(Guid scriptId)
        {
            var owner = ResolveWordWrapOwner(scriptId);
            var script = Configuration.Scripts.FirstOrDefault(item => item.Id == owner);
            return script?.Launch.WordWrap ?? Configuration.Defaults.WordWrap;
        }

        private ConsoleLaunchBehavior ResolveConsoleLaunchBehavior(Guid scriptId)
        {
            var owner = ResolveWordWrapOwner(scriptId);
            return Configuration.Scripts.FirstOrDefault(item => item.Id == owner)?.Launch.ConsoleLaunchBehavior
                ?? ConsoleLaunchBehavior.Inherit;
        }

        private Guid ResolveWordWrapOwner(Guid scriptId)
        {
            Guid owner;
            return scriptId != Guid.Empty && _managedChildParents.TryGetValue(scriptId, out owner)
                ? owner
                : scriptId;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (_grid.ContainsFocus && (keyData & Keys.Modifiers) == Keys.None &&
                ((keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right) &&
                HandleHierarchyArrowKey(keyData & Keys.KeyCode))
                return true;
            if (TryHandleApplicationHotkey(keyData)) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }

        private bool HandleHierarchyArrowKey(Keys keyCode)
        {
            if (_grid.SelectedRows.Count == 0) return false;
            var metadata = _grid.SelectedRows[0].Cells["Name"].Tag as HierarchyRowMetadata;
            if (metadata == null) return false;
            if (keyCode == Keys.Right && metadata.Item.Kind == HierarchyItemKind.Folder && !metadata.IsExpanded)
            {
                ToggleFolder(metadata.Item.Id, true);
                return true;
            }
            if (keyCode == Keys.Left && metadata.Item.Kind == HierarchyItemKind.Folder && metadata.IsExpanded)
            {
                ToggleFolder(metadata.Item.Id, false);
                return true;
            }
            if (keyCode == Keys.Left && metadata.ParentFolderId.HasValue)
            {
                var row = FindFolderRow(metadata.ParentFolderId.Value);
                if (row == null) return false;
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Name"];
                return true;
            }
            return false;
        }

        private bool TryHandleApplicationHotkey(Keys keyData)
        {
            if (_filter.TextBox.Focused && (keyData & Keys.KeyCode) == Keys.Delete &&
                (keyData & Keys.Modifiers) == Keys.None)
                return false;

            if (MatchesHotkey(HotkeyAction.StartSelected, keyData)) { StartSelected(); return true; }
            if (MatchesHotkey(HotkeyAction.StopSelected, keyData)) { _ = StopSelectedAsync(); return true; }
            if (MatchesHotkey(HotkeyAction.RestartSelected, keyData)) { _ = RestartSelectedAsync(); return true; }
            if (MatchesHotkey(HotkeyAction.AddScript, keyData)) { AddScript(); return true; }
            if (MatchesHotkey(HotkeyAction.EditScript, keyData)) { EditSelected(); return true; }
            if (MatchesHotkey(HotkeyAction.DeleteScript, keyData)) { _ = DeleteSelectedAsync(); return true; }
            if (MatchesHotkey(HotkeyAction.OpenSettings, keyData)) { OpenSettings(); return true; }
            if (MatchesHotkey(HotkeyAction.NextConsoleTab, keyData)) return _console.SelectAdjacentTab(1);
            if (MatchesHotkey(HotkeyAction.PreviousConsoleTab, keyData)) return _console.SelectAdjacentTab(-1);
            if (MatchesHotkey(HotkeyAction.CloseConsoleTab, keyData)) return _console.CloseActiveTab();
            if (MatchesHotkey(HotkeyAction.ToggleConsoleDetach, keyData)) return _console.ToggleActiveTabDetached();
            if (MatchesHotkey(HotkeyAction.ToggleConsolePane, keyData))
            {
                ToggleConsolePaneMaximized();
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ToggleConsoleFullScreen, keyData))
                return _console.ToggleSelectedTabFullScreen();
            return false;
        }

        private bool MatchesHotkey(HotkeyAction action, Keys keyData)
        {
            var binding = Configuration.Application.Hotkeys[action];
            if (!binding.Enabled) return false;
            ShowAppHotkeyGesture gesture;
            return ShowAppHotkeyGesture.TryParse(binding.Gesture, false, out gesture) && gesture.Matches(keyData);
        }

        private void HandleMainResize(object sender, EventArgs args)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                return;
            }

            var stateChanged = _lastNonMinimizedWindowState != WindowState;
            _lastNonMinimizedWindowState = WindowState;
            if (stateChanged) ScheduleWindowPlacementSave();
        }

        private void HandleMainShown(object sender, EventArgs args)
        {
            _restoringWindowPlacement = true;
            try
            {
                if (_restoreWindowMaximized) WindowState = FormWindowState.Maximized;
                _lastNonMinimizedWindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Maximized
                    : FormWindowState.Normal;
            }
            finally
            {
                _restoringWindowPlacement = false;
            }

            ApplyConsolePaneHeight();
            _windowPlacementReady = true;
            ScheduleWindowPlacementSave();
        }

        private void HandleWindowResizeEnd(object sender, EventArgs args)
        {
            ScheduleWindowPlacementSave();
        }

        private void HandleSplitSizeChanged(object sender, EventArgs args)
        {
            if (!_consolePaneMaximized || _restoringPaneLayout || _mainSplit.Height <= 0) return;
            _restoringPaneLayout = true;
            try { _mainSplit.SplitterDistance = _mainSplit.Panel1MinSize; }
            finally { _restoringPaneLayout = false; }
        }

        private void ToggleConsolePaneMaximized()
        {
            if (_mainSplit.Height <= 0) return;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                if (_consolePaneMaximized)
                {
                    _consolePaneMaximized = false;
                    UpdateScriptPanelMinimum();
                    SetConsolePaneHeight(_normalConsolePaneHeight > 0
                        ? _normalConsolePaneHeight
                        : Configuration.Application.ConsolePaneHeight);
                }
                else
                {
                    _normalConsolePaneHeight = Math.Max(_mainSplit.Panel2MinSize, _mainSplit.Panel2.Height);
                    _mainSplit.Panel1MinSize = OneScriptPanelHeight;
                    _mainSplit.SplitterDistance = _mainSplit.Panel1MinSize;
                    _consolePaneMaximized = true;
                }
                _console.SetPaneMaximized(_consolePaneMaximized);
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
            if (!_consolePaneMaximized) SchedulePaneHeightSave();
        }

        private void ApplyConsolePaneHeight()
        {
            if (_mainSplit.Height <= 0) return;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                _consolePaneMaximized = false;
                UpdateScriptPanelMinimum();
                SetConsolePaneHeight(Configuration.Application.ConsolePaneHeight);
                _normalConsolePaneHeight = _mainSplit.Panel2.Height;
                _console.SetPaneMaximized(false);
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
        }

        private void SetConsolePaneHeight(int requestedHeight)
        {
            var maximum = Math.Max(_mainSplit.Panel2MinSize,
                _mainSplit.Height - _mainSplit.SplitterWidth - _mainSplit.Panel1MinSize);
            var height = Math.Max(_mainSplit.Panel2MinSize, Math.Min(maximum, requestedHeight));
            _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize,
                _mainSplit.Height - _mainSplit.SplitterWidth - height);
        }

        private void HandleSplitterMoved(object sender, SplitterEventArgs args)
        {
            if (_restoringPaneLayout) return;
            if (_consolePaneMaximized && _mainSplit.SplitterDistance > OneScriptPanelHeight + 1)
            {
                if (Control.MouseButtons == MouseButtons.Left)
                {
                    _consolePaneMaximized = false;
                    UpdateScriptPanelMinimum();
                }
                else
                {
                    _restoringPaneLayout = true;
                    try { _mainSplit.SplitterDistance = OneScriptPanelHeight; }
                    finally { _restoringPaneLayout = false; }
                    return;
                }
            }
            _console.SetPaneMaximized(_consolePaneMaximized);
            if (_consolePaneMaximized) return;
            _normalConsolePaneHeight = _mainSplit.Panel2.Height;
            SchedulePaneHeightSave();
        }

        private void SchedulePaneHeightSave()
        {
            Configuration.Application.ConsolePaneHeight = Math.Max(_mainSplit.Panel2MinSize,
                _normalConsolePaneHeight);
            ScheduleLayoutSave();
        }

        private void ScheduleWindowPlacementSave()
        {
            if (!_windowPlacementReady || _restoringWindowPlacement || WindowState == FormWindowState.Minimized) return;
            CaptureWindowPlacement();
            ScheduleLayoutSave();
        }

        private void ScheduleLayoutSave()
        {
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void HandleLayoutSaveTimer(object sender, EventArgs args)
        {
            _layoutSaveTimer.Stop();
            SaveLayoutSilently();
        }

        private int OneScriptPanelHeight => Math.Max(48,
            _grid.ColumnHeadersHeight + _grid.RowTemplate.Height + 4);

        private void UpdateScriptPanelMinimum()
        {
            if (_mainSplit == null || _mainSplit.Height <= 0) return;
            var maximum = Math.Max(OneScriptPanelHeight,
                _mainSplit.Height - _mainSplit.SplitterWidth - _mainSplit.Panel2MinSize);
            var rowsHeight = Math.Max(1, _grid.Rows.Count) * _grid.RowTemplate.Height;
            var normalMinimum = Math.Min(maximum,
                Math.Max(OneScriptPanelHeight, _grid.ColumnHeadersHeight + rowsHeight + 4));
            var target = _consolePaneMaximized ? OneScriptPanelHeight : normalMinimum;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                _mainSplit.Panel1MinSize = OneScriptPanelHeight;
                if (_mainSplit.SplitterDistance < target) _mainSplit.SplitterDistance = target;
                _mainSplit.Panel1MinSize = target;
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
        }

        private void ApplyWindowPlacement()
        {
            var settings = Configuration.Application;
            _restoreWindowMaximized = settings.MainWindowMaximized;
            _lastNonMinimizedWindowState = _restoreWindowMaximized
                ? FormWindowState.Maximized
                : FormWindowState.Normal;
            if (!settings.MainWindowPlacementSaved) return;

            var requested = new Rectangle(settings.MainWindowX, settings.MainWindowY,
                settings.MainWindowWidth, settings.MainWindowHeight);
            var visible = KeepWindowOnScreen(requested);
            _restoringWindowPlacement = true;
            try
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                StartPosition = FormStartPosition.Manual;
                Bounds = visible;
                if (_windowPlacementReady && _restoreWindowMaximized) WindowState = FormWindowState.Maximized;
            }
            finally
            {
                _restoringWindowPlacement = false;
            }
        }

        private Rectangle KeepWindowOnScreen(Rectangle requested)
        {
            var workingArea = Screen.FromRectangle(requested).WorkingArea;
            var width = Math.Max(Math.Min(MinimumSize.Width, workingArea.Width),
                Math.Min(requested.Width, workingArea.Width));
            var height = Math.Max(Math.Min(MinimumSize.Height, workingArea.Height),
                Math.Min(requested.Height, workingArea.Height));
            var maximumX = Math.Max(workingArea.Left, workingArea.Right - width);
            var maximumY = Math.Max(workingArea.Top, workingArea.Bottom - height);
            var x = Math.Max(workingArea.Left, Math.Min(maximumX, requested.X));
            var y = Math.Max(workingArea.Top, Math.Min(maximumY, requested.Y));
            return new Rectangle(x, y, width, height);
        }

        private void EnsureRestoredWindowOnScreen()
        {
            var normalBounds = WindowState == FormWindowState.Maximized ? RestoreBounds : Bounds;
            if (IsWindowOnScreen(normalBounds)) return;

            var settings = Configuration.Application;
            Rectangle requested;
            if (settings.MainWindowPlacementSaved)
            {
                requested = new Rectangle(settings.MainWindowX, settings.MainWindowY,
                    settings.MainWindowWidth, settings.MainWindowHeight);
            }
            else
            {
                var workingArea = Screen.PrimaryScreen.WorkingArea;
                var width = Math.Min(Math.Max(MinimumSize.Width, Width), workingArea.Width);
                var height = Math.Min(Math.Max(MinimumSize.Height, Height), workingArea.Height);
                requested = new Rectangle(
                    workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2),
                    workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2),
                    width, height);
            }

            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            Bounds = KeepWindowOnScreen(requested);
        }

        private static bool IsWindowOnScreen(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            var requiredWidth = Math.Min(120, bounds.Width);
            var requiredHeight = Math.Min(40, bounds.Height);
            return Screen.AllScreens.Any(screen =>
            {
                var intersection = Rectangle.Intersect(screen.WorkingArea, bounds);
                return intersection.Width >= requiredWidth && intersection.Height >= requiredHeight;
            });
        }

        private void CaptureWindowPlacement()
        {
            if (!_windowPlacementReady || _restoringWindowPlacement) return;
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height ||
                !IsWindowOnScreen(bounds)) return;

            var settings = Configuration.Application;
            settings.MainWindowPlacementSaved = true;
            settings.MainWindowX = bounds.X;
            settings.MainWindowY = bounds.Y;
            settings.MainWindowWidth = bounds.Width;
            settings.MainWindowHeight = bounds.Height;
            settings.MainWindowMaximized = _lastNonMinimizedWindowState == FormWindowState.Maximized;
        }

        private void PersistLayoutNow()
        {
            _layoutSaveTimer.Stop();
            if (!_consolePaneMaximized && _normalConsolePaneHeight > 0)
                Configuration.Application.ConsolePaneHeight = _normalConsolePaneHeight;
            CaptureWindowPlacement();
            SaveLayoutSilently();
        }

        private void SaveLayoutSilently()
        {
            try
            {
                _store.Save(Configuration);
            }
            catch (Exception exception)
            {
                _log.Warning("Unable to persist the window layout: " + exception.Message);
            }
        }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyLocalization);
            else ApplyLocalization();
        }

        private void HandleSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (Configuration.Application.Theme != ApplicationTheme.System || IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyTheme);
            else ApplyTheme();
        }

        private void ApplyTheme()
        {
            _palette = AppThemeManager.Resolve(Configuration.Application.Theme);
            AppThemeManager.ApplyWindow(this, Configuration.Application.Theme);
            AppThemeManager.ApplyWindowCorners(this);
            AppThemeManager.ApplyToolStrip(_toolbar, _palette);
            AppThemeManager.ApplyToolStrip(_grid.ContextMenuStrip, _palette);
            _console.ApplyApplicationTheme(Configuration.Application.Theme);
            ApplyToolbarIcons();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is Guid)
                {
                    var script = Configuration.Scripts.FirstOrDefault(item => item.Id == (Guid)row.Tag);
                    if (script != null) ApplyRuntimeVisual(row, script, _supervisor.GetSnapshot(script.Id));
                }
                else
                {
                    var folderTag = row.Tag as FolderGridRowTag;
                    if (folderTag == null) continue;
                    var scripts = ScriptHierarchy.GetDescendantScripts(Configuration, folderTag.Id);
                    ApplyFolderRuntimeVisual(row, AggregateFolderRuntime(folderTag.Id, scripts), scripts.Count == 0);
                }
            }
            _grid.Invalidate();
        }

        private void ApplyToolbarIcons()
        {
            foreach (var pair in _toolbarIcons)
            {
                var previous = pair.Key.Image;
                var role = pair.Key.Tag is FluentToolRole ? (FluentToolRole)pair.Key.Tag : FluentToolRole.Normal;
                var color = role == FluentToolRole.Primary ? Color.White :
                    role == FluentToolRole.Danger ? _palette.Danger : _palette.Text;
                pair.Key.Image = ToolbarIconFactory.Create(pair.Value, color);
                previous?.Dispose();
            }
        }

        private void HandleGridRowPostPaint(object sender, DataGridViewRowPostPaintEventArgs args)
        {
            if (args.RowIndex < 0 || !_grid.Rows[args.RowIndex].Selected) return;
            using (var brush = new SolidBrush(_palette.Accent))
                args.Graphics.FillRectangle(brush, args.RowBounds.Left, args.RowBounds.Top, 3, args.RowBounds.Height);
        }

        private ScriptDefinition SelectedScript
        {
            get
            {
                if (_grid.SelectedRows.Count == 0 || !(_grid.SelectedRows[0].Tag is Guid)) return null;
                var id = (Guid)_grid.SelectedRows[0].Tag;
                return Configuration.Scripts.FirstOrDefault(item => item.Id == id);
            }
        }

        private ScriptFolderDefinition SelectedFolder
        {
            get
            {
                if (_grid.SelectedRows.Count == 0) return null;
                var tag = _grid.SelectedRows[0].Tag as FolderGridRowTag;
                return tag == null ? null : Configuration.Folders.FirstOrDefault(item => item.Id == tag.Id);
            }
        }

        private HierarchyItemKey SelectedHierarchyItem
        {
            get
            {
                if (_grid.SelectedRows.Count == 0) return null;
                var metadata = _grid.SelectedRows[0].Cells["Name"].Tag as HierarchyRowMetadata;
                return metadata?.Item;
            }
        }

        private Guid? SuggestedFolderId
        {
            get
            {
                var folder = SelectedFolder;
                if (folder != null) return folder.Id;
                return SelectedScript?.FolderId;
            }
        }

        private void UpdateButtons()
        {
            var selected = SelectedScript;
            var folder = SelectedFolder;
            var running = selected != null && _supervisor.IsRunning(selected.Id);
            var folderScripts = folder == null
                ? new ScriptDefinition[0]
                : ScriptHierarchy.GetDescendantScripts(Configuration, folder.Id).ToArray();
            _editButton.Enabled = selected != null || folder != null;
            _deleteButton.Enabled = folder != null || (selected != null && !running);
            _startButton.Enabled = selected != null
                ? selected.Enabled && (!running || selected.Launch.AllowParallelInstances)
                : folderScripts.Any(item => item.Enabled && (!_supervisor.IsRunning(item.Id) || item.Launch.AllowParallelInstances));
            _stopButton.Enabled = selected != null ? running : folderScripts.Any(item => _supervisor.IsRunning(item.Id));
        }

        private void HandleContextOpening(object sender, CancelEventArgs args)
        {
            var script = SelectedScript;
            var folder = SelectedFolder;
            _contextAddFolder.Text = folder == null ? _text["Main.Context.NewFolder"] : _text["Main.Context.NewSubfolder"];
            _contextEdit.Text = folder == null ? _text["Main.Context.EditEntry"] : _text["Main.Context.EditFolder"];
            _contextDelete.Text = folder == null ? _text["Main.Context.DeleteEntry"] : _text["Main.Context.DeleteFolder"];
            _contextEditFile.Visible = script != null;
            _contextShowInFolder.Visible = script != null;
            UpdateButtons();
            _contextStart.Enabled = _startButton.Enabled;
            _contextStop.Enabled = _stopButton.Enabled;
            _contextEdit.Enabled = _editButton.Enabled;
            _contextDelete.Enabled = _deleteButton.Enabled;
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!AllowClose && args.CloseReason == CloseReason.UserClosing)
            {
                PersistLayoutNow();
                args.Cancel = true;
                if (Configuration.Application.CloseToTray) Hide();
                else ExitRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (AllowClose)
            {
                PersistLayoutNow();
            }
        }

        private void ShowError(string message, Exception exception)
        {
            _log.Error(message, exception);
            MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + exception.Message,
                ApplicationResources.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private ToolStripButton Button(EventHandler click, ToolbarIcon icon, FluentToolRole role = FluentToolRole.Normal)
        {
            var button = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ImageScaling = ToolStripItemImageScaling.None,
                AutoSize = true,
                Margin = new Padding(1, 0, 1, 0),
                Padding = new Padding(5, 3, 5, 3),
                Tag = role,
                Overflow = ToolStripItemOverflow.AsNeeded
            };
            button.Click += click;
            _toolbarIcons.Add(button, icon);
            return button;
        }

        private static void UseCompactImageOnly(params ToolStripButton[] buttons)
        {
            foreach (var button in buttons)
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.AutoToolTip = true;
                button.Padding = new Padding(5, 3, 5, 3);
            }
        }

        private static DataGridViewTextBoxColumn Column(string name, int width)
        {
            return new DataGridViewTextBoxColumn { Name = name, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        private sealed class DoubleBufferedDataGridView : DataGridView
        {
            internal DoubleBufferedDataGridView()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
            }
        }

        private sealed class FolderGridRowTag
        {
            internal FolderGridRowTag(Guid id) { Id = id; }
            internal Guid Id { get; }
        }

        private sealed class HierarchyRowMetadata
        {
            internal HierarchyRowMetadata(HierarchyItemKey item, int depth, Guid? parentFolderId,
                IEnumerable<Guid> ancestors, bool isExpanded)
            {
                Item = item;
                Depth = depth;
                ParentFolderId = parentFolderId;
                Ancestors = (ancestors ?? Enumerable.Empty<Guid>()).ToArray();
                IsExpanded = isExpanded;
            }

            internal HierarchyItemKey Item { get; }
            internal int Depth { get; }
            internal Guid? ParentFolderId { get; }
            internal IReadOnlyList<Guid> Ancestors { get; }
            internal bool IsExpanded { get; }
        }

        private sealed class HierarchyDropIndicator
        {
            internal HierarchyDropIndicator(Guid? parentFolderId, int insertIndex, int lineY, int lineX,
                Guid? hoverFolderId)
            {
                ParentFolderId = parentFolderId;
                InsertIndex = insertIndex;
                LineY = lineY;
                LineX = lineX;
                HoverFolderId = hoverFolderId;
            }

            internal Guid? ParentFolderId { get; }
            internal int InsertIndex { get; }
            internal int LineY { get; }
            internal int LineX { get; }
            internal Guid? HoverFolderId { get; }
        }

        private static string InterpreterText(ScriptDefinition script)
        {
            var interpreter = script.Launch.Interpreter == ScriptInterpreter.Auto ? ScriptDefinitionValidator.ResolveAutoInterpreter(script.Path) : script.Launch.Interpreter;
            switch (interpreter)
            {
                case ScriptInterpreter.Cmd: return "CMD";
                case ScriptInterpreter.WindowsPowerShell: return "Windows PS 5.1";
                case ScriptInterpreter.PowerShell7: return "PowerShell 7";
                case ScriptInterpreter.CScript: return "cscript.exe";
                case ScriptInterpreter.WScript: return "wscript.exe";
                default: return interpreter.ToString();
            }
        }

        private string StateText(ScriptRuntimeSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case ScriptRuntimeState.Starting: return _text["Main.State.Starting"];
                case ScriptRuntimeState.Running: return snapshot.ActiveCount > 1 ? _text.Get("Main.State.RunningMany", snapshot.ActiveCount) : _text["Main.State.Running"];
                case ScriptRuntimeState.Stopping: return _text["Main.State.Stopping"];
                case ScriptRuntimeState.Exited: return _text["Main.State.Exited"];
                case ScriptRuntimeState.Failed: return _text["Main.State.Failed"];
                default: return _text["Main.State.Stopped"];
            }
        }
    }
}
