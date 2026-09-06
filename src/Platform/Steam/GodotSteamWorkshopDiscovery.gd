extends Node

## Optional discovery-only adapter for the in-game Workshop browser. The normal GodotSteamBridge
## owns Steam initialization and publishing/downloads; this helper only issues UGC discovery
## queries against that already-initialized singleton. Keeping it separate means an older or
## differently-bound GodotSteam build can fail discovery without disabling the rest of Desktop
## Buddy's Workshop.

signal browse_query_completed(handle: int, result: int, results_returned: int, total_matching: int)

const UGC_QUERY_RANKED_BY_VOTE := 0
const UGC_QUERY_RANKED_BY_PUBLICATION_DATE := 1
const UGC_MATCHING_ITEMS := 0
const QUERY_ALL_PAGE_ARGUMENT_COUNT := 5

var _steam: Object
var _app_id := 0
var _supported := false
var _reason := "Workshop discovery has not been configured."
var _create_query_all_method := ""

var _required_methods := PackedStringArray([
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

    # GodotSteam 4.22 exposes two overloads of SteamUGC::CreateQueryAllUGCRequest. Depending on
    # the generated GDExtension binding, the page-based five-argument overload can receive a
    # disambiguated name rather than the plain createQueryAllUGCRequest identifier. Resolve that
    # binding by signature instead of pinning Desktop Buddy to one generated spelling.
    _create_query_all_method = _resolve_create_query_all_method()
    if _create_query_all_method.is_empty():
        return _fail(_missing_query_all_detail())

    if not _steam.has_signal("ugc_query_completed"):
        return _fail("GodotSteam is missing the UGC query completion signal.")

    var callback := Callable(self, "_on_ugc_query_completed")
    if not _steam.is_connected("ugc_query_completed", callback):
        _steam.connect("ugc_query_completed", callback)

    _app_id = app_id
    _supported = true
    _reason = ""
    return {"ok": true, "query_method": _create_query_all_method}

func is_supported() -> bool:
    return _supported and _steam != null and _app_id > 0 and not _create_query_all_method.is_empty()

func unavailable_reason() -> String:
    return _reason

func query_page(sort_mode: int, page: int) -> int:
    if not is_supported():
        return -1

    var query_type := UGC_QUERY_RANKED_BY_VOTE if sort_mode == 0 else UGC_QUERY_RANKED_BY_PUBLICATION_DATE
    var safe_page := maxi(1, page)
    var handle := int(_steam.call(
        _create_query_all_method,
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

func _resolve_create_query_all_method() -> String:
    # Prefer the canonical name when a build can expose it without overload disambiguation.
    if _steam.has_method("createQueryAllUGCRequest"):
        var canonical_args := _method_argument_count("createQueryAllUGCRequest")
        if canonical_args == -1 or canonical_args == QUERY_ALL_PAGE_ARGUMENT_COUNT:
            return "createQueryAllUGCRequest"

    var fallback := ""
    for method_info_variant in _steam.get_method_list():
        if typeof(method_info_variant) != TYPE_DICTIONARY:
            continue
        var method_info: Dictionary = method_info_variant
        var method_name := str(method_info.get("name", ""))
        var folded := method_name.to_lower()
        if not folded.contains("createqueryallugc"):
            continue

        var args_variant: Variant = method_info.get("args", [])
        var argument_count := -1
        if typeof(args_variant) == TYPE_ARRAY:
            argument_count = (args_variant as Array).size()

        # The page-based Steam API has exactly five arguments. Pick it over the cursor overload.
        if argument_count == QUERY_ALL_PAGE_ARGUMENT_COUNT:
            return method_name
        if fallback.is_empty():
            fallback = method_name

    # If the binding does not expose argument metadata but only one similarly named method exists,
    # keep the browser usable rather than rejecting a potentially compatible GodotSteam build.
    return fallback

func _method_argument_count(wanted_name: String) -> int:
    for method_info_variant in _steam.get_method_list():
        if typeof(method_info_variant) != TYPE_DICTIONARY:
            continue
        var method_info: Dictionary = method_info_variant
        if str(method_info.get("name", "")) != wanted_name:
            continue
        var args_variant: Variant = method_info.get("args", [])
        if typeof(args_variant) == TYPE_ARRAY:
            return (args_variant as Array).size()
        return -1
    return -1

func _missing_query_all_detail() -> String:
    var candidates := PackedStringArray()
    for method_info_variant in _steam.get_method_list():
        if typeof(method_info_variant) != TYPE_DICTIONARY:
            continue
        var method_name := str((method_info_variant as Dictionary).get("name", ""))
        var folded := method_name.to_lower()
        if folded.contains("queryall") or folded.contains("queryugc"):
            candidates.append(method_name)
    var suffix := ""
    if not candidates.is_empty():
        suffix = " Available query methods: %s" % ", ".join(candidates)
    return "This GodotSteam build cannot browse Workshop content because no compatible CreateQueryAllUGCRequest overload was found.%s" % suffix

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
