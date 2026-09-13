// Céu do Orbital Derby: estrelas, nebulosa, faixa galáctica e a estrela
// distante — tudo procedural.
//
// Sem TIME de propósito: no vácuo estrela não cintila (não há atmosfera), e um
// céu estático deixa o Godot calcular o mapa de reflexo uma vez só.
shader_type sky;

#include "res://shaders/ruido.gdshaderinc"

uniform vec3 direcao_sol = vec3(-0.55, 0.42, -0.72);
uniform vec3 cor_sol : source_color = vec3(1.0, 0.9, 0.78);
uniform float energia_sol = 12.0;
uniform vec3 cor_nebulosa_a : source_color = vec3(0.30, 0.10, 0.52);
uniform vec3 cor_nebulosa_b : source_color = vec3(0.04, 0.30, 0.40);
uniform vec3 cor_nebulosa_c : source_color = vec3(0.55, 0.20, 0.10);
uniform float intensidade_nebulosa = 0.5;
uniform float intensidade_estrelas = 1.0;

// Uma camada de estrelas: cada célula 3D sorteia se tem estrela, e o centro
// dela é projetado de volta na esfera para o tamanho aparente ser uniforme.
vec3 camada_estrelas(vec3 dir, float escala, float densidade, float tamanho) {
	vec3 p = dir * escala;
	vec3 cel = floor(p);
	float h = hash13(cel);
	if (h < 1.0 - densidade) {
		return vec3(0.0);
	}
	vec3 desvio = vec3(hash13(cel + 7.1), hash13(cel + 13.7), hash13(cel + 29.3)) - 0.5;
	vec3 centro = normalize(cel + 0.5 + desvio * 0.6) * escala;
	float d = length(p - centro);
	float brilho = pow(hash13(cel + 3.3), 7.0) * 9.0 + 0.25;
	vec3 cor = mix(vec3(0.62, 0.74, 1.0), vec3(1.0, 0.8, 0.58), hash13(cel + 41.0));
	return cor * brilho * smoothstep(tamanho, 0.0, d);
}

void sky() {
	vec3 d = normalize(EYEDIR);
	vec3 col = vec3(0.0012, 0.0018, 0.0035);

	// Nebulosa: três escalas de ruído, cores misturadas pela segunda.
	float n1 = fbm6(d * 1.6 + vec3(3.0));
	float n2 = fbm6(d * 3.4 + vec3(11.0, 2.0, 5.0));
	float n3 = fbm4(d * 7.0 + vec3(5.0, 17.0, 1.0));
	float massa = smoothstep(0.44, 0.86, n1);
	vec3 neb = mix(cor_nebulosa_a, cor_nebulosa_b, smoothstep(0.35, 0.7, n2));
	neb = mix(neb, cor_nebulosa_c, smoothstep(0.6, 0.85, n3) * 0.55);
	float filamentos = smoothstep(0.4, 0.78, n3) * 0.65 + 0.35;
	col += neb * massa * filamentos * intensidade_nebulosa;

	// Faixa galáctica, com poeira escura cortando.
	vec3 eixo = normalize(vec3(0.25, 0.9, 0.35));
	float faixa = exp(-pow(dot(d, eixo) / 0.24, 2.0));
	col += vec3(0.05, 0.045, 0.062) * faixa * (0.4 + n2);
	col *= 1.0 - smoothstep(0.55, 0.8, n2) * faixa * 0.55;

	// Estrelas em três profundidades; a faixa galáctica é mais densa.
	vec3 est = camada_estrelas(d, 90.0, 0.10, 0.11)
	         + camada_estrelas(d, 220.0, 0.16, 0.13) * 0.75
	         + camada_estrelas(d, 480.0, 0.14 + faixa * 0.35, 0.15) * 0.5;
	col += est * intensidade_estrelas;

	// Estrela distante: disco + halo em camadas.
	vec3 s = normalize(direcao_sol);
	float cs = max(dot(d, s), 0.0);
	col += cor_sol * (smoothstep(0.99986, 0.99995, cs) * energia_sol
	                + pow(cs, 900.0) * 3.0
	                + pow(cs, 60.0) * 0.16
	                + pow(cs, 8.0) * 0.018);

	COLOR = col;
}
