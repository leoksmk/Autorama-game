// Onda de choque: anel que se expande num plano. `k` vai de 0 a 1.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 cor : source_color = vec3(1.0, 0.6, 0.3);
uniform float k = 0.0;

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float largura = 0.05 + 0.12 * k;
	float anel = exp(-pow((r - k) / largura, 2.0));
	float a = anel * (1.0 - k) * step(r, 1.0);
	ALBEDO = cor * 3.0;
	ALPHA = clamp(a, 0.0, 1.0);
}
