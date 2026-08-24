extends Node

## Dynamic anti-corruption bridge around the optional GodotSteam GDExtension. The script never
## references the global Steam symbol directly, so Desktop Buddy still parses/boots when the
## extension is physically absent.

signal bridge_state_changed(available: bool, reason: String)
signal workshop_item_created(result: int, file_id: int, needs_legal_agreement: bool)
signal workshop_item_updated(result: int, needs_legal_agreement: bool)
signal workshop_item_downloaded(result: int, app_id: int, file_id: int)

const EXPECTED_GODOTSTEAM := "4.22"
const WORKSHOP_FILE_TYPE_COMMUNITY := 0

var _steam: Object
var _initialized := false
var _app_id := 0
var _reason := "GodotSteam has not been initialized."

var _required_methods := PackedStringArray([
    "steamInitEx",
    "run_callbacks",
    "createItem",
    "startItemUpdate",
    "setItemTitle",
    "setItemDescription",
    "setItemVisibility",
    "setItemTags",
    "setItemMetadata",
    "setItemContent",
    "setItemPreview",
    "submitItemUpdate",
    "getItemUpdateProgress",
    "getSubscribedItems",
    "getItemState",
    "downloadItem",
    "getItemDownloadInfo",
    "getItemInstallInfo",
    "activateGameOverlayToWebPage",
])

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    set_process(true)

func initialize(app_id: int) -> Dictionary:
    if _initialized:
        return {"status": 0, "verbal": "Steam is already initialized.", "version": EXPECTED_GODOTSTEAM}
    if app_id <= 0:
        return _fail("No Steam AppID is configured.")

    _steam = _find_steam()
    if _steam == null:
        return _fail("GodotSteam is not installed; local play remains available.")

    var missing := PackedStringArray()
    for method_name in _required_methods:
        if not _steam.has_method(method_name):
            missing.append(method_name)
    if not missing.is_empty():
        return _fail("Unsupported GodotSteam capability set; expected %s. Missing: %s" % [EXPECTED_GODOTSTEAM, ", ".join(missing)])

    if not _connect_required_signal("item_created", Callable(self, "_on_item_created")):
        return _fail("GodotSteam is missing the item_created signal.")
    if not _connect_required_signal("item_updated", Callable(self, "_on_item_updated")):
        return _fail("GodotSteam is missing the item_updated signal.")

    # GodotSteam has historically exposed DownloadItemResult_t as item_downloaded. Keep one
    # compatibility alias for builds that expose the SDK callback name instead.
    if _steam.has_signal("item_downloaded"):
        _connect_once("item_downloaded", Callable(self, "_on_item_downloaded"))
    elif _steam.has_signal("download_item_result"):
        _connect_once("download_item_result", Callable(self, "_on_download_item_result"))
    else:
        return _fail("GodotSteam is missing the Workshop download-result signal.")

    # Since GodotSteam 4.14 the arguments are app_id first, embed_callbacks second. We keep
    # embed_callbacks false and explicitly pump run_callbacks() from this always-processing node.
    var init_result: Variant = _steam.call("steamInitEx", app_id, false)
    if typeof(init_result) != TYPE_DICTIONARY:
        return _fail("GodotSteam steamInitEx returned an unexpected value.")
    var response: Dictionary = init_result
    var status := int(response.get("status", -1))
    if status != 0:
        return _fail(str(response.get("verbal", "Steam initialization failed.")), status)

    _app_id = app_id
    _initialized = true
    _reason = ""
    bridge_state_changed.emit(true, "")
    return {"status": 0, "verbal": str(response.get("verbal", "Steam initialized.")), "version": EXPECTED_GODOTSTEAM}

func is_available() -> bool:
    return _initialized and _steam != null

func unavailable_reason() -> String:
    return _reason

func app_id() -> int:
    return _app_id

func _process(_delta: float) -> void:
    if _initialized and _steam != null:
        _steam.call("run_callbacks")

func shutdown() -> void:
    if _initialized and _steam != null and _steam.has_method("steamShutdown"):
        _steam.call("steamShutdown")
    _initialized = false
    _app_id = 0

func create_item(app_id: int) -> bool:
    if not is_available() or app_id != _app_id:
        return false
    _steam.call("createItem", app_id, WORKSHOP_FILE_TYPE_COMMUNITY)
    return true

func start_item_update(app_id: int, file_id: int) -> int:
    if not is_available() or app_id != _app_id or file_id <= 0:
        return 0
    return int(_steam.call("startItemUpdate", app_id, file_id))

func set_item_title(update_handle: int, title: String) -> bool:
    return _call_bool("setItemTitle", [update_handle, title])

