/*
  ORBITAL DERBY — carrinho que carrega a nave (ESP32-C3 SuperMini)
  =================================================================

  Recebe por ESP-NOW o PWM que o jogo calculou para a pista dele e move o motor.
  Quem manda é o controle daquela nave, que é a ponte entre o PC e este rádio:

      jogo  --USB-->  controle  --ESP-NOW-->  ESTE CARRINHO  -->  motor

  EMPARELHAMENTO
  --------------
  O controle chama (PAREAR, em broadcast) e este carrinho responde (PARCEIRO)
  se o id bater. Daí em diante os dois falam UNICAST, o que dá confirmação de
  rádio nos dois sentidos — em broadcast o envio "dá certo" mesmo sem ninguém
  ouvindo, e some a única evidência que importa quando algo não funciona.

  DE QUEM É O PACOTE
  ------------------
  Cada carrinho guarda um id na memória interna: 1 = ÍON, 2 = ÍGNIS. Sai de
  fábrica como 1; para virar o carrinho do ÍGNIS, mande pelo monitor serial:

      ID 2

  O JOGO NÃO PODE DEIXAR O MOTOR LIGADO POR ESQUECIMENTO
  ------------------------------------------------------
  Todo comando de dirigir vem com prazo de validade. Passou o prazo sem pacote
  novo, o motor para — não existe comando de "solta". Link caiu, jogo fechou,
  controle desplugado, bateria do controle acabou: tudo dá em motor parado.

  PWM NUM CIRCUITO QUE NÃO TEM PWM
  --------------------------------
  O motor é chaveado por um TIP122 (Darlington), que come 1 a 2 V. De 3,8 V
  sobram uns 2 V no motor, já no limite para arrancar; PWM rápido cortaria a
  tensão média e o motor não sairia do lugar, e o capacitor em paralelo viraria
  pico de corrente no transistor a cada chaveamento.

  Então o duty do jogo vira tempo, não tensão: dentro de uma janela de
  JANELA_MS o motor fica ligado a fração pedida, com tensão cheia. Meio
  acelerador vira movimento aos trancos, não meia velocidade. É o máximo
  honesto com este circuito.

  Duas coisas tornam isso utilizável:
    - ARRANQUE_MS: saindo do zero, o primeiro trecho é ligado cheio, para
      vencer o atrito estático — senão o carrinho fica tremendo sem sair.
    - DUTY_MINIMO: abaixo disso o motor não anda de todo jeito, então o
      carrinho fica parado em vez de zumbir.

  Trocar o TIP122 por um MOSFET de nível lógico (IRLZ44N, AO3400) devolveria
  cerca de 1,5 V ao motor e permitiria PWM de verdade. Aí este arquivo muda em
  um lugar só: motorAplicar().

  Circuito (esquemático da PCB):
    GPIO4 -> R1 330R -> base do TIP122 (chave low-side)
    motor entre +3V8 e o coletor; D1 1N4007 em antiparalelo com o motor
    C1 em paralelo com o motor (ruído das escovas)
    emissor do TIP122 -> GND (comum com o GND do ESP)

  SEM controle de direção e SEM freio: é chave liga/desliga, um sentido só, e
  ao desligar o carrinho sai em roda-livre.

  LED DA PLACA
  ------------
    pisca devagar -> viva, mas sem controle emparelhado
    aceso fixo    -> emparelhado
    apagado       -> motor andando (buraco no padrão), ou placa morta

  MONITOR SERIAL
  --------------
    ?        estado completo
    ID 1|2   grava o id
    (resto)  pulso de teste no motor, sem passar pelo rádio

  Compile com a placa "Nologo ESP32C3 Super Mini":
      arduino-cli compile --fqbn esp32:esp32:nologo_esp32c3_super_mini firmware/orbital_carrinho
*/

#include <Arduino.h>
#include <Preferences.h>
#include <WiFi.h>
#include <esp_now.h>
#include <esp_wifi.h>
#include <esp_idf_version.h>
#include "espnow_link.h"

// ---------------------------------------------------------------- pinagem ---
static const uint8_t MOTOR_PINO = 4;      // base do TIP122 via R1 330R
#if defined(CONFIG_IDF_TARGET_ESP32C3)
  #define PINO_LED  8                     // LED azul da SuperMini
  #define LED_ACESO LOW                   // ligado ao 3V3: acende em nível baixo
