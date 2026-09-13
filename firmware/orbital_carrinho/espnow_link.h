// ============================================================================
// espnow_link.h — contrato de rádio entre o CONTROLE (que fala com o PC pela
// USB) e o CARRINHO que carrega a nave.
//
// NÃO EXISTE WIFI AQUI. Nenhuma das placas entra em rede nenhuma: sem SSID,
// sem senha, sem roteador. O WiFi.mode(WIFI_STA) dos sketches só LIGA o rádio.
//
// ATENÇÃO: o Arduino IDE não compartilha arquivos entre sketches. Este arquivo
// existe DUAS vezes, uma em cada pasta, e as duas cópias têm que ser IDÊNTICAS.
// Para o sintoma não ser "carrinho mudo sem explicação", todo pacote leva
// LINK_VERSAO e o outro lado reclama no serial quando a versão não bate.
//
// EMPARELHAMENTO (versão 3)
// ------------------------
// Antes tudo era broadcast: o controle gritava e torcia. Broadcast em ESP-NOW
// não tem confirmação de rádio — o esp_now_send responde "ok" mesmo quando não
// há ninguém ouvindo, e some a única evidência que importa.
//
// Agora o controle PROCURA o carrinho (PAREAR, em broadcast), o carrinho
// responde (PARCEIRO), e o controle guarda o MAC dele. Daí em diante a conversa
// é UNICAST: cada envio tem confirmação do rádio, o LED e o jogo mostram o
// estado real, e o carrinho do outro jogador nunca recebe o que não é dele.
// ============================================================================

#pragma once

#include <stdint.h>

// ------------------------------------------------------------------ canal ---
// ESP-NOW só conversa entre rádios no MESMO canal, e como ninguém se conecta a
// um roteador não existe quem negocie isso: é combinado na marra, aqui.
static const uint8_t LINK_WIFI_CHANNEL = 1;

// ----------------------------------------------------------------- versão ---
// Suba este número sempre que mexer nas structs abaixo.
static const uint8_t LINK_VERSAO = 3;

// Assinatura do projeto: em broadcast chega pacote de qualquer ESP na volta.
static const uint32_t LINK_MAGIC = 0x4F524231UL;  // "ORB1"

// ------------------------------------------------------------------ tipos ---
enum : uint8_t {
  LINK_TIPO_DIRIGIR  = 1,  // controle -> carrinho: duty do jogo + validade
  LINK_TIPO_PULSO    = 2,  // controle -> carrinho: anda por N ms (bancada)
  LINK_TIPO_PING     = 3,  // controle -> carrinho: você ainda está aí?
  LINK_TIPO_ACK      = 4,  // carrinho -> controle: ouvi você
  LINK_TIPO_PAREAR   = 5,  // controle -> qualquer carrinho: quem é o meu?
  LINK_TIPO_PARCEIRO = 6,  // carrinho -> controle: sou eu, este é o meu MAC
};

// Id do destinatário. 0 = qualquer carrinho (modo bancada, com um só na mesa).
static const uint8_t LINK_ID_QUALQUER = 0;
static const uint8_t LINK_ID_ION      = 1;
static const uint8_t LINK_ID_IGNIS    = 2;

// ---------------------------------------------------------------- pacotes ---
// packed: sem isso o compilador alinharia os campos e o sizeof mudaria entre
// builds, quebrando a checagem de tamanho do outro lado.
struct __attribute__((packed)) LinkCabecalho {
  uint32_t magic;   // sempre LINK_MAGIC
  uint8_t  versao;  // sempre LINK_VERSAO
  uint8_t  tipo;    // LINK_TIPO_*
  uint8_t  id;      // de quem é o pacote (ou para quem vai)
  uint32_t seq;     // contador de quem enviou; reenvio repete o valor
};

struct __attribute__((packed)) PacoteDirigir {
  LinkCabecalho cab;
  uint16_t duty;      // 0..1000, direto do PWM do jogo
  uint16_t validade;  // ms; sem pacote novo nesse prazo, o motor para
};

