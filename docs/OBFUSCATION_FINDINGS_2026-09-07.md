# Obfuscation: what was tried, what happened, and why nothing shipped

Status: **investigated, not adopted.** Four configurations were built and run against the real game.
None produced a build that is both shippable and meaningfully protected. This records the evidence
so the question does not have to be re-opened from scratch.

Companion to `docs/ITCH_HARDENING_PLAN_2026-09-07.md` §5, which predicted this was a hard case. It
is worse than predicted, for a reason that was not in the prediction.

## The blocker

**Godot names a C# node after its class, at runtime, by reflection. The code looks those nodes up
with `nameof`, which is baked at compile time.** Obfuscation renames the class; `nameof` already
baked the original string; the two no longer agree and the lookup fails.

Observed directly. With type renaming on, the game boots and runs, then:

```
WARNING: [Onboarding] Tutorial step 'demo.onboarding.open_inventory'
         found no 'Win98ShopCommand' under A.
```

`A` is a node whose name came from its obfuscated class. `Win98ShopCommand` is a literal and
survived. The mismatch is the whole story.

This is not a configuration problem. Fixing it means giving every node found this way an explicit
literal `Name`, everywhere — the codebase has 286 `FindChild("...")` lookups and 38
`Name = nameof(T)` sites. That is a large, invasive change to working code, made to enable a measure
that protects against the *least likely* of the four threats in the plan's threat model.

## What was tried

Tool: **Obfuscar 2.2.50**, the free tool the Godot community actually uses. Note the global tool
shim fails on this machine (`hostfxr` architecture mismatch); run it as
`dotnet <tools>/GlobalTools.dll <config>.xml`.

| Config | Rules | Result |
| --- | --- | --- |
| v1 conservative | skip Godot bridge, generated name tables, all `Node`/`Resource`/`RefCounted` subclasses, JSON-shaped types, enums | **Scenarios pass.** But 18,199 members skipped by type rules. In a Godot game almost everything is a Node, so this protects very little. |
| v2 aggressive | skip only the Godot bridge and `Resource` subclasses | Boots, then `Scenario registry field was not found` — the developer scenario tree reflects on a field by name. |
| v4 aggressive + registry kept | v2 plus keep `ScenarioCatalog` | **Boots and runs, then cannot find its own nodes.** This is the blocker above. |
| v5/v6 members only, type names kept | skip every type rename, rename methods and fields | Crashes before producing any output at all. Not diagnosed further — see below. |

v5/v6 was the promising direction: keeping type names should keep Godot's auto-naming and `nameof`
in agreement, while still destroying readability. It deserves another look if this is revisited,
but it did not work off the shelf.

## Two things the attempt is worth regardless

**It found a live unlock.** v2 failed on the scenario registry's reflection, which is how it came to
light that 144 developer scenario files were compiled into the Steam Demo and the Full Release, with
`Bootstrap` routing a scenario argument to the `TestRunner` and
`BuddyStudioUiCompositionScenario` setting `DemoScope.FullReleaseOverride = true`. A Demo player
could open full-release content from the command line of the shipped exe. That is now fixed
(`DesktopBuddyShippingBuild`), and it was a shorter unlock path than anything obfuscation would have
closed.

**The verification gap is structural.** The scenario suite cannot test an obfuscated *shipping*
assembly, because shipping builds no longer contain the suite. Testing with scenarios present is not
the same artifact — and it is actively misleading: Obfuscar aborted with
`Inconsistent virtual method obfuscation state` purely because a test-only `ICharacterFileSystem`
implementation was skipped while the real ones were renamed. Any future attempt needs a manual pass
over every screen of an actual export, not a green suite.

## Recommendation

**Do not pursue this for the browser build.** It would have to run on the IL before AOT, inside a
pipeline that compiles a pinned Godot fork, to protect the hardest-to-read of the three
distributions against the rarest threat. The plan's §2 ranking still holds: obfuscation does nothing
about wholesale reupload or `.pck` asset extraction, which are what actually happen.

**For the Windows builds it is still the right target if pursued** — they ship plain IL — but the
node-naming conflict applies to them equally, because it is the same code. The prerequisite is
explicit node names, not a better Obfuscar config.

If the goal is specifically "make the Windows builds not trivially readable", the cheaper move is to
close the gap that made them the soft target in the first place: they currently ship with no
trimming and no AOT, unlike the browser build. `PublishTrimmed` and a native AOT path would raise
the floor without touching a single node name.
