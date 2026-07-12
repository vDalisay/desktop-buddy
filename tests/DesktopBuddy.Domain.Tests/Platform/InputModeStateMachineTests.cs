using DesktopBuddy.Domain.Platform;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Platform;

public sealed class InputModeStateMachineTests
{
    [Theory]
    [InlineData(ShellInputEvent.BuddyInteraction)]
    [InlineData(ShellInputEvent.MenuInteraction)]
    [InlineData(ShellInputEvent.ToolSelected)]
    public void PlayRequestingEvents_EnterPlayFromWork(ShellInputEvent input)
    {
        var machine = new InputModeStateMachine(InputMode.Work);
        Assert.True(machine.Apply(input));
        Assert.Equal(InputMode.Play, machine.Current);
    }

    [Theory]
    [InlineData(ShellInputEvent.OutsideClick)]
    [InlineData(ShellInputEvent.EscapePressed)]
    [InlineData(ShellInputEvent.TrayReturnToWork)]
    [InlineData(ShellInputEvent.FocusLost)]
    public void WorkRequestingEvents_ReturnToWorkFromPlay(ShellInputEvent input)
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
    public void InactivityTick_NeverChangesMode()
    {
        Assert.Equal(InputMode.Work, InputModeStateMachine.Next(InputMode.Work, ShellInputEvent.InactivityTick));
        Assert.Equal(InputMode.Play, InputModeStateMachine.Next(InputMode.Play, ShellInputEvent.InactivityTick));

        var machine = new InputModeStateMachine(InputMode.Play);
        Assert.False(machine.Apply(ShellInputEvent.InactivityTick));
        Assert.Equal(InputMode.Play, machine.Current);
    }

    [Theory]
    [InlineData(ShellInputEvent.BuddyInteraction)]
    [InlineData(ShellInputEvent.MenuInteraction)]
    [InlineData(ShellInputEvent.ToolSelected)]
    public void PlayRequestingEvents_AreIdempotentInPlay(ShellInputEvent input)
    {
        var machine = new InputModeStateMachine(InputMode.Play);
        Assert.False(machine.Apply(input));
        Assert.Equal(InputMode.Play, machine.Current);
    }

    [Theory]
    [InlineData(ShellInputEvent.OutsideClick)]
    [InlineData(ShellInputEvent.EscapePressed)]
    [InlineData(ShellInputEvent.TrayReturnToWork)]
    public void WorkRequestingEvents_AreIdempotentInWork(ShellInputEvent input)
    {
        var machine = new InputModeStateMachine(InputMode.Work);
        Assert.False(machine.Apply(input));
        Assert.Equal(InputMode.Work, machine.Current);
    }
}
