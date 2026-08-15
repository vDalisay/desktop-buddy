using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _generationWarningsInstalled;

    private void EnsureGenerationWarningUi()
    {
        if (_generationWarningsInstalled || !_categoryWorkflowInstalled) return;
        Button? generate = FindButton(this, "Generate");
        if (!GodotObject.IsInstanceValid(generate)) return;
        // CategoryWorkflow has already rebound Generate at this point, so this connection runs after
        // generation and only decorates the authoring status. Warnings never mutate canonical data.
        generate!.Pressed += AppendGenerationWarnings;
        _generationWarningsInstalled = true;
    }

    private void AppendGenerationWarnings()
    {
        if (_generated is null) return;
        IReadOnlyList<string> warnings = AssetForgeGenerationWarnings.Analyze(_generated);
        if (warnings.Count == 0) return;
        string suffix = string.Join("\n", warnings);
        _status.Text = string.IsNullOrWhiteSpace(_status.Text) ? suffix : _status.Text.TrimEnd() + "\n" + suffix;
    }
}