func set_item_description(update_handle: int, description: String) -> bool:
    return _call_bool("setItemDescription", [update_handle, description])

func set_item_visibility(update_handle: int, visibility: int) -> bool:
    return _call_bool("setItemVisibility", [update_handle, visibility])

func set_item_tags(update_handle: int, tags: PackedStringArray) -> bool:
    return _call_bool("setItemTags", [update_handle, tags])

func set_item_metadata(update_handle: int, metadata: String) -> bool:
    return _call_bool("setItemMetadata", [update_handle, metadata])

func set_item_content(update_handle: int, absolute_folder: String) -> bool:
    return _call_bool("setItemContent", [update_handle, absolute_folder])

func set_item_preview(update_handle: int, absolute_file: String) -> bool:
    if absolute_file.is_empty():
        return true
    return _call_bool("setItemPreview", [update_handle, absolute_file])

func submit_item_update(update_handle: int, change_note: String) -> bool:
    if not is_available() or update_handle <= 0:
        return false
    _steam.call("submitItemUpdate", update_handle, change_note)
    return true

func get_item_update_progress(update_handle: int) -> Dictionary:
    if not is_available() or update_handle <= 0:
        return {}
    var value: Variant = _steam.call("getItemUpdateProgress", update_handle)
    return value if typeof(value) == TYPE_DICTIONARY else {}

func get_subscribed_items() -> PackedInt64Array:
    if not is_available():
        return PackedInt64Array()
    var value: Variant = _steam.call("getSubscribedItems", false)
    if typeof(value) == TYPE_PACKED_INT64_ARRAY:
        return value
    if typeof(value) == TYPE_ARRAY:
        var ids := PackedInt64Array()
        for item in value:
            ids.append(int(item))
        return ids
    return PackedInt64Array()

func get_item_state(file_id: int) -> int:
    if not is_available() or file_id <= 0:
        return 0
    return int(_steam.call("getItemState", file_id))

func download_item(file_id: int, high_priority: bool = false) -> bool:
    return _call_bool("downloadItem", [file_id, high_priority])

func get_item_download_info(file_id: int) -> Dictionary:
    if not is_available() or file_id <= 0:
        return {}
    var value: Variant = _steam.call("getItemDownloadInfo", file_id)
    return value if typeof(value) == TYPE_DICTIONARY else {}

func get_item_install_info(file_id: int) -> Dictionary:
    if not is_available() or file_id <= 0:
        return {}
    var value: Variant = _steam.call("getItemInstallInfo", file_id)
    return value if typeof(value) == TYPE_DICTIONARY else {}

func open_workshop_browser(app_id: int) -> void:
    if not is_available() or app_id != _app_id:
        return
    _steam.call("activateGameOverlayToWebPage", "https://steamcommunity.com/app/%d/workshop/" % app_id)

func open_workshop_item(file_id: int) -> void:
    if not is_available() or file_id <= 0:
        return
    _steam.call("activateGameOverlayToWebPage", "steam://url/CommunityFilePage/%d" % file_id)

func _find_steam() -> Object:
    if Engine.has_singleton("Steam"):
        return Engine.get_singleton("Steam")
    if ClassDB.class_exists(&"Steam"):
        var instance: Variant = ClassDB.instantiate(&"Steam")
        if instance is Object:
            return instance
    return null

func _call_bool(method_name: StringName, args: Array) -> bool:
    if not is_available() or _steam == null or not _steam.has_method(method_name):
        return false
    var value: Variant = _steam.callv(method_name, args)
    return bool(value)

func _connect_required_signal(signal_name: StringName, callable: Callable) -> bool:
    if _steam == null or not _steam.has_signal(signal_name):
        return false
    _connect_once(signal_name, callable)
    return true

func _connect_once(signal_name: StringName, callable: Callable) -> void:
    if not _steam.is_connected(signal_name, callable):
        _steam.connect(signal_name, callable)

func _on_item_created(result: int, file_id: int, needs_legal_agreement: bool) -> void:
    workshop_item_created.emit(result, file_id, needs_legal_agreement)

func _on_item_updated(result: int, needs_legal_agreement: bool) -> void:
    workshop_item_updated.emit(result, needs_legal_agreement)

func _on_item_downloaded(result: int, app_id: int, file_id: int) -> void:
    workshop_item_downloaded.emit(result, app_id, file_id)

func _on_download_item_result(app_id: int, file_id: int, result: int) -> void:
    workshop_item_downloaded.emit(result, app_id, file_id)

func _fail(message: String, status: int = -1) -> Dictionary:
    _initialized = false
    _reason = message
    bridge_state_changed.emit(false, message)
    return {"status": status, "verbal": message, "version": EXPECTED_GODOTSTEAM}
