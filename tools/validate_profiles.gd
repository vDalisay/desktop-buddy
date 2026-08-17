extends SceneTree

func _init() -> void:
	for path in [
		"res://data/buddy/lab_cursor_tool_boxing_glove.tres",
		"res://data/buddy/lab_cursor_tool_baseball_bat.tres",
	]:
		var res = load(path)
		print("== ", path, " loaded=", res != null)
		if res != null:
			print("   swing=", res.get("Swing"))
			print("   errors=", res.Validate())
	quit()
