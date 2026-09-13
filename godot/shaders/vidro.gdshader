// Painel de vidro fosco do HUD: desfoca o que está atrás (a cena 3D) e tinge.
// O formato arredondado vem do StyleBox do painel, pelo alfa de COLOR.
shader_type canvas_item;

uniform sampler2D tela : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform vec4 tinta : source_color = vec4(0.025, 0.04, 0.075, 0.66);
uniform float desfoque = 3.4;

void fragment() {
	vec3 fundo = textureLod(tela, SCREEN_UV, desfoque).rgb;
	vec3 c = mix(fundo, tinta.rgb, tinta.a);
	COLOR = vec4(c, COLOR.a);
}
