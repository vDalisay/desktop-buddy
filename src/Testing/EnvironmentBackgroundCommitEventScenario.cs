using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Pins the semantic event consumed by achievement tracking. Persisted room state remains owned by
/// EnvironmentPaintStore/Workshop; this event describes only a successful changed editor commit.
/// </summary>
public sealed class EnvironmentBackgroundCommitEventScenario : IScenario
{
    public string Id => "environment_background_commit_event";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-background-event-{Guid.NewGuid():N}");
        var presenter = new EnvironmentBackgroundPresenter { Name = "BackgroundCommitPresenter" };
        var editor = new EnvironmentBackgroundEditor { Name = "BackgroundCommitEditor" };
        var store = new EnvironmentPaintStore(new CharacterFileSystem(), root);
        editor.Configure(presenter, store);
        int commits = 0;
        editor.BackgroundCommitted += () => commits++;
        tree.Root.AddChild(presenter);
        tree.Root.AddChild(editor);

        EnvironmentBackgroundPresenter? failedPresenter = null;
        EnvironmentBackgroundEditor? failedEditor = null;
        try
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            editor.Open();
            presenter.Canvas.Color = new EnvironmentColor(230, 40, 70);
            presenter.Canvas.Tool = EnvironmentPaintTool.Brush;
            presenter.Canvas.Begin(.5, .5);
            presenter.Canvas.End(.5, .5);
            bool dirtyBeforeSave = presenter.Canvas.IsDirty;
            PressSave(editor);
            await WaitUntilClosed(tree, editor);

            byte[]? persisted = store.Load();
            checks.Add(new StartupCheck(
                "environment_background_changed_successful_save_emits_commit_once",
                dirtyBeforeSave && commits == 1 && !editor.IsOpen && !presenter.Canvas.IsDirty && persisted is not null,
                $"dirtyBefore={dirtyBeforeSave} commits={commits} open={editor.IsOpen} dirtyAfter={presenter.Canvas.IsDirty} persisted={persisted is not null}"));

            editor.Open();
            bool unchangedBeforeSave = !presenter.Canvas.IsDirty;
            PressSave(editor);
            await WaitUntilClosed(tree, editor);
            checks.Add(new StartupCheck(
                "environment_background_unchanged_save_does_not_emit_commit",
                unchangedBeforeSave && commits == 1 && !editor.IsOpen,
                $"unchanged={unchangedBeforeSave} commits={commits} open={editor.IsOpen}"));

            string failureRoot = root + "-failure";
            failedPresenter = new EnvironmentBackgroundPresenter { Name = "BackgroundFailedCommitPresenter" };
            failedEditor = new EnvironmentBackgroundEditor { Name = "BackgroundFailedCommitEditor" };
            var failedStore = new EnvironmentPaintStore(new FailingWriteFileSystem(), failureRoot);
            failedEditor.Configure(failedPresenter, failedStore);
            int failedCommits = 0;
            failedEditor.BackgroundCommitted += () => failedCommits++;
            tree.Root.AddChild(failedPresenter);
            tree.Root.AddChild(failedEditor);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            failedEditor.Open();
            failedPresenter.Canvas.Color = new EnvironmentColor(20, 140, 220);
            failedPresenter.Canvas.Begin(.4, .4);
            failedPresenter.Canvas.End(.4, .4);
            PressSave(failedEditor);
            for (int frame = 0; frame < 60; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Label? status = failedEditor.FindChild("PaintToolStatus", true, false) as Label;
            checks.Add(new StartupCheck(
                "environment_background_failed_save_does_not_emit_commit",
                failedCommits == 0 && failedEditor.IsOpen && failedPresenter.Canvas.IsDirty &&
                    status?.Text.StartsWith("Save failed:", StringComparison.Ordinal) == true,
                $"commits={failedCommits} open={failedEditor.IsOpen} dirty={failedPresenter.Canvas.IsDirty} status={status?.Text}"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(failedEditor)) failedEditor!.QueueFree();
            if (GodotObject.IsInstanceValid(failedPresenter)) failedPresenter!.QueueFree();
            editor.QueueFree();
            presenter.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
            try { if (Directory.Exists(root + "-failure")) Directory.Delete(root + "-failure", true); } catch (IOException) { }
        }

        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }

    private static void PressSave(EnvironmentBackgroundEditor editor) =>
        ((Button)(editor.FindChild("PaintSaveButton", true, false)
            ?? throw new InvalidOperationException("Paint Background save button was not composed.")))
        .EmitSignal(BaseButton.SignalName.Pressed);

    private static async Task WaitUntilClosed(SceneTree tree, EnvironmentBackgroundEditor editor)
    {
        for (int frame = 0; frame < 120 && editor.IsOpen; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Enough filesystem surface for EnvironmentPaintStore to fail at the durable write.</summary>
    private sealed class FailingWriteFileSystem : ICharacterFileSystem
    {
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => false;
        public void CreateDirectory(string path) { }
        public IReadOnlyList<string> EnumerateDirectories(string path) => [];
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public byte[] ReadPrefix(string path, int maximumBytes) => throw new FileNotFoundException(path);
        public byte[] ReadAllBytes(string path, int maximumBytes) => throw new FileNotFoundException(path);
        public void WriteAllTextDurable(string path, string content) => throw new IOException("Synthetic write failure.");
        public void WriteAllBytesDurable(string path, ReadOnlySpan<byte> content) => throw new IOException("Synthetic write failure.");
        public void ReplaceFileWithBackup(string temporaryPath, string primaryPath, string backupPath) => throw new IOException("Synthetic write failure.");
        public void MoveFile(string sourcePath, string destinationPath) => throw new IOException("Synthetic write failure.");
        public void MoveDirectory(string sourcePath, string destinationPath) => throw new IOException("Synthetic write failure.");
        public void DeleteFile(string path) { }
        public void DeleteDirectory(string path, bool recursive) { }
        public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
        public bool IsReparsePoint(string path) => false;
    }
}