#elif defined(LED_BUILTIN)
  #define PINO_LED  LED_BUILTIN
  #define LED_ACESO HIGH
#endif

#if defined(CONFIG_IDF_TARGET_ESP32C3) && !ARDUINO_USB_CDC_ON_BOOT && !defined(ORBITAL_SERIAL_UART)
  #error "ESP32-C3 com USB CDC On Boot desligado: o monitor serial ficaria mudo. Arduino IDE: Ferramentas > Placa > esp32 > Nologo ESP32C3 Super Mini. arduino-cli: --fqbn esp32:esp32:nologo_esp32c3_super_mini"
#endif

// A assinatura do callback de envio mudou no IDF 5.4.
#if ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 4, 0)
  #define ORBITAL_ENVIO_CB_NOVO 1
#else
  #define ORBITAL_ENVIO_CB_NOVO 0
#endif

// ---------------------------------------------------------------- ajustes ---
static const char*    VERSAO          = "3.0";
static const uint32_t BAUD            = 115200;
static const uint8_t  ID_PADRAO       = 1;

static const uint32_t JANELA_MS       = 100;  // janela do duty por tempo
static const uint32_t MIN_LIGADO_MS   = 30;   // trecho ligado curto demais não move
static const uint16_t DUTY_MINIMO     = 120;  // abaixo disso, fica parado
static const uint32_t ARRANQUE_MS     = 90;   // tranco para vencer o atrito parado

static const uint16_t TESTE_PULSO_MS  = 400;  // pulso do teste manual
static const uint32_t DIAG_PERIODO_MS = 3000;
static const uint32_t LED_BUSCA_MS    = 400;

