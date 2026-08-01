shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform vec4 core_color : source_color;
uniform vec4 flame_color : source_color;
uniform vec4 smoke_color : source_color;
uniform float age : hint_range(0.0, 1.0) = 0.0;
uniform float phase = 0.0;
uniform float seed = 0.0;

float hash(vec2 point) {
	return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 point) {
	vec2 cell = floor(point);
	vec2 local = fract(point);
	local = local * local * (3.0 - 2.0 * local);
	return mix(
		mix(hash(cell), hash(cell + vec2(1.0, 0.0)), local.x),
		mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), local.x),
		local.y);
}

void fragment() {
	vec2 flow_uv = (UV * 5.0) + vec2(seed, -phase * 0.35);
	float foam = noise(flow_uv) * 0.65 + noise(flow_uv * 2.07 + 4.2) * 0.35;
	float breakup = smoothstep(0.22 + age * 0.12, 0.72, foam + (1.0 - age) * 0.28);
	float edge = 1.0 - smoothstep(0.58, 1.0, length(UV * 2.0 - 1.0) + foam * 0.16);
	float smoke_mix = smoothstep(0.34, 0.92, age + foam * 0.16);
	vec3 hot = mix(core_color.rgb, flame_color.rgb, smoothstep(0.02, 0.38, age));
	vec3 colour = mix(hot, smoke_color.rgb, smoke_mix);

	ALBEDO = colour;
	EMISSION = colour * mix(2.8, 0.08, smoke_mix);
	ALPHA = breakup * edge * mix(0.9, 0.38, age);
}
