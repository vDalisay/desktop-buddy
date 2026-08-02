using DesktopBuddy.Domain.Platform;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Platform;

public sealed class InputModeStateMachineTests
{
    [Fact]
    public void GameplayCanvasInteraction_EntersPlayFromWork()
    {
        var machine = new InputModeStateMachine(InputMode.Work);
        Assert.True(machine.Apply(ShellInputEvent.BuddyInteraction));
        Assert.Equal(InputMode.Play, machine.Current);
    }

    [Theory]
    [InlineData(ShellInputEvent.MenuInteraction)]
    [InlineData(ShellInputEvent.ToolSelected)]
    [InlineData(ShellInputEvent.OutsideClick)]
    [InlineData(ShellInputEvent.FocusLost)]
    [InlineData(ShellInputEvent.InactivityTick)]
    public void PassiveOrUiEvents_DoNotChangeMode(ShellInputEvent input)
    {
        Assert.Equal(InputMode.Work, InputModeStateMachine.Next(InputMode.Work, input));
        Assert.Equal(InputMode.Play, InputModeStateMachine.Next(InputMode.Play, input));
    }

    [Theory]
    [InlineData(ShellInputEvent.EscapePressed)]
    [InlineData(ShellInputEvent.TrayReturnToWork)]
    public void RecoveryEvents_ReturnToWorkFromPlay(ShellInputEvent input)
    {
        var machine = new InputModeStateMachine(InputMode.Play);
        Assert.True(machine.Apply(input));
        Assert.Equal(InputMode.Work, machine.Current);
    }

    [Fact]
    public void GlobalToggle_FlipsBothWays()
    {
        var machine = new InputModeStateMachine(InputMode.Work);
        Assert.True(machine.Apply(ShellInputEvent.GlobalToggle));
        Assert.Equal(InputMode.Play, machine.Current);
        Assert.True(machine.Apply(ShellInputEvent.GlobalToggle));
        Assert.Equal(InputMode.Work, machine.Current);
    }

    [Fact]
    public void GameplayCanvasInteraction_IsIdempotentInPlay()
    {
        var machine = new InputModeStateMachine(InputMode.Play);
        Assert.False(machine.Apply(ShellInputEvent.BuddyInteraction));
        Assert.Equal(InputMode.Play, machine.Current);
    }

    [Theory]
    [InlineData(ShellInputEvent.EscapePressed)]
    [InlineData(ShellInputEvent.TrayReturnToWork)]
    public void RecoveryEvents_AreIdempotentInWork(ShellInputEvent input)
    {
        var machine = new InputModeStateMachine(InputMode.Work);
        Assert.False(machine.Apply(input));
        Assert.Equal(InputMode.Work, machine.Current);
    }
}