// ------------------------------------------------------------------ estado ---
static uint8_t  macBroadcast[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
static uint8_t  macControle[6]  = {0, 0, 0, 0, 0, 0};
static bool     temControle     = false;
static Preferences prefs;
static uint8_t  id = ID_PADRAO;

// Comando em vigor (escrito pelo callback, lido pelo loop).
static volatile uint16_t dutyAlvo      = 0;
static volatile uint32_t dutyValidoAte = 0;
static volatile bool     pulsoPendente = false;
static volatile uint16_t pulsoMs       = 0;
static volatile uint32_t ultimoSeqPulso = 0xFFFFFFFFUL;

// Respostas pendentes: o callback marca, o LOOP transmite. Mandar pacote de
// dentro do callback é pedir dor de cabeça — ele roda em task do WiFi.
static volatile bool     ackPendente     = false;
static volatile uint32_t ackSeq          = 0;
static volatile bool     parearPendente  = false;
static volatile uint8_t  macPedinte[6]   = {0, 0, 0, 0, 0, 0};
static volatile bool     controleNovo    = false;

// Diagnóstico contado no callback, impresso no loop (Serial lá é proibido).
static volatile uint32_t rxTotal      = 0;
static volatile uint32_t rxMeus       = 0;
static volatile uint32_t rxOutroId    = 0;
static volatile uint32_t rxMagicRuim  = 0;
static volatile uint32_t rxVersaoRuim = 0;
static volatile uint32_t versaoVista  = 0;
static volatile uint32_t ultimoRxMs   = 0;
static volatile bool     jaRecebeu    = false;
static volatile uint32_t enviosOk     = 0;
static volatile uint32_t enviosFalha  = 0;

static portMUX_TYPE trava = portMUX_INITIALIZER_UNLOCKED;

static bool     motorLigado   = false;
static uint32_t motorPulsoAte = 0;    // pulso de bancada/teste em andamento
static uint32_t arranqueAte   = 0;    // tranco de partida
static uint32_t janelaInicio  = 0;
static uint16_t dutyEmVigor   = 0;    // o que o loop está realmente aplicando
static bool     ouvindoMostrado = false;
static uint32_t proximoDiag   = 0;
static uint32_t proximoAnuncio = 0;
static esp_err_t errPs = ESP_OK, errProto = ESP_OK, errCanal = ESP_OK;
static char     linha[32];
static uint8_t  tamLinha      = 0;

// ------------------------------------------------------------------- motor ---
static void motorEscrever(bool ligar) {
  if (ligar == motorLigado) return;
  motorLigado = ligar;
  digitalWrite(MOTOR_PINO, ligar ? HIGH : LOW);
}

static void ledAcender(bool aceso) {
#ifdef PINO_LED
  digitalWrite(PINO_LED, aceso ? LED_ACESO : !LED_ACESO);
#else
  (void)aceso;
#endif
}

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

/// Decide se o motor fica ligado neste instante. Toda a tradução de
/// "duty do jogo" para "tempo ligado" está aqui.
static void motorAplicar(uint32_t agora) {
  // Pulso de bancada/teste manda enquanto durar.
  if ((int32_t)(agora - motorPulsoAte) < 0) {
    motorEscrever(true);
    return;
  }

  uint16_t duty = dutyEmVigor;
  if (duty < DUTY_MINIMO) {
    motorEscrever(false);
    arranqueAte = 0;
    return;
  }

  // Saindo do zero: tranco cheio para vencer o atrito estático.
  if ((int32_t)(agora - arranqueAte) < 0) {
    motorEscrever(true);
    return;
  }

  if (duty >= 1000) {          // acelerador cheio: contínuo, sem picar
    motorEscrever(true);
    return;
  }

  uint32_t ligado = (JANELA_MS * duty) / 1000;
  if (ligado < MIN_LIGADO_MS) ligado = MIN_LIGADO_MS;
  const uint32_t fase = (agora - janelaInicio) % JANELA_MS;
  motorEscrever(fase < ligado);
}

/// Estado da placa. Sai no boot e sob demanda ('?'): canal e protocolo REAIS
/// dos dois lados é o que precisa bater para o rádio funcionar.
static void imprimirBanner() {
  uint8_t canal = 0;
  wifi_second_chan_t seg = WIFI_SECOND_CHAN_NONE;
  esp_wifi_get_channel(&canal, &seg);
  uint8_t proto = 0;
  esp_wifi_get_protocol(WIFI_IF_STA, &proto);

  Serial.println();
  Serial.println("========== CARRINHO (Orbital Derby) ==========");
  Serial.printf("id              : %u (%s)\n", id, id == 1 ? "ION" : "IGNIS");
  Serial.printf("firmware        : %s   link versao %u\n", VERSAO, LINK_VERSAO);
  Serial.printf("MAC desta placa : %s\n", WiFi.macAddress().c_str());
  Serial.printf("motivo do boot  : %s\n", motivoReset());
  Serial.printf("canal REAL      : %u   <-- tem que ser igual no controle\n", canal);
  Serial.printf("protocolo       : pedido=0x%02X real=0x%02X %s\n",
                LINK_PROTOCOLO, proto,
                (proto == LINK_PROTOCOLO) ? "(11b/g/n + LR, ok)" : "(NAO bate!)");
  int8_t potencia = 0;
  esp_wifi_get_max_tx_power(&potencia);
  Serial.printf("potencia TX     : %d (%.1f dBm)\n", potencia, potencia * 0.25);
  Serial.printf("set_ps=%s set_protocol=%s set_channel=%s\n",
                esp_err_to_name(errPs), esp_err_to_name(errProto), esp_err_to_name(errCanal));
  if (temControle) {
    Serial.printf("controle        : %02X:%02X:%02X:%02X:%02X:%02X\n",
                  macControle[0], macControle[1], macControle[2],
                  macControle[3], macControle[4], macControle[5]);
  } else {
    Serial.println("controle        : (nenhum emparelhado ainda)");
  }
  Serial.println("Serial: '?' estado | 'ID 1|2' grava o id | resto = teste do motor");
  Serial.println("=============================================");
}

// -------------------------------------------------------------- callbacks ---
// Rodam fora do loop, em task do WiFi: não acionam motor, não transmitem e
// NUNCA chamam Serial. Só anotam.

#if ORBITAL_ENVIO_CB_NOVO
static void aoEnviar(const wifi_tx_info_t* info, esp_now_send_status_t status)
#else
static void aoEnviar(const uint8_t* mac, esp_now_send_status_t status)
#endif
{
  portENTER_CRITICAL(&trava);
  if (status == ESP_NOW_SEND_SUCCESS) enviosOk++; else enviosFalha++;
  portEXIT_CRITICAL(&trava);
}

#if ESP_ARDUINO_VERSION_MAJOR >= 3
static void aoReceber(const esp_now_recv_info_t* info, const uint8_t* dados, int len)
#else
static void aoReceber(const uint8_t* mac, const uint8_t* dados, int len)
#endif
{
  if (len < (int)sizeof(LinkCabecalho)) return;

  LinkCabecalho cab;
  memcpy(&cab, dados, sizeof(cab));

  const uint32_t agora = millis();
  portENTER_CRITICAL(&trava);
  rxTotal++;
  portEXIT_CRITICAL(&trava);

  if (cab.magic != LINK_MAGIC) {              // outro projeto na sala
    portENTER_CRITICAL(&trava);
    rxMagicRuim++;
    portEXIT_CRITICAL(&trava);
    return;
  }
  if (cab.versao != LINK_VERSAO) {            // espnow_link.h divergente
    portENTER_CRITICAL(&trava);
    rxVersaoRuim++;
    versaoVista = cab.versao;
    portEXIT_CRITICAL(&trava);
    return;
  }
  // Ecos do que nós mesmos mandamos.
  if (cab.tipo == LINK_TIPO_ACK || cab.tipo == LINK_TIPO_PARCEIRO) return;

  if (cab.id != id && cab.id != LINK_ID_QUALQUER) {
    portENTER_CRITICAL(&trava);
    rxOutroId++;                              // é do carrinho do vizinho
    portEXIT_CRITICAL(&trava);
    return;
  }

#if ESP_ARDUINO_VERSION_MAJOR >= 3
  const uint8_t* origem = (info != NULL) ? info->src_addr : NULL;
#else
  const uint8_t* origem = mac;
#endif

  portENTER_CRITICAL(&trava);
  rxMeus++;
  ultimoRxMs = agora;
  jaRecebeu  = true;
  // Qualquer pacote válido serve de apresentação: a chamada pode ter se
  // perdido e um ping ter passado.
  if (!temControle && origem != NULL) {
    memcpy((void*)macPedinte, origem, 6);
    controleNovo = true;
  }
  portEXIT_CRITICAL(&trava);

  // --- chamada de emparelhamento ---
  if (cab.tipo == LINK_TIPO_PAREAR && len == (int)sizeof(PacoteParear) && origem != NULL) {
    portENTER_CRITICAL(&trava);
    memcpy((void*)macPedinte, origem, 6);
    parearPendente = true;
    controleNovo   = true;
    portEXIT_CRITICAL(&trava);
    return;
  }

  if (cab.tipo == LINK_TIPO_DIRIGIR && len == (int)sizeof(PacoteDirigir)) {
    PacoteDirigir p;
    memcpy(&p, dados, sizeof(p));
    uint16_t validade = p.validade > 0 ? p.validade : LINK_VALIDADE_MS;
    portENTER_CRITICAL(&trava);
    dutyAlvo      = p.duty > 1000 ? 1000 : p.duty;
    dutyValidoAte = agora + validade;
    ackPendente   = true;
    ackSeq        = cab.seq;
    portEXIT_CRITICAL(&trava);
    return;
  }

  if (cab.tipo == LINK_TIPO_PULSO && len == (int)sizeof(PacotePulso)) {
    PacotePulso p;
    memcpy(&p, dados, sizeof(p));
    portENTER_CRITICAL(&trava);
    // Reenvio do mesmo pulso não move o motor duas vezes, mas é confirmado:
    // um ACK perdido não pode deixar o controle achando que morremos.
    if (cab.seq != ultimoSeqPulso) {
      ultimoSeqPulso = cab.seq;
      pulsoMs        = p.ms > LINK_PULSO_MAX_MS ? LINK_PULSO_MAX_MS : p.ms;
      pulsoPendente  = true;
    }
    ackPendente = true;
    ackSeq      = cab.seq;
    portEXIT_CRITICAL(&trava);
    return;
  }

  if (cab.tipo == LINK_TIPO_PING && len == (int)sizeof(PacotePing)) {
    portENTER_CRITICAL(&trava);
    ackPendente = true;
    ackSeq      = cab.seq;
    portEXIT_CRITICAL(&trava);
    return;
  }
}

// ------------------------------------------------------------------ rádio ---

static void guardarControle(const uint8_t* mac) {
  memcpy(macControle, mac, 6);
  esp_now_peer_info_t peer = {};
  memcpy(peer.peer_addr, macControle, 6);
  peer.channel = 0;
  peer.ifidx   = WIFI_IF_STA;
  peer.encrypt = false;
  esp_now_del_peer(macControle);     // se já existia de um pareamento anterior
  esp_now_add_peer(&peer);
  temControle = true;
}

static void montarCabecalho(LinkCabecalho& cab, uint8_t tipo, uint32_t seq) {
  cab.magic  = LINK_MAGIC;
  cab.versao = LINK_VERSAO;
  cab.tipo   = tipo;
  cab.id     = id;
  cab.seq    = seq;
}

/// Responde a chamada: "sou eu". O controle pega o MAC do próprio frame.
static void responderParear(uint32_t seq) {
  PacoteParceiro p;
  montarCabecalho(p.cab, LINK_TIPO_PARCEIRO, seq);
  p.versaoFw = LINK_VERSAO;
  // Vai em broadcast: o controle ainda não tem este carrinho como peer, e
  // ESP-NOW só entrega unicast para peer registrado dos dois lados.
  esp_now_send(macBroadcast, (const uint8_t*)&p, sizeof(p));
}

static void responderAck(uint32_t seq) {
  PacoteAck ack;
  montarCabecalho(ack.cab, LINK_TIPO_ACK, seq);
  ack.andando = motorLigado ? 1 : 0;
  ack.duty    = dutyEmVigor;
  esp_now_send(temControle ? macControle : macBroadcast,
               (const uint8_t*)&ack, sizeof(ack));
}

// ---------------------------------------------------------------- serial ---
static void tratarComando(const char* cmd, uint32_t agora) {
  if (strcmp(cmd, "?") == 0) {
    uint32_t sUltimo, sTotal, sMeus, sOutro, sMagic, sVersao, sOk, sFalha;
    bool sJa;
    portENTER_CRITICAL(&trava);
    sUltimo = ultimoRxMs;
    sJa     = jaRecebeu;
    sTotal  = rxTotal;
    sMeus   = rxMeus;
    sOutro  = rxOutroId;
    sMagic  = rxMagicRuim;
    sVersao = rxVersaoRuim;
    sOk     = enviosOk;
    sFalha  = enviosFalha;
    portEXIT_CRITICAL(&trava);

    imprimirBanner();
    const int32_t idade = sJa ? (int32_t)(agora - sUltimo) : -1;
    Serial.printf("[carro] duty=%u motor=%s | ultimo pacote ha %ld ms\n",
                  dutyEmVigor, motorLigado ? "ON" : "off", (long)idade);
    Serial.printf("[carro] total=%lu meus=%lu outro_id=%lu magic=%lu versao_ruim=%lu | envios ok=%lu falha=%lu\n",
                  (unsigned long)sTotal, (unsigned long)sMeus, (unsigned long)sOutro,
                  (unsigned long)sMagic, (unsigned long)sVersao,
                  (unsigned long)sOk, (unsigned long)sFalha);
    return;
  }
  if (strncmp(cmd, "ID ", 3) == 0) {
    int n = atoi(cmd + 3);
    if (n == 1 || n == 2) {
      id = (uint8_t)n;
      prefs.putUChar("id", id);
      temControle = false;        // o parceiro mudou: espera o controle certo
      Serial.printf("[carro] id gravado: %u (%s)\n", id, id == 1 ? "ION" : "IGNIS");
    } else {
      Serial.println("[carro] id invalido (use 1 ou 2)");
    }
    return;
  }
  // Qualquer outra coisa: pulso de teste sem passar pelo rádio. Se ISTO move o
  // carrinho mas o jogo não, o problema é link. Se nem isto move, é
  // motor/transistor/alimentação.
  portENTER_CRITICAL(&trava);
  pulsoMs       = TESTE_PULSO_MS;
  pulsoPendente = true;
  portEXIT_CRITICAL(&trava);
  Serial.printf("[carro] TESTE: motor por %u ms (sem radio)\n", TESTE_PULSO_MS);
}

static void lerSerial(uint32_t agora) {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\n' || c == '\r') {
      linha[tamLinha] = '\0';
      tratarComando(linha, agora);     // linha vazia também vira teste
      tamLinha = 0;
    } else if (tamLinha < sizeof(linha) - 1) {
      linha[tamLinha++] = c;
    } else {
      tamLinha = 0;
    }
  }
}

