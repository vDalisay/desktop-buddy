using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Sandbox;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sandbox;

public sealed class SandboxSignalTests
{
    private const int Delay = 5;

    private static SandboxPartId Place(SandboxDocument document, SemanticDefinitionId definition) =>
        document.Add(definition, new CanonicalRoomPosition(0.5f, 0.5f)).Part!.PartId;

    private static SandboxWireResult Wire(SandboxDocument document, SandboxPartId from, SandboxPartId to) =>
        document.AddWire(from, SandboxDevices.Out, to, SandboxDevices.In);

    private static List<SandboxDeviceCommand> Run(SandboxSignalNetwork network, int ticks)
    {
        var commands = new List<SandboxDeviceCommand>();
        for (int tick = 0; tick < ticks; tick++)
            network.Tick(commands);
        return commands;
    }

    [Fact]
    public void Only_an_output_can_feed_an_input_and_a_rejected_wire_changes_nothing()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId timer = Place(document, SandboxPartCatalogue.Timer);
        SandboxPartId piston = Place(document, SandboxPartCatalogue.Piston);
        SandboxPartId beam = Place(document, SandboxPartCatalogue.WoodBeam);
        long revision = document.Revision;

        Assert.True(Wire(document, button, timer).Succeeded);
        Assert.True(Wire(document, timer, piston).Succeeded);
        Assert.Equal(revision + 2, document.Revision);

        Assert.Equal(SandboxLinkStatus.Duplicate, Wire(document, button, timer).Status);
        Assert.Equal(SandboxLinkStatus.Invalid, Wire(document, piston, timer).Status);        // piston has no output
        Assert.Equal(SandboxLinkStatus.Invalid, Wire(document, timer, button).Status);        // button has no input
        Assert.Equal(SandboxLinkStatus.Invalid, Wire(document, button, beam).Status);         // a beam is no device
        Assert.Equal(SandboxLinkStatus.Invalid, Wire(document, timer, timer).Status);
        Assert.Equal(SandboxLinkStatus.Invalid, document.AddWire(button, "fire", piston, SandboxDevices.In).Status);
        Assert.Equal(SandboxLinkStatus.PartNotFound, Wire(document, button, SandboxPartId.New()).Status);

