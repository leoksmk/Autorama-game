// Asteroides. Cada instância gira no próprio eixo pelo shader (dados
// customizados da MultiMesh), sem custo de CPU por frame.
shader_type spatial;
render_mode cull_back, diffuse_burley;

#include "res://shaders/ruido.gdshaderinc"

varying vec3 pos_objeto;

vec3 girar(vec3 v, vec3 eixo, float a) {
	float c = cos(a);
	float s = sin(a);
	return v * c + cross(eixo, v) * s + eixo * dot(eixo, v) * (1.0 - c);
}

void vertex() {
	vec3 eixo = normalize(INSTANCE_CUSTOM.rgb * 2.0 - 1.0 + vec3(0.001));
	float ang = TIME * (INSTANCE_CUSTOM.a - 0.5) * 0.5;
	pos_objeto = VERTEX;
	VERTEX = girar(VERTEX, eixo, ang);
	NORMAL = girar(NORMAL, eixo, ang);

	// Pedra que passaria colada na câmera encolhe até sumir, em vez de tampar
	// a tela. MODEL_MATRIX já inclui a transformação da instância.
	vec3 centro = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float raio = length(MODEL_MATRIX[0].xyz);
	float folga = distance(centro, CAMERA_POSITION_WORLD) - raio * 1.4;
	VERTEX *= smoothstep(1.0, 6.0, folga);
}

void fragment() {
	float n = ruido(pos_objeto * 3.0) * 0.55
	        + ruido(pos_objeto * 9.0) * 0.3
	        + ruido(pos_objeto * 25.0) * 0.15;
	vec3 escuro = vec3(0.13, 0.12, 0.115);
	vec3 claro = vec3(0.33, 0.29, 0.25);
	ALBEDO = mix(escuro, claro, n) * (0.8 + 0.35 * ruido(pos_objeto * 1.2));
	ROUGHNESS = 0.8 + n * 0.18;
	METALLIC = 0.04;
}
