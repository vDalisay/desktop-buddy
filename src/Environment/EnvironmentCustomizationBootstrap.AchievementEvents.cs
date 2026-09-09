using System;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Achievement-facing semantic event adapter. The Scene-aware environment composition remains the
/// owner of the editor; consumers only observe successful authored commits and never poll the PNG,
/// editor visibility, or persistence files.
/// </summary>
public partial class EnvironmentCustomizationBootstrap : IEnvironmentCustomizationEvents
{
    private Action? _backgroundCommitSubscribers;
    private EnvironmentBackgroundEditor? _backgroundCommitEditor;
    private bool _backgroundCommitDiscoverySubscribed;
    private bool _backgroundCommitCleanupBound;

    public event Action? BackgroundCommitted
    {
        add
        {
            _backgroundCommitSubscribers += value;
            EnsureBackgroundCommitBridge();
        }
        remove => _backgroundCommitSubscribers -= value;
    }

    private void EnsureBackgroundCommitBridge()
    {
        if (!_backgroundCommitCleanupBound)
        {
            TreeExiting += CleanupBackgroundCommitBridge;
            _backgroundCommitCleanupBound = true;
        }

        if (GodotObject.IsInstanceValid(_backgroundEditor))
        {
            BindBackgroundCommitEditor(_backgroundEditor!);
            return;
        }

        if (!IsInsideTree() || _backgroundCommitDiscoverySubscribed)
            return;

        GetTree().Root.ChildEnteredTree += OnEnvironmentRootChildEntered;
        _backgroundCommitDiscoverySubscribed = true;
    }

    private void OnEnvironmentRootChildEntered(Node child)
    {
        if (child is EnvironmentBackgroundEditor editor)
            BindBackgroundCommitEditor(editor);
    }

    private void BindBackgroundCommitEditor(EnvironmentBackgroundEditor editor)
    {
        if (ReferenceEquals(_backgroundCommitEditor, editor))
            return;

        if (GodotObject.IsInstanceValid(_backgroundCommitEditor))
            _backgroundCommitEditor!.BackgroundCommitted -= ForwardBackgroundCommitted;

        _backgroundCommitEditor = editor;
        _backgroundCommitEditor.BackgroundCommitted += ForwardBackgroundCommitted;

        if (_backgroundCommitDiscoverySubscribed && IsInsideTree())
        {
            GetTree().Root.ChildEnteredTree -= OnEnvironmentRootChildEntered;
            _backgroundCommitDiscoverySubscribed = false;
        }
    }

    private void ForwardBackgroundCommitted() => _backgroundCommitSubscribers?.Invoke();

    private void CleanupBackgroundCommitBridge()
    {
        if (_backgroundCommitCleanupBound)
        {
            TreeExiting -= CleanupBackgroundCommitBridge;
            _backgroundCommitCleanupBound = false;
        }

        if (_backgroundCommitDiscoverySubscribed && IsInsideTree())
        {
            GetTree().Root.ChildEnteredTree -= OnEnvironmentRootChildEntered;
            _backgroundCommitDiscoverySubscribed = false;
        }

        if (GodotObject.IsInstanceValid(_backgroundCommitEditor))
            _backgroundCommitEditor!.BackgroundCommitted -= ForwardBackgroundCommitted;
        _backgroundCommitEditor = null;
    }
}
