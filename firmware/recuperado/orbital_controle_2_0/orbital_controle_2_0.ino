/*
  ORBITAL DERBY — controle de nave + ponte de rádio para o carrinho
  ==================================================================

  Uma placa por nave (ESP32-C3 SuperMini), com dois papéis ao mesmo tempo:

    1. CONTROLE   dois switches — ACELERADOR (martelar = andar) e AÇÃO (pegar e
                  usar o poder) — que viram eventos na USB para o jogo.
    2. PONTE      o jogo devolve o PWM daquela pista pela USB, e esta placa
                  repassa por rádio (ESP-NOW) para o carrinho que carrega a
                  nave impressa.

              switches            USB                    ESP-NOW
      dedo  ----------->  CONTROLE  <--------->  JOGO  ...  CONTROLE  ---->  CARRINHO
                             (esta placa)                    (esta placa)

  O caminho de volta é o que importa: o jogo é quem decide a velocidade, e o
  único atuador é o PWM. Nada nesta placa inventa velocidade por conta própria
  — fora o modo bancada, abaixo.

  LIGAÇÃO
  -------
  Cada switch entre o pino e o GND. Nada de resistor: o pull-up interno segura
  o pino em nível alto, e apertar leva a zero.

                        ESP32 clássico     ESP32-C3 SuperMini   ESP32-S2 / S3
      Acelerador        GPIO 25            GPIO 4 (pino "4")    GPIO 4
      Ação              GPIO 26            GPIO 5 (pino "5")    GPIO 5
      O outro lado      GND                GND                  GND

  Na C3, evite 2, 8 e 9 (pinos de boot) e 20/21 (UART).

  ESP32-C3 SUPERMINI
  ------------------
  A SuperMini não tem conversor USB-serial: a porta COM é a USB do próprio C3.
  Compile com a placa "Nologo ESP32C3 Super Mini", que já liga o USB CDC:

      arduino-cli compile --fqbn esp32:esp32:nologo_esp32c3_super_mini firmware/orbital_controle

  Com a genérica "ESP32C3 Dev Module" o CDC vem desligado: o Serial sairia
  pelos pinos 20/21 e a porta COM ficaria muda. O firmware se recusa a compilar
  assim (veja o #error).

  Se a gravação não conectar: segure BOOT, aperte e solte RESET, solte BOOT e
  grave de novo (depois, RESET para rodar).

  IDENTIDADE (ÍON ou ÍGNIS)
  -------------------------
  Cada placa guarda o próprio id na memória interna: 1 = ÍON, 2 = ÍGNIS. Sai de
  fábrica como 1. Para transformar um controle em ÍGNIS, uma vez só:

      python tools/controle_esp.py --definir-id COM5 2

  O mesmo id vai em todo pacote de rádio: o carrinho do ÍGNIS ignora o que o
  controle do ÍON manda. Sem isso, em broadcast, um jogador moveria o carrinho
  do outro. O carrinho tem o id dele gravado do mesmo jeito.

  MODO BANCADA (sem PC)
  ---------------------
  Quando o jogo não está mandando PWM (nenhum "M" há mais de PC_MUDO_MS), o
  acelerador volta a ser o botão de sempre: cada aperto manda um pulso de
  PULSO_BANCADA_MS para o carrinho andar um tanto. É como testar a pista sem
  abrir o jogo. Assim que o jogo começa a mandar PWM, o pulso sai de cena.

  PROTOCOLO COM O PC (serial 115200, linhas de texto)
  ---------------------------------------------------
      ESP -> PC   HELLO ORBITAL <id> <versao>   ao ligar e em resposta a "?"
                  T1 / T0                       acelerador apertou / soltou
                  A1 / A0                       ação apertou / soltou
                  K <t><a>                      pulsação a cada 500 ms, ex. "K 10"
                  L1 / L0                       carrinho entrou / saiu do ar
      PC -> ESP   ?                             pede o HELLO
                  ID <n>                        grava o id (1 ou 2)
                  M <duty>                      PWM da pista, 0..1000
                  D <0|1>                       liga/desliga o diagnóstico

  O diagnóstico sai desligado: ele é útil na bancada e só atrapalha quando o
  jogo está lendo a porta. Ligue com "D 1" no monitor serial.

  DEBOUNCE
  --------
  Fica AQUI, e é o motivo de este firmware existir em vez de um simples "manda
  o estado do pino". O acelerador é de martelar: cada aperto conta. Um switch
  que repica gera cliques fantasmas e a nave acelera sozinha. O estado só muda
  depois de ESTAVEL_MS parado — rápido para 25 apertos por segundo, lento o
  bastante para engolir o repique.
*/