        Assert.Equal(2, document.Wires.Count);
        Assert.Equal(revision + 2, document.Revision);
    }

    [Fact]
    public void Removing_a_device_cuts_its_wires()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId timer = Place(document, SandboxPartCatalogue.Timer);
        SandboxPartId lamp = Place(document, SandboxPartCatalogue.Lamp);
        Wire(document, button, timer);
        SandboxWire kept = Wire(document, button, lamp).Wire!;

        document.Remove(timer);

        Assert.Equal(kept, Assert.Single(document.Wires));
        Assert.True(document.RemoveWire(kept.WireId).Succeeded);
        Assert.Equal(SandboxLinkStatus.WireNotFound, document.RemoveWire(kept.WireId).Status);
    }

    [Fact]
    public void Button_to_piston_fires_on_the_next_tick()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId piston = Place(document, SandboxPartCatalogue.Piston);
        Wire(document, button, piston);
        var network = new SandboxSignalNetwork(Delay);
        network.Rebuild(document);

        Assert.True(network.Press(button));
        Assert.False(network.Press(piston));

        Assert.Equal([new SandboxDeviceCommand(piston, SandboxDeviceAction.PistonExtend)], Run(network, 1));
        Assert.Empty(Run(network, 10));
    }

    [Fact]
    public void A_button_switches_a_timer_that_then_fires_every_interval()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId timer = Place(document, SandboxPartCatalogue.Timer);
        SandboxPartId piston = Place(document, SandboxPartCatalogue.Piston);
        Wire(document, button, timer);
        Wire(document, timer, piston);
        var network = new SandboxSignalNetwork(Delay);   // default 1 s interval = Delay ticks
        network.Rebuild(document);

        Assert.False(network.IsTimerRunning(timer));   // fed by a wire, so it waits for a pulse
        Assert.Empty(Run(network, Delay * 3));

        var beat = new SandboxDeviceCommand(piston, SandboxDeviceAction.PistonExtend);
        network.Press(button);
        Assert.Empty(Run(network, Delay));             // tick 1 switches it on; first beat an interval later
        Assert.True(network.IsTimerRunning(timer));
        Assert.Equal([beat], Run(network, 1));
        Assert.Equal([beat, beat], Run(network, Delay * 2));

        network.Press(button);                         // and off again
        Run(network, 1);
        Assert.False(network.IsTimerRunning(timer));
        Assert.Empty(Run(network, Delay * 3));
    }

    [Fact]
    public void A_timer_with_nothing_wired_in_runs_at_its_own_interval()
    {
        var document = new SandboxDocument();
        SandboxPartId timer = document.Add(
            SandboxPartCatalogue.Timer,
            new CanonicalRoomPosition(0.5f, 0.5f),
            overrides: new SandboxPartOverrides(TimerSeconds: 2.0f)).Part!.PartId;
        SandboxPartId lamp = Place(document, SandboxPartCatalogue.Lamp);
        Wire(document, timer, lamp);
        var network = new SandboxSignalNetwork(Delay);
        network.Rebuild(document);

        Assert.True(network.IsTimerRunning(timer));
        Assert.Equal(Delay * 2, network.IntervalTicksOf(timer));
        Assert.Empty(Run(network, Delay * 2 - 1));
        Assert.Equal([new SandboxDeviceCommand(lamp, SandboxDeviceAction.LampOn)], Run(network, 1));
        Assert.Equal([new SandboxDeviceCommand(lamp, SandboxDeviceAction.LampOff)], Run(network, Delay * 2));

        // The setting survives a save, and so does a Piston's push.
        SandboxPartId piston = document.Add(
            SandboxPartCatalogue.Piston,
            new CanonicalRoomPosition(0.2f, 0.5f),
            overrides: new SandboxPartOverrides(PistonPush: 5000.0f)).Part!.PartId;
        SandboxDocument reloaded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document)).Document!;
        Assert.True(reloaded.TryGet(timer, out PlacedSandboxPart? savedTimer));
        Assert.Equal(2.0f, savedTimer!.Overrides.TimerSecondsValue);
        Assert.True(reloaded.TryGet(piston, out PlacedSandboxPart? savedPiston));
        Assert.Equal(SandboxPartOverrides.MaximumPistonPush, savedPiston!.Overrides.PistonPushValue);

        // Out-of-band settings are clamped rather than trusted.
        document.SetOverrides(timer, new SandboxPartOverrides(TimerSeconds: 0.0f));
        network.Rebuild(document);
        Assert.Equal(1, network.IntervalTicksOf(timer));   // 0.1 s at 5 ticks a second, never below one tick
    }

    [Fact]
    public void A_lamp_toggles_and_a_weapon_trigger_fires_every_pulse()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId lamp = Place(document, SandboxPartCatalogue.Lamp);
        SandboxPartId trigger = Place(document, SandboxPartCatalogue.WeaponTrigger);
        Wire(document, button, lamp);
        Wire(document, button, trigger);
        var network = new SandboxSignalNetwork(Delay);
        network.Rebuild(document);

        network.Press(button);
        List<SandboxDeviceCommand> first = Run(network, 1);
        network.Press(button);
        List<SandboxDeviceCommand> second = Run(network, 1);

        Assert.Contains(new SandboxDeviceCommand(lamp, SandboxDeviceAction.LampOn), first);
        Assert.Contains(new SandboxDeviceCommand(trigger, SandboxDeviceAction.WeaponFire), first);
        Assert.Contains(new SandboxDeviceCommand(lamp, SandboxDeviceAction.LampOff), second);
        Assert.Contains(new SandboxDeviceCommand(trigger, SandboxDeviceAction.WeaponFire), second);
        Assert.False(network.IsLampLit(lamp));
    }

    [Fact]
    public void Delivery_order_does_not_depend_on_the_order_wires_were_made()
    {
        List<SandboxDeviceCommand> Build(bool reversed)
        {
            var ids = Enumerable.Range(0, 3).Select(index => System.Guid.Parse($"00000000-0000-0000-0000-00000000000{index + 1}")).ToArray();
            var parts = new[]
            {
                new PlacedSandboxPart(SandboxPartId.From(ids[0]), SandboxPartCatalogue.Button, new CanonicalRoomPosition(0.1f, 0.1f), 0, default),
                new PlacedSandboxPart(SandboxPartId.From(ids[1]), SandboxPartCatalogue.Piston, new CanonicalRoomPosition(0.2f, 0.1f), 0, default),
                new PlacedSandboxPart(SandboxPartId.From(ids[2]), SandboxPartCatalogue.WeaponTrigger, new CanonicalRoomPosition(0.3f, 0.1f), 0, default),
            };
            var document = new SandboxDocument(parts);
            SandboxPartId[] targets = reversed ? [parts[2].PartId, parts[1].PartId] : [parts[1].PartId, parts[2].PartId];
            foreach (SandboxPartId target in targets)
                Wire(document, parts[0].PartId, target);
            var network = new SandboxSignalNetwork(Delay);
            network.Rebuild(document);
            network.Press(parts[0].PartId);
            return Run(network, 1);
        }

        Assert.Equal(Build(reversed: false), Build(reversed: true));
    }

    [Fact]
    public void Timers_switching_each_other_in_a_ring_stay_bounded()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId[] timers = Enumerable.Range(0, 12).Select(_ => Place(document, SandboxPartCatalogue.Timer)).ToArray();
        Wire(document, button, timers[0]);
        for (int index = 0; index < timers.Length; index++)
        {
            Assert.True(Wire(document, timers[index], timers[(index + 1) % timers.Length]).Succeeded);
            Assert.True(Wire(document, timers[index], timers[(index + 2) % timers.Length]).Succeeded);
        }
        var network = new SandboxSignalNetwork(1);
        network.Rebuild(document);

        network.Press(button);
        List<SandboxDeviceCommand> commands = Run(network, 200);

        // Timers only schedule; the ring ends as twelve switches, never a runaway pile of pulses.
        Assert.Empty(commands);
        Assert.Equal(0, network.DroppedPulses);
    }

    [Fact]
    public void Cutting_a_wire_or_removing_a_device_mid_wait_stops_what_it_would_have_done()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId timer = Place(document, SandboxPartCatalogue.Timer);
        SandboxPartId piston = Place(document, SandboxPartCatalogue.Piston);
        Wire(document, button, timer);
        SandboxWire toPiston = Wire(document, timer, piston).Wire!;
        var network = new SandboxSignalNetwork(Delay);
        network.Rebuild(document);

        network.Press(button);
        Run(network, 1);
        document.RemoveWire(toPiston.WireId);
        network.Rebuild(document);
        Assert.Empty(Run(network, Delay * 2));

        Wire(document, timer, piston);
        network.Rebuild(document);
        network.Press(button);
        Run(network, 1);
        document.Remove(timer);
        network.Rebuild(document);
        Assert.Empty(Run(network, Delay * 2));
        Assert.False(network.IsTimerRunning(timer));
    }

    [Fact]
    public void Wires_survive_save_snapshot_and_duplication_and_bad_ones_are_dropped_on_load()
    {
        var document = new SandboxDocument();
        SandboxPartId button = Place(document, SandboxPartCatalogue.Button);
        SandboxPartId lamp = Place(document, SandboxPartCatalogue.Lamp);
        SandboxWire wire = Wire(document, button, lamp).Wire!;

        SandboxDocumentDecodeResult decoded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document));
        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(wire, Assert.Single(decoded.Document!.Wires));
        Assert.Equal(document.Revision, decoded.Document.Revision);
        Assert.Equal(wire, Assert.Single(document.Snapshot().Wires));

        SandboxDocument copy = document.CopyWithNewPartIds();
        SandboxWire copied = Assert.Single(copy.Wires);
        Assert.NotEqual(wire.WireId, copied.WireId);
        Assert.Equal(SandboxDeviceKind.Button, copy.DeviceOf(copied.From));
        Assert.Equal(SandboxDeviceKind.Lamp, copy.DeviceOf(copied.To));

        // A wire backwards, a wire to a part that is gone, and a room from before wires existed.
        JsonObject save = JsonNode.Parse(SandboxSavePolicy.Serialize(document))!.AsObject();
        JsonArray wires = save["wires"]!.AsArray();
        wires.Add(new JsonObject
        {
            ["wireId"] = System.Guid.NewGuid(), ["fromPartId"] = lamp.Value, ["fromPort"] = "out",
            ["toPartId"] = button.Value, ["toPort"] = "in",
        });
        wires.Add(new JsonObject
        {
            ["wireId"] = System.Guid.NewGuid(), ["fromPartId"] = button.Value, ["fromPort"] = "out",
            ["toPartId"] = System.Guid.NewGuid(), ["toPort"] = "in",
        });
        SandboxDocument damaged = SandboxSavePolicy.Decode(save.ToJsonString()).Document!;
        Assert.Equal(wire, Assert.Single(damaged.Wires));

        save.Remove("wires");
        save["schemaVersion"] = 2;
        SandboxDocumentDecodeResult older = SandboxSavePolicy.Decode(save.ToJsonString());
        Assert.Equal(SaveDecodeStatus.Valid, older.Status);
        Assert.Empty(older.Document!.Wires);
        Assert.Equal(2, older.Document.Count);
    }
}
