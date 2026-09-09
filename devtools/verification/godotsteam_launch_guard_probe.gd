extends SceneTree

const GUARD_SCRIPT_PATH := "res://src/Platform/Steam/GodotSteamLaunchGuard.gd"

func _initialize() -> void:
    var script: GDScript = load(GUARD_SCRIPT_PATH)
    if script == null:
        push_error("Steam launch guard script could not be loaded: %s" % GUARD_SCRIPT_PATH)
        quit(1)
        return

    var instance: Variant = script.new()
    if not instance is Node:
        push_error("Steam launch guard did not instantiate as a Node.")
        quit(1)
        return

    var guard: Node = instance
    var supported := bool(guard.call("has_restart_capability"))
    guard.free()
    if not supported:
        push_error("Pinned GodotSteam is missing restartAppIfNecessary required by the launch guard.")
        quit(1)
        return

    print("GodotSteam launch guard capability OK: restartAppIfNecessary")
    quit(0)