#include <Arduino.h>
#include <Preferences.h>
#include <WiFi.h>
#include <esp_now.h>
#include <esp_wifi.h>
#include "espnow_link.h"

// ---------------------------------------------------------------------------
// Pinos por modelo
// ---------------------------------------------------------------------------
#if defined(CONFIG_IDF_TARGET_ESP32C3) || defined(CONFIG_IDF_TARGET_ESP32C6)
  #define PINO_ACELERADOR 4
  #define PINO_ACAO       5
#elif defined(CONFIG_IDF_TARGET_ESP32S2) || defined(CONFIG_IDF_TARGET_ESP32S3)
  #define PINO_ACELERADOR 4
  #define PINO_ACAO       5
#else  // ESP32 clássico (DevKit V1, WROOM-32)
  #define PINO_ACELERADOR 25
  #define PINO_ACAO       26
#endif

// ESP32-C3 sem USB CDC On Boot: o Serial iria para os pinos 20/21 e a porta COM
// da USB ficaria muda. Placa C3 com conversor CH340/CP210x na UART (não é o
// caso da SuperMini)? Compile com -DORBITAL_SERIAL_UART para liberar.
#if defined(CONFIG_IDF_TARGET_ESP32C3) && !ARDUINO_USB_CDC_ON_BOOT && !defined(ORBITAL_SERIAL_UART)
  #error "ESP32-C3 com USB CDC On Boot desligado: a porta COM ficaria muda. Arduino IDE: Ferramentas > Placa > esp32 > Nologo ESP32C3 Super Mini (ou Ferramentas > USB CDC On Boot > Enabled). arduino-cli: --fqbn esp32:esp32:nologo_esp32c3_super_mini"
#endif

// LED da placa: mostra o estado do CARRINHO, que é o que ninguém consegue ver
// de longe. Pisca devagar = procurando; aceso fixo = carrinho respondendo;
// apaga um instante = acabou de mandar comando.
#if defined(CONFIG_IDF_TARGET_ESP32C3)
  // SuperMini: LED azul no GPIO 8, ligado ao 3V3 — acende em nível BAIXO. O
  // LED_BUILTIN do C3 genérico não serve: lá ele é um LED RGB endereçável.
  #define PINO_LED  8
  #define LED_ACESO LOW
#elif defined(LED_BUILTIN)
  #define PINO_LED  LED_BUILTIN
  #define LED_ACESO HIGH
#endif

// ---------------------------------------------------------------------------
// Parâmetros
// ---------------------------------------------------------------------------
static const char*    VERSAO           = "2.0";
static const uint32_t BAUD             = 115200;
static const uint8_t  ID_PADRAO        = 1;
static const uint32_t ESTAVEL_MS       = 5;     // debounce
static const uint32_t PULSACAO_MS      = 500;   // sinal de vida para o PC
static const uint32_t PC_MUDO_MS       = 1500;  // sem "M" por isso: o PC saiu
static const uint16_t PULSO_BANCADA_MS = 150;   // aperto sem PC = anda isso
static const uint32_t LED_PISCA_MS     = 80;
static const uint32_t LED_BUSCA_MS     = 400;
static const uint32_t DIAG_PERIODO_MS  = 3000;  // heartbeat do diagnóstico

struct Botao {
  uint8_t  pino;
  char     letra;      // 'T' (acelerador) ou 'A' (ação)
  bool     estavel;    // estado já sem repique; true = apertado
  bool     bruto;      // última leitura do pino
  uint32_t desde;      // quando o bruto mudou pela última vez
};

static Botao botoes[2] = {
  { PINO_ACELERADOR, 'T', false, false, 0 },
  { PINO_ACAO,       'A', false, false, 0 },
};

static Preferences prefs;
static uint8_t     id = ID_PADRAO;
static uint32_t    ultimaPulsacao = 0;
static char        linha[32];
static uint8_t     tamLinha = 0;
static bool        diagnostico = false;