// ------------------------------------------------------------------- setup ---
void setup() {
  Serial.begin(BAUD);

  // Motor desligado ANTES de qualquer outra coisa: se o resto falhar e
  // reiniciar, a placa nunca passa por um estado com o motor solto girando.
  pinMode(MOTOR_PINO, OUTPUT);
  digitalWrite(MOTOR_PINO, LOW);
  motorLigado = false;

#ifdef PINO_LED
  pinMode(PINO_LED, OUTPUT);
#endif
  ledAcender(false);

  prefs.begin("orbital", false);
  id = prefs.getUChar("id", ID_PADRAO);
  if (id != 1 && id != 2) {
    id = ID_PADRAO;
  }

  // O carrinho não tem PC para mandar ligar o rádio: aqui ele sobe sempre.
  WiFi.mode(WIFI_STA);
  esp_wifi_set_storage(WIFI_STORAGE_RAM);
  WiFi.disconnect(false, true);
  errPs    = esp_wifi_set_ps(WIFI_PS_NONE);   // sem isso o rádio dorme e fica surdo
  errProto = esp_wifi_set_protocol(WIFI_IF_STA, LINK_PROTOCOLO);
  errCanal = esp_wifi_set_channel(LINK_WIFI_CHANNEL, WIFI_SECOND_CHAN_NONE);
  esp_wifi_set_max_tx_power(LINK_POTENCIA);   // a antena da SuperMini precisa de tudo

  if (esp_now_init() != ESP_OK) {
    Serial.println("[carro] ESP-NOW nao inicializou -- reiniciando");
    delay(1000);
    ESP.restart();
  }
  esp_now_register_recv_cb(aoReceber);
  esp_now_register_send_cb(aoEnviar);

  // Receber não precisa de peer, mas RESPONDER precisa. O broadcast serve até
  // o controle se apresentar.
  esp_now_peer_info_t peer = {};
  memcpy(peer.peer_addr, macBroadcast, 6);
  peer.channel = 0;
  peer.ifidx   = WIFI_IF_STA;
  peer.encrypt = false;
  esp_now_add_peer(&peer);

  imprimirBanner();
  janelaInicio = millis();
  proximoDiag  = millis() + DIAG_PERIODO_MS;
}

