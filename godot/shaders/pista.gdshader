// Leito da pista: painéis metálicos com juntas, as duas faixas pintadas na cor
// de quem corre nelas, desgaste em cima da trajetória, zebras nas curvas, luzes
// de borda correndo no sentido da prova e o xadrez da largada.
//
// UV.x  = 0 na borda de DENTRO do circuito, 1 na de fora. A faixa 0 (ÍON) é
//         sempre a metade de dentro; a 1 (ÍGNIS), a de fora. Quem garante isso
//         é PistaVisual.ConstruirLeito, não este shader.
// UV.y  = metros percorridos desde a largada.
// UV2.x = inclinação lateral do leito, 0 na reta e 1 na curva mais fechada.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/ruido.gdshaderinc"

uniform vec3 cor_base : source_color = vec3(0.07, 0.078, 0.092);
uniform vec3 cor_painel_b : source_color = vec3(0.115, 0.12, 0.135);
uniform vec3 cor_linha : source_color = vec3(0.75, 0.9, 1.0);
uniform vec3 cor_pista1 : source_color = vec3(0.18, 0.84, 1.0);
uniform vec3 cor_pista2 : source_color = vec3(1.0, 0.63, 0.18);
uniform vec3 cor_zebra : source_color = vec3(0.58, 0.09, 0.08);
uniform float comprimento = 388.0;
uniform float largura = 11.0;
uniform float faixa_u = 0.236;     // meia distância entre os eixos das faixas, em UV.x

// Listra de meio período: 1 numa faixa, 0 na seguinte.
float listra(float v, float passo) {
	return step(0.5, fract(v / passo));
}

void fragment() {
	float u = UV.x;
	float v = UV.y;
	float x = (u - 0.5) * largura;          // metros a partir do eixo do leito
	float banco = clamp(UV2.x, 0.0, 1.0);   // 0 na reta, 1 na curva mais fechada

	// Qual metade do leito: a de dentro é do ÍON, a de fora é do ÍGNIS.
	vec3 cor_faixa = mix(cor_pista1, cor_pista2, step(0.5, u));

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

	// Desgaste: o rastro que as naves deixam em cima do eixo de cada faixa. É o
	// detalhe que mais faz o leito parecer usado — mais escuro, mais liso, e com
	// a borda irregular, senão vira uma fita pintada.
	float d_faixa = min(abs(u - (0.5 - faixa_u)), abs(u - (0.5 + faixa_u))) * largura;
	float ruga = (ruido(vec3(v * 0.35, 0.0, 0.0)) - 0.5) * 0.5;
	float rastro = 1.0 - smoothstep(0.55, 1.5, d_faixa + ruga);
	alb *= 1.0 - rastro * 0.38;
	rug = mix(rug, 0.24, rastro * 0.8);
	met = mix(met, 0.7, rastro * 0.5);

	// Linhas: bordas contínuas e divisória central tracejada. A divisória é
	// bicolor, cada metade puxando para a cor da faixa que ela separa.
	float borda = 1.0 - smoothstep(0.06, 0.1, abs(abs(x) - (largura * 0.5 - 0.4)));
	float centro = (1.0 - smoothstep(0.05, 0.09, abs(x))) * listra(v, 3.0);
	vec3 tinta_linha = mix(cor_linha, cor_faixa, 0.35);
	float linhas = max(borda, centro);
	alb = mix(alb, tinta_linha * 0.75, linhas);
	rug = mix(rug, 0.5, linhas);
	vec3 emis = tinta_linha * linhas * 0.25;

	// ZEBRAS. Duas medidas aqui foram tiradas de tela, não de gosto:
	//
	// O limiar de inclinação é alto (0,45) porque este circuito é curva em três
	// quartos da volta. Com o limiar baixo a zebra saía do S, entrava no curvão,
	// seguia pela subida e virava a moldura da pista inteira — deixando de
	// marcar coisa alguma. Em 0,45 ela fica só no S, na saída do curvão e no
	// alto da subida: as três curvas onde há o que marcar.
	//
	// A faixa é estreita (últimos 8% de cada lado, ~0,44 m) porque a zebra é
	// acostamento, não pintura de leito. Larga, ela roubava pista útil na tela.
	float nas_curvas = smoothstep(0.45, 0.70, banco);
	float na_borda = smoothstep(0.84, 0.92, abs(x) / (largura * 0.5));
	float zebra = nas_curvas * na_borda;
	if (zebra > 0.001) {
		float faixa_z = listra(v, 1.4);
		vec3 tom = mix(vec3(0.70, 0.68, 0.64), cor_zebra, faixa_z);
		// Sulco entre listras: o olho lê como degrau, não como pintura.
		float sulco = 1.0 - smoothstep(0.02, 0.09, min(fract(v / 1.4), 1.0 - fract(v / 1.4)));
		tom *= 1.0 - sulco * 0.65;
		alb = mix(alb, tom, zebra);
		rug = mix(rug, 0.72, zebra);
		met = mix(met, 0.03, zebra);
	}

	// Luzes de borda a cada 2 m, na cor da faixa, com uma onda correndo no
	// sentido da prova. Nas curvas elas ficam mais fortes: é o que marca o
	// traçado de longe, quando a câmera abre.
	float fp = fract(v / 2.0);
	float ponto = 1.0 - smoothstep(0.05, 0.09,
		length(vec2((fp - 0.5) * 2.0, abs(x) - (largura * 0.5 - 0.14))));
	float onda = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(v * 0.35 - TIME * 7.0), 8.0);
	emis += cor_faixa * ponto * onda * (5.0 + 4.0 * banco);

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
