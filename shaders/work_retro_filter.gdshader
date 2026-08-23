shader_type canvas_item;

// Work Mode's optional retro pass (owner instruction 2026-08-23). It runs per CanvasItem — the
// PC art and the buddy's viewport each wear the same material — rather than as a screen-reading
// overlay, so it needs no BackBufferCopy and cannot tint the transparent window around them.
//
// Alpha is left exactly as it arrives. Work Mode is a shaped, transparent window: darkening or
// quantising alpha would either punch holes in the art or paint a grey box over the desktop.

// Source pixels per retro pixel. 1.0 leaves the art's own resolution alone.
uniform float chunk : hint_range(1.0, 8.0) = 2.0;
// Colour steps per channel. Fewer steps reads as a cheaper display.
uniform float levels : hint_range(2.0, 32.0) = 6.0;
// How much every other screen row is dimmed.
uniform float scanline : hint_range(0.0, 1.0) = 0.22;
// Vertical period of the scanlines, in screen pixels.
uniform float scanline_period : hint_range(2.0, 8.0) = 3.0;

void fragment() {
	vec2 texel = TEXTURE_PIXEL_SIZE * chunk;
	vec2 snapped = (floor(UV / texel) + 0.5) * texel;
	vec4 source = texture(TEXTURE, snapped);

	vec3 stepped = floor(source.rgb * levels + 0.5) / levels;
	float row = mod(FRAGCOORD.y, scanline_period);
	float dim = row < 1.0 ? 1.0 - scanline : 1.0;

	COLOR = vec4(stepped * dim, source.a);
}