// --- o que o jogo mandou ---------------------------------------------------
static uint16_t dutyAtual   = 0;     // 0..1000
static uint32_t ultimoM     = 0;     // quando chegou o último "M"
static bool     pcAtivo     = false;
static uint32_t proximoEnvio = 0;

// --- rádio -----------------------------------------------------------------
static uint8_t  macBroadcast[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
static uint32_t seqDirigir = 0;
static uint32_t seqPulso   = 0;
static uint32_t seqPing    = 0;
static uint32_t proximoPing = 0;

// Pulso de bancada esperando confirmação. 0 = nenhum pendente.
static PacotePulso pulsoEmVoo;
static uint32_t pulsoPendenteSeq = 0;
static uint32_t pulsoPrazo       = 0;
static uint32_t proximoReenvio   = 0;

// --- anotado pelo callback (task do WiFi), lido pelo loop -------------------
// volatile + seção crítica: sem isso o loop pode ler valor pela metade. O
// callback NUNCA chama Serial nem toca no LED. Só anota.
static volatile uint32_t ultimoAckMs   = 0;
static volatile bool     jaTeveAck     = false;
static volatile uint32_t ackPulsoSeq   = 0;
static volatile bool     jaTevePulsoAck = false;
static volatile uint8_t  ackAndando    = 0;
static volatile uint16_t ackDuty       = 0;
static volatile uint32_t ackVersaoRuim = 0;   // pacote de header divergente

static portMUX_TYPE trava = portMUX_INITIALIZER_UNLOCKED;

static bool     ligadoMostrado = false;   // último estado de link já avisado
static bool     piscando       = false;
static uint32_t piscaAte       = 0;
static uint32_t proximoDiag    = 0;

// ---------------------------------------------------------------------------

static void ledAcender(bool aceso) {
#ifdef PINO_LED
  digitalWrite(PINO_LED, aceso ? LED_ACESO : !LED_ACESO);
#else
  (void)aceso;
#endif
}

static void ola() {
  Serial.printf("HELLO ORBITAL %u %s\n", id, VERSAO);
}

// Por que a placa reiniciou. BROWNOUT aqui significa alimentação, e nenhuma
// mexida no código resolve.
static const char* motivoReset() {
  switch (esp_reset_reason()) {
    case ESP_RST_POWERON:  return "power-on (normal)";
    case ESP_RST_EXT:      return "botao de reset";
    case ESP_RST_SW:       return "reset por software";
    case ESP_RST_PANIC:    return "PANIC (crash no codigo)";
    case ESP_RST_INT_WDT:  return "watchdog de interrupcao";
    case ESP_RST_TASK_WDT: return "watchdog de task";
    case ESP_RST_WDT:      return "watchdog";
    case ESP_RST_BROWNOUT: return "BROWNOUT -- a tensao caiu!";
    default:               return "outro";
  }
}

static void montarCabecalho(LinkCabecalho& cab, uint8_t tipo, uint32_t seq) {
  cab.magic  = LINK_MAGIC;
  cab.versao = LINK_VERSAO;
  cab.tipo   = tipo;
  cab.id     = id;
  cab.seq    = seq;
}

static void enviarRadio(const void* pacote, size_t tamanho) {
  esp_err_t err = esp_now_send(macBroadcast, (const uint8_t*)pacote, tamanho);
  if (err != ESP_OK && diagnostico) {
    Serial.printf("[ctrl] falha no envio: %s\n", esp_err_to_name(err));
  }
}

/// Manda o PWM do jogo para o carrinho. Vai com prazo: se parar de chegar, o
/// carrinho desliga o motor sozinho.
static void enviarDirigir(uint32_t agora) {
  PacoteDirigir p;
  montarCabecalho(p.cab, LINK_TIPO_DIRIGIR, ++seqDirigir);
  p.duty     = dutyAtual;
  p.validade = LINK_VALIDADE_MS;
  enviarRadio(&p, sizeof(p));
  proximoEnvio = agora + LINK_ENVIO_MS;
}

/// Modo bancada: um aperto = um tanto de movimento. Reenviado até o carrinho
/// confirmar ESTE seq, porque um aperto perdido é um aperto que o dedo deu e a
/// pista não mostrou.
static void dispararPulso(uint32_t agora) {
  montarCabecalho(pulsoEmVoo.cab, LINK_TIPO_PULSO, ++seqPulso);
  pulsoEmVoo.ms = PULSO_BANCADA_MS;
  enviarRadio(&pulsoEmVoo, sizeof(pulsoEmVoo));

  pulsoPendenteSeq = seqPulso;
  pulsoPrazo       = agora + LINK_PULSO_ACK_MS;
  proximoReenvio   = agora + LINK_REENVIO_MS;
  piscando         = true;
  piscaAte         = agora + LED_PISCA_MS;
}

// -------------------------------------------------------------- recepção ---
// Resposta do carrinho. Roda em task do WiFi: só anota, nada de Serial aqui.
#if ESP_ARDUINO_VERSION_MAJOR >= 3
static void aoReceber(const esp_now_recv_info_t* info, const uint8_t* dados, int len)
#else
static void aoReceber(const uint8_t* mac, const uint8_t* dados, int len)
#endif
{
  if (len < (int)sizeof(LinkCabecalho)) return;

  LinkCabecalho cab;
  memcpy(&cab, dados, sizeof(cab));
  if (cab.magic != LINK_MAGIC) return;          // tráfego de outro projeto

  if (cab.versao != LINK_VERSAO) {              // espnow_link.h divergente
    portENTER_CRITICAL(&trava);
    ackVersaoRuim = cab.versao;
    portEXIT_CRITICAL(&trava);
    return;
  }
  if (cab.tipo != LINK_TIPO_ACK) return;        // eco do nosso próprio envio
  if (cab.id != id) return;                     // ack do carrinho do outro
  if (len != (int)sizeof(PacoteAck)) return;

  PacoteAck ack;
  memcpy(&ack, dados, sizeof(ack));

  portENTER_CRITICAL(&trava);
  ultimoAckMs = millis();
  jaTeveAck   = true;
  ackAndando  = ack.andando;
  ackDuty     = ack.duty;
  if (ack.andando) {
    // Confirmação que serve para o pulso pendente: o carrinho reagiu.
    ackPulsoSeq    = ack.cab.seq;
    jaTevePulsoAck = true;
  }
  portEXIT_CRITICAL(&trava);
}

// ---------------------------------------------------------------------------

static void tratarComando(const char* cmd) {
  if (strcmp(cmd, "?") == 0) {
    ola();
    return;
  }
  if (strncmp(cmd, "ID ", 3) == 0) {
    int n = atoi(cmd + 3);
    if (n == 1 || n == 2) {
      id = (uint8_t)n;
      prefs.putUChar("id", id);
    }
    ola();
    return;
  }
  if (cmd[0] == 'M' && (cmd[1] == ' ' || cmd[1] == '\0')) {
    // PWM da pista, 0..1000. É o jogo dirigindo o carrinho.
    int n = (cmd[1] == '\0') ? 0 : atoi(cmd + 2);
    if (n < 0) n = 0;
    if (n > 1000) n = 1000;
    dutyAtual = (uint16_t)n;
    ultimoM   = millis();
    enviarDirigir(ultimoM);        // sem esperar a cadência: acelerador é agora
    return;
  }
  if (cmd[0] == 'D' && cmd[1] == ' ') {
    diagnostico = atoi(cmd + 2) != 0;
    Serial.printf("[ctrl] diagnostico %s\n", diagnostico ? "ligado" : "desligado");
    return;
  }
  // "C <n>" (sensor de checkpoint) e demais comandos: reservados.
}

static void lerSerial() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\n' || c == '\r') {
      if (tamLinha > 0) {
        linha[tamLinha] = '\0';
        tratarComando(linha);
        tamLinha = 0;
      }
    } else if (tamLinha < sizeof(linha) - 1) {
      linha[tamLinha++] = c;
    } else {
      tamLinha = 0;  // linha longa demais: é lixo, descarta
    }
  }
}

