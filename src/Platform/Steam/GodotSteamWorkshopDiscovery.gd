extends Node

## Optional discovery-only adapter for the in-game Workshop browser. The normal GodotSteamBridge
## owns Steam initialization and publishing/downloads; this helper only issues UGC discovery
## queries against that already-initialized singleton. Keeping it separate means an older
## GodotSteam build can fail discovery without disabling the rest of Desktop Buddy's Workshop.

signal browse_query_completed(handle: int, result: int, results_returned: int, total_matching: int)

const UGC_QUERY_RANKED_BY_VOTE := 0
const UGC_QUERY_RANKED_BY_PUBLICATION_DATE := 1
const UGC_MATCHING_ITEMS := 0

var _steam: Object
var _app_id := 0
var _supported := false
var _reason := "Workshop discovery has not been configured."

var _required_methods := PackedStringArray([
    "createQueryAllUGCRequest",
    "sendQueryUGCRequest",
    "getQueryUGCResult",
    "getQueryUGCPreviewURL",
    "releaseQueryUGCRequest",
    "subscribeItem",
    "getItemState",
])

func configure(app_id: int) -> Dictionary:
    if app_id <= 0:
        return _fail("No Workshop AppID is configured for discovery.")

    _steam = _find_steam()
    if _steam == null:
        return _fail("GodotSteam is unavailable.")

    var missing := PackedStringArray()
    for method_name in _required_methods:
        if not _steam.has_method(method_name):
            missing.append(method_name)
    if not missing.is_empty():
        return _fail("This GodotSteam build cannot browse Workshop content. Missing: %s" % ", ".join(missing))

    if not _steam.has_signal("ugc_query_completed"):
        return _fail("GodotSteam is missing the UGC query completion signal.")

    var callback := Callable(self, "_on_ugc_query_completed")
    if not _steam.is_connected("ugc_query_completed", callback):
        _steam.connect("ugc_query_completed", callback)

    _app_id = app_id
    _supported = true
    _reason = ""
    return {"ok": true}

func is_supported() -> bool:
    return _supported and _steam != null and _app_id > 0

func unavailable_reason() -> String:
    return _reason

func query_page(sort_mode: int, page: int) -> int:
    if not is_supported():
        return -1

    var query_type := UGC_QUERY_RANKED_BY_VOTE if sort_mode == 0 else UGC_QUERY_RANKED_BY_PUBLICATION_DATE
    var safe_page := maxi(1, page)
    var handle := int(_steam.call(
        "createQueryAllUGCRequest",
        query_type,
        UGC_MATCHING_ITEMS,
        _app_id,
        _app_id,
        safe_page))
    if handle < 0:
        return -1

    # These are presentation improvements only. A GodotSteam patch lacking one of them still
    # returns a usable query, so do not make browsing depend on the optional setters.
    if _steam.has_method("setReturnLongDescription"):
        _steam.call("setReturnLongDescription", handle, true)
    if _steam.has_method("setReturnMetadata"):
        _steam.call("setReturnMetadata", handle, true)

    _steam.call("sendQueryUGCRequest", handle)
    return handle

func get_query_item_result(handle: int, index: int) -> Dictionary:
    if not is_supported() or handle < 0 or index < 0:
        return {}
    var raw: Variant = _steam.call("getQueryUGCResult", handle, index)
    if typeof(raw) != TYPE_DICTIONARY:
        return {}

    var details: Dictionary = raw.duplicate(true)
    var preview: Variant = _steam.call("getQueryUGCPreviewURL", handle, index)
    if typeof(preview) == TYPE_STRING:
        details["preview_url"] = str(preview)
    elif typeof(preview) == TYPE_DICTIONARY:
        details["preview_url"] = str(preview.get("url", preview.get("preview_url", "")))
    return details

func release_query(handle: int) -> void:
    if is_supported() and handle >= 0:
        _steam.call("releaseQueryUGCRequest", handle)

func subscribe_item(file_id: int) -> bool:
    if not is_supported() or file_id <= 0:
        return false
    _steam.call("subscribeItem", file_id)
    return true

func get_item_state(file_id: int) -> int:
    if not is_supported() or file_id <= 0:
        return 0
    return int(_steam.call("getItemState", file_id))

func _on_ugc_query_completed(
    handle: int,
    result: int,
    results_returned: int,
    total_matching: int,
    _cached: bool,
    _next_cursor: String) -> void:
    browse_query_completed.emit(handle, result, results_returned, total_matching)

func _find_steam() -> Object:
    if Engine.has_singleton("Steam"):
        return Engine.get_singleton("Steam")
    if ClassDB.class_exists(&"Steam"):
        var instance: Variant = ClassDB.instantiate(&"Steam")
        if instance is Object:
            return instance
    return null

func _fail(message: String) -> Dictionary:
    _supported = false
    _reason = message
    return {"ok": false, "reason": message}
