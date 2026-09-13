// Placas hexagonais da ÍRIS-9. A cor de cada placa vem no vértice; o alfa do
// vértice marca as placas de vidro. Nelas o shader desenha fileiras de janelas
// pequenas, só algumas acesas — a placa inteira acesa parecia um olho.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/ruido.gdshaderinc"

uniform vec3 cor_janela : source_color = vec3(1.0, 0.86, 0.66);
uniform float energia_janela = 3.0;

varying vec3 pos_objeto;

void vertex() {
	pos_objeto = VERTEX;
}

void fragment() {
	float vidro = COLOR.a;

	// Grade em longitude (comprimento de arco) e altura: ~0,3 m por janela.
	vec2 g = vec2(atan(pos_objeto.z, pos_objeto.x) * length(pos_objeto.xz) * 1.6, pos_objeto.y * 2.4);
	vec2 f = fract(g);
	vec2 aa = fwidth(g) * 0.75;
	vec2 dentro = smoothstep(vec2(0.22, 0.32) - aa, vec2(0.22, 0.32) + aa, f)
	            * (1.0 - smoothstep(vec2(0.78, 0.68) - aa, vec2(0.78, 0.68) + aa, f));
	float janela = dentro.x * dentro.y;
	// De longe a grade vira média, em vez de cintilar.
	janela = mix(janela, 0.2, clamp(max(aa.x, aa.y) * 2.0 - 0.5, 0.0, 1.0));
	float acesa = step(0.45, hash21(floor(g)));

	ALBEDO = mix(COLOR.rgb, vec3(0.03, 0.035, 0.05), vidro * 0.85);
	METALLIC = mix(0.5, 0.2, vidro);
	ROUGHNESS = mix(0.3 + fract(COLOR.r * 97.0 + COLOR.g * 31.0) * 0.28, 0.08, vidro);
	EMISSION = cor_janela * vidro * (janela * acesa * energia_janela + 0.04);
}
