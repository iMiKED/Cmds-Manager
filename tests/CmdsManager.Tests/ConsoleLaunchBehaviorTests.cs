using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Infrastructure.Logging;
using CmdsManager.Infrastructure.Windows;
using CmdsManager.Presentation;
using CmdsManager.Presentation.Controls;
using CmdsManager.Presentation.Forms;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Tests
{
    internal static partial class Program
    {
        private static void TestConsoleLaunchConfiguration()
        {
            WithTemporaryDirectory(directory =>
            {
                var path = Path.Combine(directory, "CmdsManager.ini");
                var scriptPath = Path.Combine(directory, "sample.cmd");
                File.WriteAllText(scriptPath, "@exit /b 0\r\n", Encoding.ASCII);
                var script = new ScriptDefinition { Id = Guid.NewGuid(), Name = "Sample", Path = scriptPath };
                File.WriteAllText(path, "[Application]\r\nConfigVersion=13\r\n[Script:" + script.Id +
                    "]\r\nName=Sample\r\nPath=" + scriptPath + "\r\n", Encoding.UTF8);
                var store = new ConfigurationStore(path);
                var configuration = store.LoadOrCreate();
                Equal(14, configuration.Application.ConfigVersion, "schema 13 migrates to 14");
                Equal(ConsoleLaunchBehavior.Reuse, configuration.Application.ConsoleLaunchBehavior,
                    "existing configurations default to console reuse");
                Equal(ConsoleLaunchBehavior.Inherit, configuration.Scripts.Single().Launch.ConsoleLaunchBehavior,
                    "existing scripts dynamically inherit the global mode");

                foreach (ConsoleLaunchBehavior behavior in Enum.GetValues(typeof(ConsoleLaunchBehavior)))
                {
                    configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.NewKeepPrevious;
                    configuration.Scripts[0].Launch.ConsoleLaunchBehavior = behavior;
                    store.Save(configuration);
                    configuration = store.Reload();
                    Equal(ConsoleLaunchBehavior.NewKeepPrevious, configuration.Application.Clone().ConsoleLaunchBehavior,
                        "global behavior survives INI round-trip and cloning");
                    Equal(behavior, configuration.Scripts.Single().Clone().Launch.ConsoleLaunchBehavior,
                        "each script override survives INI round-trip and cloning");
                }
                configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.Inherit;
                Expect<ConfigurationValidationException>(() => store.Save(configuration), "global mode cannot inherit itself");
                configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.Reuse;
                var invalid = script.Clone();
                invalid.Launch.ConsoleLaunchBehavior = (ConsoleLaunchBehavior)99;
                Expect<ArgumentException>(() => ScriptDefinitionValidator.Validate(invalid, false), "invalid script mode is rejected");

                foreach (var language in new[] { "en", "ru" })
                {
                    configuration.Localization.Language = language;
                    var text = new LocalizationService(new ConfigurationState(configuration));
                    using (var settings = new SettingsForm(configuration.Application, configuration.PowerShell7Path,
                        configuration.Localization, text))
                    using (var editor = new ScriptEditorForm(script, configuration.Defaults, directory, text, ApplicationTheme.Light))
                    {
                        var globalCombo = AllControls(settings).OfType<ComboBox>().Single(control =>
                            control.Items.Count > 0 && control.Items[0] is DisplayItem<ConsoleLaunchBehavior>);
                        var scriptCombo = AllControls(editor).OfType<ComboBox>().Single(control =>
                            control.Items.Count > 0 && control.Items[0] is DisplayItem<ConsoleLaunchBehavior>);
                        Equal(3, globalCombo.Items.Count, language + " global modes");
                        Equal(4, scriptCombo.Items.Count, language + " script modes include inheritance");
                        Equal(ConsoleLaunchBehavior.Reuse, ((DisplayItem<ConsoleLaunchBehavior>)globalCombo.SelectedItem).Value,
                            "default mode is shown in Settings");
                        Equal(ConsoleLaunchBehavior.Inherit, ((DisplayItem<ConsoleLaunchBehavior>)scriptCombo.SelectedItem).Value,
                            "new script defaults to inheritance");
                        Assert(globalCombo is FluentComboBox && scriptCombo is FluentComboBox, "both new selectors use Fluent controls");
                        var settingsHandle = settings.Handle;
                        var editorHandle = editor.Handle;
                        var settingsTabs = AllControls(settings).OfType<TabControl>().First();
                        settingsTabs.SelectedTab = settingsTabs.TabPages.Cast<TabPage>()
                            .Single(page => page.Text == text["Settings.Tab.Console"]);
                        settings.Show();
                        editor.Show();
                        settings.PerformLayout();
                        editor.PerformLayout();
                        var outputEncoding = AllControls(editor).OfType<ComboBox>().Single(control =>
                            control.Items.Count > 0 && control.Items[0] is DisplayItem<ScriptOutputEncoding>);
                        Equal(outputEncoding.Left, scriptCombo.Left, "script selectors have the same left edge");
                        Equal(outputEncoding.Width, scriptCombo.Width, "script selectors have the same width");
                        Assert(scriptCombo.Items.Cast<object>().All(item => TextRenderer.MeasureText(item.ToString(),
                            scriptCombo.Font).Width < scriptCombo.Width - 30), language + " script mode labels fit");
                        Assert(globalCombo.Items.Cast<object>().All(item => TextRenderer.MeasureText(item.ToString(),
                            globalCombo.Font).Width < globalCombo.Width - 30), language + " global mode labels fit in " +
                            globalCombo.Width + " px: " + string.Join(",", globalCombo.Items.Cast<object>().Select(item =>
                                TextRenderer.MeasureText(item.ToString(), globalCombo.Font).Width)));
                        globalCombo.SelectedIndex = 2;
                        scriptCombo.SelectedIndex = 2;
                        InvokePrivate(settings, "SaveAndClose", settings, EventArgs.Empty);
                        InvokePrivate(editor, "SaveAndClose", editor, EventArgs.Empty);
                        Equal(ConsoleLaunchBehavior.NewClosePrevious, settings.SettingsResult.ConsoleLaunchBehavior,
                            "Settings saves the selected behavior");
                        Equal(ConsoleLaunchBehavior.NewKeepPrevious, editor.Result.Launch.ConsoleLaunchBehavior,
                            "script editor saves an explicit override");
                    }
                }
            });
        }

        private static void TestConsoleLaunchLifecycle()
        {
            WithTemporaryDirectory(directory =>
            {
                var configuration = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini")).LoadOrCreate();
                configuration.Localization.Language = "en";
                var text = new LocalizationService(new ConfigurationState(configuration));
                foreach (var global in new[] { ConsoleLaunchBehavior.Reuse, ConsoleLaunchBehavior.NewKeepPrevious,
                    ConsoleLaunchBehavior.NewClosePrevious })
                foreach (ConsoleLaunchBehavior local in Enum.GetValues(typeof(ConsoleLaunchBehavior)))
                {
                    configuration.Application.ConsoleLaunchBehavior = global;
                    using (var console = new ConsoleTabsControl(text, () => configuration.Application,
                        consoleBehaviorForScript: id => local))
                    {
                        console.CreateControl();
                        var script = Guid.NewGuid();
                        var firstRun = Guid.NewGuid();
                        var secondRun = Guid.NewGuid();
                        console.EnqueueStarted(ConsoleInstance(script, 1001, firstRun));
                        console.EnqueueOutput(new ScriptOutputEventArgs(script, 1001, "old-history", false, null, firstRun));
                        FlushConsole(console);
                        var first = AllControls(console).OfType<RichTextBox>().Single();
                        var closed = 0;
                        console.CloseRequested += (sender, args) => closed++;
                        // Exercise output, exit and restart pending in the same UI batch.
                        console.EnqueueOutput(new ScriptOutputEventArgs(script, 1001, "old-tail", false, null, firstRun));
                        console.EnqueueExited(ConsoleInstance(script, 1001, firstRun, 0));
                        console.EnqueueStarted(ConsoleInstance(script, 1002, secondRun));
                        console.EnqueueOutput(new ScriptOutputEventArgs(script, 1002, "new-history", false, null, secondRun));
                        FlushConsole(console);
                        var tabs = FindControl<TerminalTabStrip>(console);
                        var actual = local == ConsoleLaunchBehavior.Inherit ? global : local;
                        var outputs = AllControls(console).OfType<RichTextBox>().ToArray();
                        Equal(actual == ConsoleLaunchBehavior.NewKeepPrevious ? 2 : 1, outputs.Length,
                            "tab count for global=" + global + ", script=" + local);
                        var current = outputs.Single(control => control.Text.Contains("new-history"));
                        Equal(actual == ConsoleLaunchBehavior.Reuse, ReferenceEquals(first, current), "only reuse keeps the output control");
                        Equal(actual == ConsoleLaunchBehavior.NewClosePrevious, first.IsDisposed, "only replace disposes the previous console");
                        Assert(actual != ConsoleLaunchBehavior.Reuse || current.Text.Contains("old-history\nold-tail\nnew-history"),
                            "reuse preserves the old history and final pending output in order");
                        Equal(0, closed, "automatic replacement never requests another process stop");

                        // A running parallel instance must never be stolen or closed by these modes.
                        var beforeParallel = tabs.TabCount;
                        console.EnqueueStarted(ConsoleInstance(script, 1003, Guid.NewGuid()));
                        FlushConsole(console);
                        Equal(actual == ConsoleLaunchBehavior.NewKeepPrevious ? beforeParallel + 1 : 2,
                            tabs.TabCount, "parallel processes keep separate consoles");
                        Assert(!current.IsDisposed, "live predecessor is never closed");
                    }
                }

                configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.Reuse;
                configuration.Application.ConsoleAutoRecord = true;
                var logDirectory = Path.Combine(directory, "console-logs");
                using (var host = new Form { ClientSize = new Size(800, 300), ShowInTaskbar = false })
                using (var console = new ConsoleTabsControl(text, () => configuration.Application,
                    id => true, logDirectory) { Dock = DockStyle.Fill })
                {
                    host.Controls.Add(console);
                    host.Show();
                    var script = Guid.NewGuid();
                    var oldRun = Guid.NewGuid();
                    var newRun = Guid.NewGuid();
                    console.EnqueueStarted(ConsoleInstance(script, 2001, oldRun));
                    console.EnqueueOutput(new ScriptOutputEventArgs(script, 2001, "\x1b[31mred-old", false, null, oldRun));
                    FlushConsole(console);
                    var output = AllControls(console).OfType<RichTextBox>().Single();
                    var tabs = FindControl<TerminalTabStrip>(console);
                    var scrollLock = tabs.ContextMenuStrip.Items.OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == text["Console.ScrollLock"]);
                    output.Select(0, 0);
                    scrollLock.PerformClick();
                    Assert(console.DetachSelectedTab(), "the old console detaches");
                    var detached = (DetachedConsoleForm)output.FindForm();
                    detached.SetFullScreen(true);
                    console.EnqueueOutput(new ScriptOutputEventArgs(script, 2001, "tail-old", false, null, oldRun));
                    console.EnqueueExited(ConsoleInstance(script, 2001, oldRun, 0));
                    console.EnqueueStarted(ConsoleInstance(script, 2002, newRun));
                    console.EnqueueOutput(new ScriptOutputEventArgs(script, 2002, "plain-new", false, null, newRun));
                    // Late callbacks from the old process must not complete or contaminate the new run.
                    console.EnqueueOutput(new ScriptOutputEventArgs(script, 2001, "late-old", false, null, oldRun));
                    console.EnqueueExited(ConsoleInstance(script, 2001, oldRun, 0));
                    FlushConsole(console);
                    Assert(ReferenceEquals(detached, output.FindForm()) && console.DetachedTabCount == 1,
                        "reuse keeps the detached window without reparenting or restarting it");
                    Assert(detached.IsFullScreen, "reuse keeps the full-screen state");
                    Assert(output.Text.Contains("red-old\ntail-old\nplain-new") && !output.Text.Contains("late-old"),
                        "detached reuse preserves history and ignores old callbacks");
                    Assert(output.WordWrap, "word wrap survives reuse");
                    Equal(0, output.SelectionStart, "scroll lock preserves the caret across reuse and new output");
                    Assert(detached.Text.Contains("2002"), "detached title identifies the new PID");
                    console.ApplySettings();
                    output.Select(output.Text.IndexOf("red-old", StringComparison.Ordinal), 3);
                    Equal(Color.FromArgb(197, 15, 31), output.SelectionColor, "old ANSI style survives redraw");
                    output.Select(output.Text.IndexOf("plain-new", StringComparison.Ordinal), 5);
                    Equal(output.ForeColor.ToArgb(), output.SelectionColor.ToArgb(), "new run resets ANSI state even after history redraw");
                    var logs = Directory.GetFiles(logDirectory, "*.log");
                    Equal(2, logs.Length, "automatic recording creates one file per run, even with reuse");
                    var oldLog = logs.Select(ReadSharedText).Single(value => value.Contains("red-old"));
                    var newLog = logs.Select(ReadSharedText).Single(value => value.Contains("plain-new"));
                    Assert(oldLog.Contains("tail-old") && !oldLog.Contains("plain-new") && !newLog.Contains("red-old"),
                        "run logs include final output but never replay preceding history");
                    detached.Close();
                    Equal(2001, tabs.SelectedKey, "reused tab retains its stable key after reattaching");
                    ConsoleTabCloseRequestedEventArgs closed = null;
                    console.CloseRequested += (sender, args) => closed = args;
                    console.CloseActiveTab();
                    Assert(closed != null && closed.IsRunning && closed.ProcessId == 2002, "close targets the current process, not the old tab key");
                }

                configuration.Application.ConsoleAutoRecord = false;
                configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.NewKeepPrevious;
                using (var console = new ConsoleTabsControl(text, () => configuration.Application))
                {
                    console.CreateControl();
                    var script = Guid.NewGuid();
                    var oldRun = Guid.NewGuid();
                    var newRun = Guid.NewGuid();
                    console.EnqueueStarted(ConsoleInstance(script, 3001, oldRun));
                    console.EnqueueExited(ConsoleInstance(script, 3001, oldRun, 0));
                    console.EnqueueStarted(ConsoleInstance(script, 3001, newRun));
                    FlushConsole(console);
                    var tabs = FindControl<TerminalTabStrip>(console);
                    Equal(2, tabs.TabCount, "recycled OS PID does not replace an archived tab in keep mode");
                    tabs.SelectTab(3001);
                    console.CloseActiveTab();
                    console.EnqueueOutput(new ScriptOutputEventArgs(script, 3001, "recycled-live", false, null, newRun));
                    console.EnqueueExited(ConsoleInstance(script, 3001, oldRun, 0));
                    FlushConsole(console);
                    Assert(AllControls(console).OfType<RichTextBox>().Single().Text.Contains("recycled-live"),
                        "closing the old PID tab does not suppress the new process output");
                    console.EnqueueExited(ConsoleInstance(script, 3001, newRun, 0));
                    configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.Reuse;
                    console.EnqueueStarted(ConsoleInstance(script, 3002, Guid.NewGuid()));
                    FlushConsole(console);
                    Equal(1, tabs.TabCount, "inherited setting follows a live global change without editing the script");
                }
            });
        }

        private static void TestConsoleRestartIntegration()
        {
            WithTemporaryDirectory(directory =>
            {
                var path = Path.Combine(directory, "restart.cmd");
                File.WriteAllText(path, "@echo off\r\necho restart-output\r\nping.exe -n 30 127.0.0.1 >nul\r\n", Encoding.ASCII);
                var store = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini"));
                var configuration = store.LoadOrCreate();
                configuration.Application.ConsoleLaunchBehavior = ConsoleLaunchBehavior.NewKeepPrevious;
                var script = new ScriptDefinition
                {
                    Id = Guid.NewGuid(), Name = "Restart", Path = path,
                    Launch = new LaunchProfile
                    {
                        CaptureOutput = true, Interpreter = ScriptInterpreter.Cmd,
                        StopPolicy = ScriptStopPolicy.Kill, StopTimeoutSeconds = 0,
                        ConsoleLaunchBehavior = ConsoleLaunchBehavior.Reuse
                    }
                };
                configuration.Scripts.Add(script);
                var state = new ConfigurationState(configuration);
                var commandBuilder = new ScriptCommandBuilder(directory);
                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(commandBuilder, logger, () => false))
                using (var hotkey = new ShowAppHotkeyManager())
                using (var form = new MainForm(state, store, supervisor, new WindowsScriptEditorLauncher(commandBuilder),
                    new NoOpStartupRegistration(), hotkey, logger, new LocalizationService(state)))
                {
                    var handle = form.Handle;
                    var console = FindControl<ConsoleTabsControl>(form);
                    var tabs = FindControl<TerminalTabStrip>(console);
                    var exited = 0;
                    supervisor.InstanceExited += (sender, args) => Interlocked.Increment(ref exited);
                    RichTextBox original = null;
                    for (var run = 1; run <= 3; run++)
                    {
                        supervisor.Start(script, string.Empty);
                        var expected = run;
                        Assert(WaitWithUi(() => AllControls(console).OfType<RichTextBox>().Any(output =>
                            output.Text.Split(new[] { "restart-output" }, StringSplitOptions.None).Length - 1 == expected),
                            TimeSpan.FromSeconds(4)), "real restart appends to previous console");
                        var current = AllControls(console).OfType<RichTextBox>().Single();
                        if (original == null) original = current;
                        Assert(ReferenceEquals(original, current), "real restart keeps the same output control");
                        supervisor.StopAsync(script.Id).GetAwaiter().GetResult();
                        Equal(run, Volatile.Read(ref exited), "StopAsync completes only after exit callbacks");
                        // Intentionally no UI pumping between StopAsync and the next Start.
                    }
                    FlushConsole(console);
                    Equal(1, tabs.TabCount, "per-script override wins over global keep mode in MainForm");

                    Guid childId = Guid.Empty;
                    supervisor.InstanceStarted += (sender, args) => { if (args.ScriptId != script.Id) childId = args.ScriptId; };
                    var arguments = new[] { "Managed child", "cmd", "/c", path };
                    form.RunManagedChild(script.Id, directory, arguments);
                    Assert(WaitWithUi(() => tabs.TabCount == 2 && AllControls(console).OfType<RichTextBox>()
                        .Any(output => !ReferenceEquals(output, original) && output.Text.Contains("restart-output")),
                        TimeSpan.FromSeconds(4)), "child has its own console");
                    var firstChildId = childId;
                    supervisor.StopAsync(childId).GetAwaiter().GetResult();
                    form.RunManagedChild(script.Id, directory, arguments);
                    Equal(firstChildId, childId, "managed child identity remains stable across launches");
                    Assert(WaitWithUi(() => AllControls(console).OfType<RichTextBox>().Any(output =>
                        !ReferenceEquals(output, original) && output.Text.Split(new[] { "restart-output" },
                            StringSplitOptions.None).Length == 3), TimeSpan.FromSeconds(4)), "managed child reuses its history using the parent's override");
                    Equal(2, tabs.TabCount, "parent and child consoles are never merged");
                    form.RunManagedChild(script.Id, directory, arguments);
                    Assert(WaitWithUi(() => tabs.TabCount == 3, TimeSpan.FromSeconds(4)), "parallel children still have separate consoles");
                    supervisor.StopAllAsync().GetAwaiter().GetResult();
                }
            });
        }

        private static ScriptInstanceEventArgs ConsoleInstance(Guid scriptId, int processId, Guid instanceId, int? exitCode = null)
        {
            return new ScriptInstanceEventArgs(scriptId, "Console test", processId, DateTime.Now, true,
                exitCode, ScriptOutputEncoding.Utf8, instanceId);
        }

        private static void FlushConsole(ConsoleTabsControl console)
        {
            InvokePrivate(console, "FlushPendingOutput");
        }

        private static void InvokePrivate(object target, string method, params object[] arguments)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
        }
    }
}
