using System;
using System.IO;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Work;

public sealed class WorkWindowRepaintTests
{
    [Fact]
    public void Region_updates_leave_client_repainting_to_Godot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "project.godot")))
            root = root.Parent;
        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(
            root!.FullName, "src", "Work", "WorkCompanionView.WindowsShape.cs"));

        // Cross-platform guard for the Win32 call; desktop capture must still verify pixels.
        Assert.Contains("SetWindowRgn(_ownedWorkWindowHandle, combined, false)", source);
        Assert.DoesNotContain("SetWindowRgn(_ownedWorkWindowHandle, combined, true)", source);
        Assert.Contains("SetWindowRgn(_ownedWorkWindowHandle, 0, true)", source);
    }
}