static void lerBotoes(uint32_t agora) {
  for (Botao& b : botoes) {
    bool apertado = digitalRead(b.pino) == LOW;
    if (apertado != b.bruto) {
      // O pino mexeu: recomeça a contar. Repique fica preso aqui.
      b.bruto = apertado;
      b.desde = agora;
    } else if (apertado != b.estavel && agora - b.desde >= ESTAVEL_MS) {
      b.estavel = apertado;
      Serial.printf("%c%c\n", b.letra, apertado ? '1' : '0');

      // Sem PC, o acelerador volta a ser o botão que anda com o carrinho.
      if (apertado && !pcAtivo && b.letra == 'T') {
        dispararPulso(agora);
      }
    }
  }
}

// ------------------------------------------------------------------- setup ---
void setup() {
  Serial.begin(BAUD);

  for (Botao& b : botoes) {
    pinMode(b.pino, INPUT_PULLUP);
    b.bruto = b.estavel = (digitalRead(b.pino) == LOW);
    b.desde = millis();
  }

#ifdef PINO_LED
  pinMode(PINO_LED, OUTPUT);
#endif
  ledAcender(false);

  prefs.begin("orbital", false);
  id = prefs.getUChar("id", ID_PADRAO);
  if (id != 1 && id != 2) {
    id = ID_PADRAO;
  }

  // ESP-NOW roda sobre a interface STA, mas SEM conectar em rede nenhuma.
  WiFi.mode(WIFI_STA);

  // Credenciais que qualquer sketch anterior tenha salvo na NVS fazem o core
  // tentar reconectar sozinho — e reconectar VARRE CANAIS, arrastando o rádio
  // para longe do canal combinado. Guardar só na RAM e apagar o que está salvo
  // mata isso de vez.
  esp_wifi_set_storage(WIFI_STORAGE_RAM);
  WiFi.disconnect(false, true);

  // ESP-NOW + modem sleep = rádio surdo: sem estar associado a um AP, o C3
  // desliga o rádio em pedaços para economizar, e o frame chega bem na hora em
  // que ele está dormindo.
  esp_wifi_set_ps(WIFI_PS_NONE);

  if (LINK_LONG_RANGE) {
    esp_wifi_set_protocol(WIFI_IF_STA, WIFI_PROTOCOL_LR);
  }
  esp_wifi_set_channel(LINK_WIFI_CHANNEL, WIFI_SECOND_CHAN_NONE);

  if (esp_now_init() != ESP_OK) {
    Serial.println("[ctrl] ESP-NOW nao inicializou -- reiniciando");
    delay(1000);
    ESP.restart();
  }
  if (esp_now_register_recv_cb(aoReceber) != ESP_OK) {
    Serial.println("[ctrl] falha ao registrar callback -- reiniciando");
    delay(1000);
    ESP.restart();
  }

  // Mesmo em broadcast o ESP-NOW exige o "peer" registrado. channel = 0
  // significa "use o canal atual da interface".
  esp_now_peer_info_t peer = {};
  memcpy(peer.peer_addr, macBroadcast, 6);
  peer.channel = 0;
  peer.ifidx   = WIFI_IF_STA;
  peer.encrypt = false;
  if (esp_now_add_peer(&peer) != ESP_OK) {
    Serial.println("[ctrl] falha ao registrar peer broadcast -- reiniciando");
    delay(1000);
    ESP.restart();
  }

  uint32_t agora = millis();
  proximoPing = agora + LINK_PING_MS;
  proximoDiag = agora + DIAG_PERIODO_MS;

  delay(50);
  ola();
}

