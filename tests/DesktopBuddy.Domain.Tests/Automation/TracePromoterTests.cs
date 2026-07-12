using DesktopBuddy.Domain.Automation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Automation;

public sealed class TracePromoterTests
{
    [Fact]
    public void PromotionCollapsesKnownSamplesIntoDraftSteps()
    {
        var trace = new InputTrace("desktop-buddy-input-trace-v1", 7, new[]
        {
            new InputTraceEvent(1, "pointer_press", "buddy:Head", 0, 0, 1),
            new InputTraceEvent(2, "pointer_motion", "buddy:Head", 0, 0),
            new InputTraceEvent(3, "pointer_release", "buddy:Head", 0, 0, 1),
        });
        string result = TracePromoter.Promote(trace);
        Assert.Contains("pointer_press", result);
        Assert.Contains("buddy:Head", result);
        Assert.Contains("TODO_add_semantic_assertion", result);
    }
}