// -------------------------------------------------------------------- loop ---
void loop() {
  const uint32_t agora = millis();

  lerSerial(agora);

  // --- snapshot do que os callbacks anotaram --------------------------------
  bool     novoPulso = false, mandarAck = false, mandarParear = false, novoControle = false;
  uint16_t msPulso = 0;
  uint32_t seqAck = 0;
  uint16_t sDuty;
  uint32_t sValidoAte, sUltimoRx, sTotal, sMeus, sOutro, sMagic, sVersaoRuim, sVersaoVista;
  uint32_t sOk, sFalha;
  bool     sJaRecebeu;
  uint8_t  sMacPedinte[6];

  portENTER_CRITICAL(&trava);
  if (pulsoPendente) {
    pulsoPendente = false;
    msPulso       = pulsoMs;
    novoPulso     = true;
  }
  if (ackPendente) {
    ackPendente = false;
    mandarAck   = true;
    seqAck      = ackSeq;
  }
  if (parearPendente) {
    parearPendente = false;
    mandarParear   = true;
  }
  if (controleNovo) {
    controleNovo = false;
    novoControle = true;
  }
  memcpy(sMacPedinte, (const void*)macPedinte, 6);
  sDuty        = dutyAlvo;
  sValidoAte   = dutyValidoAte;
  sUltimoRx    = ultimoRxMs;
  sJaRecebeu   = jaRecebeu;
  sTotal       = rxTotal;
  sMeus        = rxMeus;
  sOutro       = rxOutroId;
  sMagic       = rxMagicRuim;
  sVersaoRuim  = rxVersaoRuim;
  sVersaoVista = versaoVista;
  sOk          = enviosOk;
  sFalha       = enviosFalha;
  portEXIT_CRITICAL(&trava);

  // --- emparelhamento --------------------------------------------------------
  if (novoControle) {
    guardarControle(sMacPedinte);
    Serial.printf("[carro] >>> emparelhado com %02X:%02X:%02X:%02X:%02X:%02X\n",
                  sMacPedinte[0], sMacPedinte[1], sMacPedinte[2],
                  sMacPedinte[3], sMacPedinte[4], sMacPedinte[5]);
  }
  if (mandarParear) {
    responderParear(0);
  }

  // Sem controle emparelhado, o carrinho também chama. Antes ele só respondia,
  // e bastava a chamada do controle se perder para nunca acontecer nada; assim
  // o emparelhamento funciona se qualquer uma das duas direções estiver viva.
  if (!temControle && (int32_t)(agora - proximoAnuncio) >= 0) {
    proximoAnuncio = agora + LINK_PAREAR_MS * 2;
    responderParear(0);
  }

  // --- validade: sem comando novo, o motor cai --------------------------------
  // É o que garante que link caído, jogo fechado ou controle desplugado param o
  // carrinho sem ninguém precisar mandar "solta".
  uint16_t dutyAntes = dutyEmVigor;
  dutyEmVigor = ((int32_t)(agora - sValidoAte) < 0) ? sDuty : 0;

  // Saiu do zero agora: dá o tranco de partida e recomeça a janela.
  if (dutyAntes < DUTY_MINIMO && dutyEmVigor >= DUTY_MINIMO) {
    arranqueAte  = agora + ARRANQUE_MS;
    janelaInicio = agora;
  }

  if (novoPulso) {
    motorPulsoAte = agora + msPulso;
  }

  motorAplicar(agora);

  // --- responde o controle (transmitir fora do callback) ---------------------
  if (mandarAck) {
    responderAck(seqAck);
  }

  // --- LED -------------------------------------------------------------------
  const int32_t idadeRx = (int32_t)(agora - sUltimoRx);
  const bool ouvindo = sJaRecebeu && (idadeRx < (int32_t)LINK_TIMEOUT_MS);

  if (motorLigado) {
    ledAcender(false);                                // buraco = motor andando
  } else if (ouvindo) {
    ledAcender(true);                                 // fixo = ouvindo o controle
  } else {
    ledAcender(((agora / LED_BUSCA_MS) % 2) == 0);    // pisca = viva, mas surda
  }

  // Calou por LINK_TIMEOUT_MS: o controle reiniciou, saiu do ar ou voltou a
  // procurar. Sem soltar o emparelhamento aqui, o carrinho ficaria calado para
  // sempre esperando alguém que não vai mais chamar.
  if (temControle && !ouvindo) {
    temControle = false;
    proximoAnuncio = agora;
  }

  if (ouvindo != ouvindoMostrado) {
    ouvindoMostrado = ouvindo;
    Serial.println(ouvindo ? "[carro] >>> OUVINDO o controle"
                           : "[carro] >>> PERDI o controle (parou de chegar pacote)");
  }

  // --- diagnóstico -----------------------------------------------------------
  if ((int32_t)(agora - proximoDiag) >= 0) {
    proximoDiag = agora + DIAG_PERIODO_MS;
    if (sVersaoRuim > 0) {
      Serial.printf("[carro] ATENCAO: %lu pacotes com espnow_link.h versao %lu (aqui e %u). "
                    "As duas copias do header tem que ser iguais.\n",
                    (unsigned long)sVersaoRuim, (unsigned long)sVersaoVista, LINK_VERSAO);
    }
    if (!sJaRecebeu) {
      Serial.println("[carro] SEM SINAL: nenhum pacote desde o boot. Controle com 'NOW 1'? mesmo canal?");
    } else {
      Serial.printf("[carro] %s | duty=%u motor=%s | meus=%lu outro_id=%lu magic=%lu total=%lu | envios ok=%lu falha=%lu\n",
                    ouvindo ? "link OK" : "link MUDO", dutyEmVigor,
                    motorLigado ? "ON" : "off",
                    (unsigned long)sMeus, (unsigned long)sOutro,
                    (unsigned long)sMagic, (unsigned long)sTotal,
                    (unsigned long)sOk, (unsigned long)sFalha);
    }
  }
}
