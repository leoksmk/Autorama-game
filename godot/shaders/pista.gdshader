// Leito da pista: painéis metálicos com juntas, linhas de faixa, luzes de
// borda que "correm" no sentido da prova e o xadrez da largada.
//
// UV.x = 0..1 atravessando a pista; UV.y = metros ao longo da volta.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/ruido.gdshaderinc"

uniform vec3 cor_base : source_color = vec3(0.07, 0.078, 0.092);
uniform vec3 cor_painel_b : source_color = vec3(0.115, 0.12, 0.135);
uniform vec3 cor_linha : source_color = vec3(0.75, 0.9, 1.0);
uniform vec3 cor_luz_borda : source_color = vec3(0.3, 0.72, 1.0);
uniform float comprimento = 260.0;
uniform float largura = 8.0;

void fragment() {
	float u = UV.x;
	float v = UV.y;
	float x = (u - 0.5) * largura;

	// Painéis de 4 m x 2 m, cada um com tom, rugosidade e metal próprios.
	vec2 painel = vec2(floor(v / 4.0), floor(x / 2.0));
	float variacao = hash21(painel);
	vec3 alb = mix(cor_base, cor_painel_b, variacao);
	float rug = mix(0.42, 0.66, hash21(painel + 7.0));
	float met = mix(0.3, 0.55, hash21(painel + 3.0));

	// Riscos finos de uso ao longo da pista.
	float riscos = ruido(vec3(x * 40.0, v * 0.6, 0.0));
	rug += (riscos - 0.5) * 0.08;

	// Juntas entre painéis.
	float fv = fract(v / 4.0);
	float fx = fract(x / 2.0);
	float dv = min(fv, 1.0 - fv) * 4.0;
	float dx = min(fx, 1.0 - fx) * 2.0;
	float junta = 1.0 - smoothstep(0.015, 0.045, min(dv, dx));
	alb *= 1.0 - junta * 0.7;
	rug = mix(rug, 0.85, junta);

	// Linhas: bordas contínuas e divisória central tracejada.
	float borda = 1.0 - smoothstep(0.06, 0.1, abs(abs(x) - (largura * 0.5 - 0.4)));
	float centro = (1.0 - smoothstep(0.05, 0.09, abs(x))) * step(0.45, fract(v / 3.0));
	float linhas = max(borda, centro);
	alb = mix(alb, cor_linha * 0.75, linhas);
	rug = mix(rug, 0.5, linhas);
	vec3 emis = cor_linha * linhas * 0.25;

	// Luzes de borda a cada 2 m, com uma onda correndo no sentido da prova.
	float fp = fract(v / 2.0);
	float ponto = 1.0 - smoothstep(0.05, 0.09,
		length(vec2((fp - 0.5) * 2.0, abs(x) - (largura * 0.5 - 0.14))));
	float onda = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(v * 0.35 - TIME * 7.0), 8.0);
	emis += cor_luz_borda * ponto * onda * 6.0;

	// Xadrez da linha de largada / chegada.
	float ate_largada = min(v, comprimento - v);
	if (ate_largada < 1.25) {
		float xadrez = mod(floor(v / 0.62) + floor(x / 0.62), 2.0);
		alb = mix(vec3(0.02), vec3(0.82), xadrez);
		emis += vec3(0.5) * xadrez;
		rug = 0.35;
	}

	ALBEDO = alb;
	ROUGHNESS = clamp(rug, 0.05, 1.0);
	METALLIC = met;
	SPECULAR = 0.35;
	EMISSION = emis;
}