// -------------------------------------------------------------------- loop ---
void loop() {
  const uint32_t agora = millis();

  lerBotoes(agora);
  lerSerial();

  // --- o jogo ainda está mandando PWM? --------------------------------------
  // Comparação com sinal: sobrevive ao estouro do millis() (~49 dias).
  bool ativo = (ultimoM != 0) && ((int32_t)(agora - ultimoM) < (int32_t)PC_MUDO_MS);
  if (!ativo && pcAtivo) {
    // O jogo fechou ou o cabo saiu: derruba o duty. O carrinho também para
    // sozinho pelo prazo de validade, mas não custa mandar o zero.
    dutyAtual = 0;
    enviarDirigir(agora);
  }
  pcAtivo = ativo;

  // --- cadência do duty ------------------------------------------------------
  if (pcAtivo && (int32_t)(agora - proximoEnvio) >= 0) {
    enviarDirigir(agora);
  }

  // --- snapshot do que o callback anotou ------------------------------------
  uint32_t sAckMs, sPulsoSeq, sVersaoRuim;
  bool     sJaTeveAck, sJaTevePulsoAck;
  uint16_t sDuty;
  uint8_t  sAndando;

  portENTER_CRITICAL(&trava);
  sAckMs          = ultimoAckMs;
  sJaTeveAck      = jaTeveAck;
  sPulsoSeq       = ackPulsoSeq;
  sJaTevePulsoAck = jaTevePulsoAck;
  sDuty           = ackDuty;
  sAndando        = ackAndando;
  sVersaoRuim     = ackVersaoRuim;
  portEXIT_CRITICAL(&trava);

  // Comparação COM SINAL, não unsigned: o callback roda em outra task e pode
  // gravar ultimoAckMs depois de termos lido "agora". Em unsigned viraria
  // underflow gigante e o link piscaria "offline" justamente quando o ack
  // acabou de chegar.
  const int32_t idadeAck = (int32_t)(agora - sAckMs);
  const bool ligado = sJaTeveAck && (idadeAck < (int32_t)LINK_TIMEOUT_MS);

  // --- pulso de bancada: insiste até o carrinho confirmar --------------------
  if (pulsoPendenteSeq != 0) {
    if (sJaTevePulsoAck && sPulsoSeq == pulsoPendenteSeq) {
      pulsoPendenteSeq = 0;
    } else if ((int32_t)(agora - pulsoPrazo) >= 0) {
      if (diagnostico) {
        Serial.printf("[ctrl] pulso %lu perdido\n", (unsigned long)pulsoPendenteSeq);
      }
      pulsoPendenteSeq = 0;
    } else if ((int32_t)(agora - proximoReenvio) >= 0) {
      enviarRadio(&pulsoEmVoo, sizeof(pulsoEmVoo));
      proximoReenvio = agora + LINK_REENVIO_MS;
    }
  }

  // --- ping: só quando não há duty saindo ------------------------------------
  // Com o jogo rodando, o próprio pacote de duty já faz o papel de sonda.
  if (!pcAtivo && (int32_t)(agora - proximoPing) >= 0) {
    proximoPing = agora + LINK_PING_MS;
    PacotePing ping;
    montarCabecalho(ping.cab, LINK_TIPO_PING, ++seqPing);
    enviarRadio(&ping, sizeof(ping));
  }

  // --- avisa o PC quando o carrinho entra ou sai do ar -----------------------
  if (ligado != ligadoMostrado) {
    ligadoMostrado = ligado;
    Serial.printf("L%c\n", ligado ? '1' : '0');
  }

  // --- LED -------------------------------------------------------------------
  if (piscando && (int32_t)(agora - piscaAte) < 0) {
    ledAcender(false);                                   // buraco = mandou comando
  } else {
    piscando = false;
    if (ligado) {
      ledAcender(true);                                  // fixo = carrinho na escuta
    } else {
      ledAcender(((agora / LED_BUSCA_MS) % 2) == 0);     // pisca = procurando
    }
  }

  // --- sinal de vida para o PC ----------------------------------------------
  if (agora - ultimaPulsacao >= PULSACAO_MS) {
    ultimaPulsacao = agora;
    Serial.printf("K %c%c\n", botoes[0].estavel ? '1' : '0', botoes[1].estavel ? '1' : '0');
  }

  // --- diagnóstico (desligado por padrão) -----------------------------------
  if (diagnostico && (int32_t)(agora - proximoDiag) >= 0) {
    proximoDiag = agora + DIAG_PERIODO_MS;
    if (sVersaoRuim != 0) {
      Serial.printf("[ctrl] ATENCAO: carrinho com espnow_link.h versao %lu (aqui e %u)\n",
                    (unsigned long)sVersaoRuim, LINK_VERSAO);
    }
    Serial.printf("[ctrl] id=%u pc=%s duty=%u | carrinho=%s ack ha %ld ms duty=%u andando=%u\n",
                  id, pcAtivo ? "ativo" : "mudo", dutyAtual,
                  ligado ? "ONLINE" : "offline",
                  (long)(sJaTeveAck ? idadeAck : -1), sDuty, sAndando);
  }
}

