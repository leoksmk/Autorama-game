// ============================================================================
// espnow_link.h — contrato de rádio entre o CONTROLE (que fala com o PC pela
// USB) e o CARRINHO que carrega a nave.
//
// NÃO EXISTE WIFI AQUI. Nenhuma das placas entra em rede nenhuma: sem SSID,
// sem senha, sem roteador. O WiFi.mode(WIFI_STA) dos sketches só LIGA o rádio,
// e o WiFi.disconnect() logo depois garante que ele não se associe a nada. A
// única coisa que as duas precisam ter em comum é o CANAL.
//
// ATENÇÃO: o Arduino IDE não compartilha arquivos entre sketches. Este arquivo
// existe DUAS vezes, uma em cada pasta, e as duas cópias têm que ser IDÊNTICAS.
// Para não virar carrinho mudo sem explicação, todo pacote carrega LINK_VERSAO:
// se as cópias divergirem, o carrinho reclama no serial em vez de ficar quieto.
//
// O QUE MUDOU DA VERSÃO 1 (e por quê)
// -----------------------------------
//   1. Cabeçalho com TIPO explícito. Antes o tipo do pacote era deduzido do
//      tamanho (10/8/9 bytes). Era enxuto, mas agora existem mais pacotes e um
//      destinatário, e o truque não cabe mais.
//   2. Campo ID: 1 = carrinho do ÍON, 2 = carrinho do ÍGNIS. Em broadcast, sem
//      isso, o pacote de um controle mandaria no carrinho do outro jogador.
//   3. Pacote DIRIGIR: o jogo manda o PWM da pista (0..1000) com prazo de
//      validade. Vencido o prazo sem pacote novo, o carrinho para sozinho —
//      link caiu, jogo fechou, controle desplugado, tudo dá em motor parado.
// ============================================================================

#pragma once

#include <stdint.h>

// ------------------------------------------------------------------ canal ---
// ESP-NOW só conversa entre rádios no MESMO canal, e como ninguém se conecta a
// um roteador não existe quem negocie isso: é combinado na marra, aqui.
static const uint8_t LINK_WIFI_CHANNEL = 1;

// ----------------------------------------------------------------- versão ---
// Suba este número sempre que mexer nas structs abaixo.
static const uint8_t LINK_VERSAO = 2;

// Assinatura do projeto: em broadcast chega pacote de qualquer ESP na volta.
static const uint32_t LINK_MAGIC = 0x4F524231UL;  // "ORB1"

// ------------------------------------------------------------------ tipos ---
enum : uint8_t {
  LINK_TIPO_DIRIGIR = 1,  // controle -> carrinho: duty do jogo + validade
  LINK_TIPO_PULSO   = 2,  // controle -> carrinho: anda por N ms (bancada)
  LINK_TIPO_PING    = 3,  // controle -> carrinho: só pra saber se está vivo
  LINK_TIPO_ACK     = 4,  // carrinho -> controle: "ouvi você"
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

// Travas de tamanho: mexer numa struct de um lado só estoura na compilação em
// vez de virar carrinho mudo na bancada.
static_assert(sizeof(LinkCabecalho) == 11, "LinkCabecalho tem que ter 11 bytes");
static_assert(sizeof(PacoteDirigir) == 15, "PacoteDirigir tem que ter 15 bytes");
static_assert(sizeof(PacotePulso)   == 13, "PacotePulso tem que ter 13 bytes");
static_assert(sizeof(PacotePing)    == 11, "PacotePing tem que ter 11 bytes");
static_assert(sizeof(PacoteAck)     == 14, "PacoteAck tem que ter 14 bytes");

// ------------------------------------------------------------------ tempos ---
static const uint32_t LINK_PING_MS     = 500;   // sonda quando não há duty saindo
static const uint32_t LINK_TIMEOUT_MS  = 2000;  // sem ACK por isso = carrinho offline
static const uint32_t LINK_ENVIO_MS    = 80;    // cadência do pacote DIRIGIR
static const uint16_t LINK_VALIDADE_MS = 400;   // prazo do duty no carrinho
static const uint32_t LINK_REENVIO_MS  = 25;    // intervalo entre reenvios do pulso
static const uint32_t LINK_PULSO_ACK_MS = 600;  // até quando insistir num pulso
static const uint16_t LINK_PULSO_MAX_MS = 1000; // teto de segurança do pulso

// ------------------------------------------------------------- long range ---
// Modo Long Range da Espressif: 250 kbps em vez de 1 Mbps+, com ganho grande de
// sensibilidade (na prática 2x ou mais de alcance útil). Como ninguém fala com
// roteador, abrir mão da taxa não custa nada: o pacote tem 15 bytes.
//
// Foi ligado porque a bancada mostrou ~97% de perda nas duas direções no modo
// normal: a antena de PCB da ESP32-C3 SuperMini é reconhecidamente mal casada.
//
// TEM que estar igual nos dois lados: em LR-only, uma placa em modo normal e a
// outra em LR simplesmente não se enxergam.
static const bool LINK_LONG_RANGE = true;
