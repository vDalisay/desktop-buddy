extends Node

## Tiny pre-initialization boundary for SteamAPI_RestartAppIfNecessary. Keep this separate from
## GodotSteamBridge: the Workshop bridge owns Steam initialization and remote services, while this
## object may only perform the optional launch-context check before steamInitEx is ever called.
##
## The C# caller first verifies that the Windows Steam client process is already running. That is
## intentional: Valve's restart helper may start Steam when it is closed, but Desktop Buddy remains
## offline-first and must not force the client open for a direct local launch.

const EXPECTED_GODOTSTEAM := "4.22"

func has_restart_capability() -> bool:
    var steam := _find_steam()
    return steam != null and steam.has_method("restartAppIfNecessary")

func request_restart_if_necessary(app_id: int) -> Dictionary:
    if app_id <= 0:
        return _continue(false, "No Steam AppID is configured.")

    var steam := _find_steam()
    if steam == null:
        return _continue(false, "GodotSteam is not installed; continuing local play.")
    if not steam.has_method("restartAppIfNecessary"):
        return _continue(
            false,
            "GodotSteam %s launch capability is unavailable; continuing local play." % EXPECTED_GODOTSTEAM)

    # This must remain the first Steamworks API call made by Desktop Buddy. The caller performs
    # only an OS-level process-presence check before reaching this boundary; Steam initialization
    # happens later in GodotSteamBridge.
    var result: Variant = steam.call("restartAppIfNecessary", app_id)
    if typeof(result) != TYPE_BOOL:
        return _continue(false, "GodotSteam restartAppIfNecessary returned an unexpected value.")

    if bool(result):
        return {
            "capability_available": true,
            "restart_requested": true,
            "reason": "Steam requested a relaunch through AppID %d." % app_id,
        }

    return {
        "capability_available": true,
        "restart_requested": false,
        "reason": "Launch context already satisfies Steam or the restart helper declined relaunch.",
    }

func _continue(capability_available: bool, reason: String) -> Dictionary:
    return {
        "capability_available": capability_available,
        "restart_requested": false,
        "reason": reason,
    }

func _find_steam() -> Object:
    if Engine.has_singleton("Steam"):
        return Engine.get_singleton("Steam")
    if ClassDB.class_exists(&"Steam"):
        var instance: Variant = ClassDB.instantiate(&"Steam")
        if instance is Object:
            return instance
    return null
