// Chama do motor. Malha em cone com UV.y = 0 no bocal e 1 na ponta.
// A intensidade vem do PWM da pista: o jogador vê o atuador.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

#include "res://shaders/ruido.gdshaderinc"

uniform vec3 cor : source_color = vec3(0.3, 0.8, 1.0);
uniform float intensidade = 1.0;

void fragment() {
	float t = UV.y;
	float frontal = pow(abs(dot(NORMAL, VIEW)), 1.2);
	float n = ruido(vec3(UV.x * 7.0, t * 5.0 - TIME * 14.0, TIME * 2.0));
	float nucleo = pow(1.0 - t, 3.0);
	vec3 c = mix(cor, vec3(1.0, 0.97, 0.9), nucleo * 0.8);
	float a = frontal * pow(1.0 - t, 1.4) * (0.55 + 0.7 * n) * intensidade;
	ALBEDO = c * 2.2;
	ALPHA = clamp(a, 0.0, 1.0);
}