struct __attribute__((packed)) PacotePulso {
  LinkCabecalho cab;
  uint16_t ms;        // liga o motor por esse tempo e desliga
};

struct __attribute__((packed)) PacotePing {
  LinkCabecalho cab;
};

struct __attribute__((packed)) PacoteAck {
  LinkCabecalho cab;  // seq = seq do pacote confirmado
  uint8_t  andando;   // 1 = motor ligado neste instante
  uint16_t duty;      // duty em vigor no carrinho (0..1000)
};

// Chamada de emparelhamento. Vai em broadcast, porque ainda não se sabe o MAC.
struct __attribute__((packed)) PacoteParear {
  LinkCabecalho cab;
};

// Resposta: o carrinho se apresenta. O MAC vem do próprio frame (src_addr),
// não do payload — assim não há como mentir por engano.
struct __attribute__((packed)) PacoteParceiro {
  LinkCabecalho cab;
  uint8_t versaoFw;   // versão do firmware do carrinho, só informativo
};

// Travas de tamanho: mexer numa struct de um lado só estoura na compilação em
// vez de virar carrinho mudo na bancada.
static_assert(sizeof(LinkCabecalho)  == 11, "LinkCabecalho tem que ter 11 bytes");
static_assert(sizeof(PacoteDirigir)  == 15, "PacoteDirigir tem que ter 15 bytes");
static_assert(sizeof(PacotePulso)    == 13, "PacotePulso tem que ter 13 bytes");
static_assert(sizeof(PacotePing)     == 11, "PacotePing tem que ter 11 bytes");
static_assert(sizeof(PacoteAck)      == 14, "PacoteAck tem que ter 14 bytes");
static_assert(sizeof(PacoteParear)   == 11, "PacoteParear tem que ter 11 bytes");
static_assert(sizeof(PacoteParceiro) == 12, "PacoteParceiro tem que ter 12 bytes");

// ------------------------------------------------------------------ tempos ---
static const uint32_t LINK_PAREAR_MS   = 400;   // ritmo da chamada de emparelhamento
static const uint32_t LINK_PING_MS     = 500;   // sonda quando não há duty saindo
static const uint32_t LINK_TIMEOUT_MS  = 2000;  // sem ACK por isso = perdeu o carrinho
static const uint32_t LINK_ENVIO_MS    = 80;    // cadência do pacote DIRIGIR
static const uint16_t LINK_VALIDADE_MS = 400;   // prazo do duty no carrinho
static const uint32_t LINK_REENVIO_MS  = 25;    // intervalo entre reenvios do pulso
static const uint32_t LINK_PULSO_ACK_MS = 600;  // até quando insistir num pulso
static const uint16_t LINK_PULSO_MAX_MS = 1000; // teto de segurança do pulso

// ------------------------------------------------------------- long range ---
// Modo Long Range da Espressif: 250 kbps, com ganho grande de sensibilidade.
// Como ninguém fala com roteador, abrir mão da taxa não custa nada.
//
// MAS: ligar SÓ o LR (bitmap 0x08) desliga 11b/g/n, e aí o rádio não recebe
// mais quadro em taxa legada — que é o que o ESP-NOW usa em broadcast. Foi
// exatamente o que a bancada mostrou: ~96% de perda, passando só o que calhava
// de sair em LR. Com 11b/g/n + LR (0x0F) cada lado recebe qualquer taxa.
//
// Os bits são os do esp_wifi.h: 11B=1, 11G=2, 11N=4, LR=8. Fica aqui, e não
// no sketch, porque o valor TEM que ser igual nos dois lados.
static const uint8_t LINK_PROTOCOLO = 0x0F;

// ---------------------------------------------------------------- potência ---
// Potência máxima de transmissão, em quartos de dBm (78 = 19,5 dBm, o teto do
// C3). A antena de PCB da SuperMini é mal casada e perde muito; não há motivo
// para economizar aqui, e o padrão de fábrica nem sempre é o teto.
static const int8_t LINK_POTENCIA = 78;
