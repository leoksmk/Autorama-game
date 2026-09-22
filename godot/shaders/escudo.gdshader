// Bolha do Escudo: borda de Fresnel e casca hexagonal. `impacto` acende tudo
// no instante em que um ataque é bloqueado; `aviso` avisa que o escudo cai no
// próximo checkpoint — a trama pisca depressa e a cor puxa para o âmbar.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 cor : source_color = vec3(0.35, 0.95, 0.9);
uniform float forca = 1.0;
uniform float impacto = 0.0;
uniform float aviso = 0.0;      // 1 = último trecho antes de cair

// Distância à borda de uma grade hexagonal (0,5 na aresta).
float borda_hex(vec2 p) {
	const vec2 s = vec2(1.0, 1.7320508);
	vec4 hc = floor(vec4(p, p - vec2(0.5, 1.0)) / s.xyxy) + 0.5;
	vec4 h = vec4(p - hc.xy * s, p - (hc.zw + 0.5) * s);
	vec2 q = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
	q = abs(q);
	float d = max(dot(q, s * 0.5), q.x);
	return smoothstep(0.40, 0.5, d);
}

void fragment() {
	float fres = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.4);
	float hexa = borda_hex(UV * vec2(26.0, 13.0));
	// No último trecho a trama passa de respiração (3 Hz) a piscada (13 Hz):
	// é a mesma leitura de uma luz de reserva no painel.
	float pulso = 0.5 + 0.5 * sin(TIME * mix(3.0, 13.0, aviso) + UV.y * 22.0);
	// No bloqueio acendem a borda e a trama hexagonal; o miolo quase não muda,
	// senão a bolha virava um borrão branco que escondia a nave.
	float a = (fres * 0.85 + hexa * 0.3 * (0.35 + 0.65 * pulso)) * forca
	        + impacto * (0.1 + fres * 0.8 + hexa * 0.55);
	vec3 ambar = vec3(1.0, 0.62, 0.18);
	ALBEDO = mix(cor, ambar, aviso * (0.35 + 0.35 * pulso)) * (1.2 + impacto * 0.9);
	ALPHA = clamp(a, 0.0, 1.0);
}
