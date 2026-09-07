using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class ProjectileVisualAlignmentScenario : IScenario
{
    public string Id => "projectile_visual_alignment";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        using var world = new World2D();
        var viewport = new SubViewport { Size = new Vector2I(400, 240), World2D = world };
        tree.Root.AddChild(viewport);
        var parent = new Node2D { Rotation = 0.7f, Scale = new Vector2(1.2f, 0.8f) };
        viewport.AddChild(parent);
        try
        {
            foreach (string gun in new[] { "pistol", "shotgun" })
            {
                var body = new ProjectileBody();
                parent.AddChild(body);
                body.Configure(GD.Load<GunProfile>($"res://data/tools/gun_{gun}.tres"));
                using Shape2D shape = body.GetChild<CollisionShape2D>(0).Shape;
                bool independent = true;
                bool stayedHidden = true;
                float maxSpin = 0;
                for (int flight = 0; flight < 2; flight++)
                {
                    body.Launch(new Vector2(160, 120), new Vector2(flight == 0 ? 200 : -200, 0));
                    body.AngularVelocity = 40;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                        // _Process listeners run after ProcessFrame; let the actual visual update.
                        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                        maxSpin = Mathf.Max(maxSpin, Mathf.Abs(body.Rotation));
                        Node2D? visual = body.GetNodeOrNull<Node2D>("BrightTracer");
                        independent &= visual is not null && visual.TopLevel &&
                            visual.PhysicsInterpolationMode == Node.PhysicsInterpolationModeEnum.Off &&
                            visual.GlobalTransform.IsEqualApprox(Transform2D.Identity) && !body.LockRotation;
                    }
                    body.Park();
                    stayedHidden &= !body.GetNode<Node2D>("BrightTracer").IsVisibleInTree();
                }
                checks.Add(new StartupCheck($"{gun}_visual_ignores_physics_spin_and_parent_transform",
                    independent && maxSpin > 0.5f, $"independent={independent} spin={maxSpin:F2}rad"));
                checks.Add(new StartupCheck($"{gun}_pooled_tracer_stays_hidden", stayedHidden, $"hidden={stayedHidden}"));
                body.Free();
            }
        }
        finally
        {
            viewport.Free();
        }
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }
}
