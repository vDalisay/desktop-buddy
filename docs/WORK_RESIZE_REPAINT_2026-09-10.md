# Work companion resize repaint

The owner recording shows transient grey strips along the three native Work window
regions during resize. Region updates called `SetWindowRgn` with redraw enabled before
Godot presented the resized transparent frame. Updates now disable the redundant native
repaint; removing the region on Work exit still requests redraw. Existing local resize
and clear-colour edits are preserved.

Verification:

- Debug solution build passed (12 CA2255 module-initializer warnings).
- 48 focused Work tests passed, including the cross-platform native-call source guard.
- `work_mode_resilience`, seed 1: all 12 checks passed. Godot reported leaked ObjectDB
  instances and one resource at shutdown; this remains an unresolved test-cleanup issue.
- Live MCP launch was declined. The visual fix remains unconfirmed on Windows.

Remaining visual check: continuously grow and shrink using the resize button and
LMB + wheel; check for grey strips during movement, transparent-area passthrough,
unclipped art and fixed-size controls, then exit Work and confirm normal window recovery.
Headless checks and the source guard cannot verify Windows compositor pixels.

Native API reference: [SetWindowRgn redraw flag](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn).
