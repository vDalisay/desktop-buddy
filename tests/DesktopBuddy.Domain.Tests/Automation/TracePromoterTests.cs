using DesktopBuddy.Domain.Automation;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Automation;

public sealed class TracePromoterTests
{
    [Fact]
    public void PromotionCollapsesKnownSamplesIntoDraftSteps()
    {
        var events = new List<InputTraceEvent> { new(1, "pointer_press", "buddy:Head", 0, 0, 1) };
        for (int i = 0; i < 240; i++) events.Add(new(2 + i, "pointer_motion", "buddy:Head", i, i));
        events.Add(new(242, "pointer_release", "buddy:Head", 239, 239, 1));
        var trace = new InputTrace("desktop-buddy-input-trace-v1", 7, events);
        string result = TracePromoter.Promote(trace);
        using JsonDocument document = JsonDocument.Parse(result);
        JsonElement steps = document.RootElement.GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        Assert.Equal("pointer_press", steps[0].GetProperty("step").GetString());
        Assert.Equal("drag", steps[1].GetProperty("step").GetString());
        Assert.Equal("pointer_release", steps[2].GetProperty("step").GetString());
        Assert.Equal("lab_finite", document.RootElement.GetProperty("assertions")[0].GetProperty("predicate").GetString());
    }
}
