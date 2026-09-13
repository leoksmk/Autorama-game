/*
  ORBITAL DERBY — controle de nave com ESP32
  ==========================================

  Um controle por nave: um switch de ACELERADOR (martelar = andar) e um de
  AÇÃO (pegar e usar o poder). Ligado ao PC por cabo USB, aparece como porta
  COM; o jogo acha a porta sozinho e reconhece qual nave é qual pelo id.

  Sem rádio, sem Wi-Fi: esta placa é um teclado pela USB, e só.

  LIGAÇÃO
  -------
  Cada switch vai entre o pino e o GND. Nada de resistor: o pull-up interno
  do ESP32 segura o pino em nível alto, e apertar o switch leva a zero.

                        ESP32 clássico     ESP32-C3 SuperMini   ESP32-S2 / S3
      Acelerador        GPIO 25            GPIO 3 (pino "3")    GPIO 3
      Ação              GPIO 26            GPIO 5 (pino "5")    GPIO 5
      O outro lado      GND                GND                  GND

  Esta tabela repete os #define lá embaixo, e os dois têm de andar juntos: se
  o switch do acelerador estiver soldado no pino 4 e aqui disser 3, a placa
  grava sem erro nenhum e o acelerador simplesmente não responde.

  Os pinos são escolhidos pelo modelo na compilação (logo abaixo). Se os
  switches já estiverem soldados em outros pinos, é só trocar ali. Na C3,
  evite 2, 8 e 9 (pinos de boot) e 20/21 (UART).

  ESP32-C3 SUPERMINI
  ------------------
  A SuperMini não tem conversor USB-serial: a porta COM é a USB do próprio C3.
  Compile com a placa "Nologo ESP32C3 Super Mini", que já liga o USB CDC:

      arduino-cli compile --fqbn esp32:esp32:nologo_esp32c3_super_mini firmware/orbital_controle

  Com a placa genérica "ESP32C3 Dev Module" o CDC vem desligado: o Serial sai
  pelos pinos 20/21 e a porta COM fica muda. O controle gravaria sem erro e o
  jogo nunca o acharia — por isso o firmware se recusa a compilar assim.

  Se a gravação não conectar: segure BOOT, aperte e solte RESET, solte BOOT e
  grave de novo (depois, RESET para rodar). O LED azul fica no GPIO 8 e acende
  em nível baixo.

  IDENTIDADE (ÍON ou ÍGNIS)
  -------------------------
  Cada ESP guarda o próprio id na memória interna: 1 = ÍON, 2 = ÍGNIS. Sai de
  fábrica como 1. Para transformar um controle em ÍGNIS, uma vez só:

      python tools/controle_esp.py --definir-id COM5 2

  O número da porta COM NÃO serve para isso: o Windows troca o número quando
  o cabo muda de entrada USB. O id viaja com a placa.

  PROTOCOLO (serial 115200, linhas de texto)
  ------------------------------------------
      ESP -> PC   HELLO ORBITAL <id> <versao>   ao ligar e em resposta a "?"
                  T1 / T0                       acelerador apertou / soltou
                  A1 / A0                       ação apertou / soltou
                  K <t><a>                      pulsação a cada 500 ms, ex. "K 10"
      PC -> ESP   ?                             pede o HELLO
                  ID <n>                        grava o id (1 ou 2)
                  D <0|1>                       liga/desliga o diagnóstico
                  VARRER                        acha em que GPIO está o switch

  QUANDO O BOTÃO NÃO RESPONDE
  ---------------------------
  Não adivinhe o pino: pergunte à placa.

      python tools/controle_esp.py --achar-pino COM9

  A varredura faz dois passos em todos os pinos livres: pull-up procurando quem
  vai a ZERO (switch no GND, a ligação certa) e pull-down procurando quem vai a
  UM (switch no 3V3, ligação invertida — que a varredura só com pull-up não
  enxerga, porque apertar empurra o pino para onde ele já estava).

  Só conta como achado o pino que MUDA durante o teste. Pino ativo em 100% das
  amostras está preso num trilho e não é aperto nenhum: na C3 o 2 e o 9 sempre
  aparecem assim no passo do pull-down, porque a placa tem pull-up de fábrica
  neles. O firmware marca esses e os descarta sozinho.

  Durante a varredura o LED azul acompanha ao vivo: acende no instante em que
  qualquer pino reage. Dá para achar o pad certo encostando o fio, com as duas
  mãos na placa e sem olhar a tela.

  Se NENHUM pino reage nas duas polaridades, acabou o software. É solda fria,
  fio rompido, pad errado, ou os dois fios no mesmo par interno do switch — num
  tátil de 4 pernas, 1-2 e 3-4 já saem ligados de fábrica, e é preciso usar uma
  perna de CADA par. A prova final é o multímetro em continuidade nas duas
  pernas: solto abre, apertado fecha.

  Reservados para o autorama físico, já ignorados com segurança:
      PC -> ESP   M <duty>                      PWM do motor da pista (0..1000)
      ESP -> PC   C <n>                         passou pelo sensor de checkpoint n

  DEBOUNCE
  --------
  Fica AQUI, e é o motivo de este firmware existir em vez de um simples
  "manda o estado do pino". O acelerador é de martelar: cada aperto conta. Um
  switch que repica gera cliques fantasmas e a nave acelera sozinha. O estado
  só muda depois de ESTAVEL_MS milissegundos parado — rápido o bastante para
  25 apertos por segundo, lento o bastante para engolir o repique.
*/

#include <Arduino.h>
#include <Preferences.h>

// ---------------------------------------------------------------------------
// Pinos por modelo
// ---------------------------------------------------------------------------
#if defined(CONFIG_IDF_TARGET_ESP32C3) || defined(CONFIG_IDF_TARGET_ESP32C6)
  #define PINO_ACELERADOR 3
  #define PINO_ACAO       5
#elif defined(CONFIG_IDF_TARGET_ESP32S2) || defined(CONFIG_IDF_TARGET_ESP32S3)
  #define PINO_ACELERADOR 3
  #define PINO_ACAO       5
#else  // ESP32 clássico (DevKit V1, WROOM-32)
  #define PINO_ACELERADOR 25
  #define PINO_ACAO       26
#endif

// ESP32-C3 sem USB CDC On Boot: o Serial iria para os pinos 20/21 e a porta
// COM da USB ficaria muda. Placa C3 com conversor CH340/CP210x na UART (não é
// o caso da SuperMini)? Compile com -DORBITAL_SERIAL_UART para liberar.
#if defined(CONFIG_IDF_TARGET_ESP32C3) && !ARDUINO_USB_CDC_ON_BOOT && !defined(ORBITAL_SERIAL_UART)
  #error "ESP32-C3 com USB CDC On Boot desligado: a porta COM ficaria muda. Arduino IDE: Ferramentas > Placa > esp32 > Nologo ESP32C3 Super Mini (ou Ferramentas > USB CDC On Boot > Enabled). arduino-cli: --fqbn esp32:esp32:nologo_esp32c3_super_mini"
#endif

// LED da placa acende enquanto algum switch está apertado: dá para testar a
// fiação sem PC. Placas sem LED simplesmente não piscam.
#if defined(CONFIG_IDF_TARGET_ESP32C3)
  // SuperMini: LED azul no GPIO 8, ligado ao 3V3 — acende em nível BAIXO. O
  // LED_BUILTIN do C3 genérico não serve: lá ele é um LED RGB endereçável, e
  // mandar o protocolo dele para um LED comum só faz piscar errado.
  #define PINO_LED  8
  #define LED_ACESO LOW
#elif defined(LED_BUILTIN)
  #define PINO_LED  LED_BUILTIN
  #define LED_ACESO HIGH
#endif

// ---------------------------------------------------------------------------
// Parâmetros
// ---------------------------------------------------------------------------
static const char*    VERSAO      = "1.2";
static const uint32_t BAUD        = 115200;
static const uint8_t  ID_PADRAO   = 1;
static const uint32_t ESTAVEL_MS  = 5;     // debounce
static const uint32_t PULSACAO_MS = 500;   // sinal de vida

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

static bool        diagnostico    = false;
static uint32_t    ultimoDiag     = 0;
static const uint32_t DIAG_MS     = 500;

// Pinos que o VARRER testa. Fica de fora o que não pode virar entrada: o LED
// (8), a flash interna (12..17) e a USB (18/19). Na C3 SuperMini os pinos que
// saem no conector são justamente estes.
#if defined(CONFIG_IDF_TARGET_ESP32C3) || defined(CONFIG_IDF_TARGET_ESP32C6)
static const uint8_t PINOS_VARREDURA[] = { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 20, 21 };
#else
static const uint8_t PINOS_VARREDURA[] = {
  4, 5, 13, 14, 16, 17, 18, 19, 21, 22, 23, 25, 26, 27, 32, 33
};
#endif
static const size_t N_VARREDURA = sizeof(PINOS_VARREDURA);

// ---------------------------------------------------------------------------

static void ola() {
  Serial.printf("HELLO ORBITAL %u %s\n", id, VERSAO);
}

// Por que a placa reiniciou. Se aparecer BROWNOUT, o problema é alimentação e
// nenhuma mexida no código vai resolver.
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

static void imprimirBanner() {
  Serial.println();
  Serial.println("========== CONTROLE (Orbital Derby) ==========");
  Serial.printf("id              : %u (%s)\n", id, id == 1 ? "ION" : "IGNIS");
  Serial.printf("firmware        : %s\n", VERSAO);
  Serial.printf("motivo do boot  : %s\n", motivoReset());
  // Os pinos vão no banner porque a pergunta "em que GPIO isto está?" já custou
  // caro aqui: o comentário do cabeçalho e o #define discordaram por semanas.
  // Agora quem responde é a placa, não a documentação.
  Serial.printf("acelerador      : GPIO %u (nivel agora: %d)\n",
                botoes[0].pino, digitalRead(botoes[0].pino));
  Serial.printf("acao            : GPIO %u (nivel agora: %d)\n",
                botoes[1].pino, digitalRead(botoes[1].pino));
  Serial.println("Com o switch SOLTO os dois niveis tem que ser 1.");
  Serial.println("Serial: '?' quem sou | 'ID 1|2' grava o id");
  Serial.println("        'D 1' diagnostico | 'VARRER' acha o pino do switch");
  Serial.println("==============================================");
}

/*
  Acha em que GPIO o switch está de verdade.

  Liga o pull-up em todo pino livre e observa por alguns segundos quem vai a
  zero enquanto você aperta. É a resposta direta para "o botão não funciona":
  ou aparece um pino (e é só corrigir o #define), ou não aparece nenhum — e aí
  o problema é fio, solda ou o outro lado do switch que não está no GND.

  Trava o loop pelo tempo do teste: a pulsação para e o jogo larga o controle
  enquanto isso. É comando de bancada, não de corrida.
*/
// Pinos que a própria placa já puxa com resistor externo. Eles NUNCA obedecem
// ao pull interno contrário e por isso mentem na varredura: com pull-down eles
// aparecem em 1 nas 100% das amostras, e uma leitura ingênua chama isso de
// "switch encontrado". Aqui eles são anotados e nunca contam como achado.
static bool pinoTemPullExterno(uint8_t p) {
#if defined(CONFIG_IDF_TARGET_ESP32C3) || defined(CONFIG_IDF_TARGET_ESP32C6)
  return p == 2 || p == 8 || p == 9;   // strapping: 2 e 9 com pull-up, 8 no LED
#else
  return false;
#endif
}

// Um switch APERTADO E SOLTO muda de nível: aparece em parte das amostras, não
// em todas. Pino em 100% está preso em algum trilho e não é aperto nenhum.
static bool mudouDeVerdade(uint16_t ativos, uint16_t amostras) {
  return ativos > 0 && ativos < amostras;
}

static bool varrerPasso(bool comPullUp, uint32_t duracaoMs) {
  // Com pull-up, o pino em repouso é 1 e um switch para o GND o leva a 0.
  // Com pull-down é o contrário, e é isso que pega o switch ligado no 3V3 por
  // engano — um erro que a varredura só com pull-up NÃO enxerga, porque
  // apertar empurra o pino para onde ele já estava.
  const int nivelAtivo = comPullUp ? LOW : HIGH;

  uint16_t ativos[N_VARREDURA];
  for (size_t i = 0; i < N_VARREDURA; i++) ativos[i] = 0;

  for (size_t i = 0; i < N_VARREDURA; i++)
    pinMode(PINOS_VARREDURA[i], comPullUp ? INPUT_PULLUP : INPUT_PULLDOWN);
  delay(50);                                    // deixa as linhas assentarem

  uint16_t amostras = 0;
  uint32_t fim = millis() + duracaoMs;
  while ((int32_t)(millis() - fim) < 0) {
    // O LED acompanha a varredura ao vivo. É o que permite depurar a fiação
    // com as duas mãos na placa, sem terminal e sem ninguém do outro lado:
    // encostou o fio no pino certo, o LED acende na hora.
    bool algum = false;
    for (size_t i = 0; i < N_VARREDURA; i++) {
      if (digitalRead(PINOS_VARREDURA[i]) != nivelAtivo) continue;
      ativos[i]++;
      if (!pinoTemPullExterno(PINOS_VARREDURA[i])) algum = true;
    }
#ifdef PINO_LED
    digitalWrite(PINO_LED, algum ? LED_ACESO : !LED_ACESO);
#endif
    amostras++;
    delay(2);
  }
#ifdef PINO_LED
  digitalWrite(PINO_LED, !LED_ACESO);
#endif

  bool achou = false;
  for (size_t i = 0; i < N_VARREDURA; i++) {
    uint8_t p = PINOS_VARREDURA[i];
    if (ativos[i] == 0) continue;

    const char* nota;
    if (mudouDeVerdade(ativos[i], amostras)) {
      achou = true;
      nota = "<<< O SWITCH ESTA AQUI";
    } else if (pinoTemPullExterno(p)) {
      nota = "sempre ativo -- pull-up de fabrica da placa, ignore";
    } else {
      nota = comPullUp ? "sempre em zero -- fio preso no GND, nao e aperto"
                       : "sempre em um -- fio preso na alimentacao, nao e aperto";
    }
    Serial.printf("[scan] GPIO %-2u  ativo em %u de %u amostras  %s\n",
                  p, ativos[i], amostras, nota);
  }
  return achou;
}

static void varrerPinos() {
  Serial.println("[scan] APERTE E SOLTE os switches sem parar ate o 'fim'.");

  Serial.println("[scan] passo 1/2: pull-up, procurando pino que vai a ZERO");
  Serial.println("[scan]            (switch ligado ao GND -- a ligacao correta)");
  bool achouGnd = varrerPasso(true, 5000);
  if (!achouGnd) Serial.println("[scan] passo 1: nenhum pino MUDOU para zero.");

  Serial.println("[scan] passo 2/2: pull-down, procurando pino que vai a UM");
  Serial.println("[scan]            (switch ligado ao 3V3 -- ligacao invertida)");
  bool achou3v3 = varrerPasso(false, 5000);
  if (!achou3v3) Serial.println("[scan] passo 2: nenhum pino MUDOU para um.");

  if (achou3v3 && !achouGnd) {
    Serial.println("[scan] >>> O SWITCH ESTA NO 3V3, NAO NO GND.");
    Serial.println("[scan] >>> Mova o fio do 3V3 para um pino GND. E so isso.");
  } else if (!achouGnd && !achou3v3) {
    Serial.println("[scan] NENHUM pino reagiu, nas duas polaridades.");
    Serial.println("[scan] O switch nao esta chegando eletricamente a GPIO nenhum:");
    Serial.println("[scan]   - solda fria, fio rompido ou pad errado");
    Serial.println("[scan]   - os dois fios no mesmo par interno do switch");
    Serial.println("[scan]     (tactil de 4 pernas: 1-2 e 3-4 ja sao ligados de fabrica;");
    Serial.println("[scan]      tem de usar uma perna de CADA par, ex. 1 e 3)");
    Serial.println("[scan]   - switch queimado -- teste a continuidade no multimetro");
  }

  for (Botao& b : botoes) pinMode(b.pino, INPUT_PULLUP);
  delay(5);
  Serial.println("[scan] fim.");
}

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
  if (cmd[0] == 'D' && cmd[1] == ' ') {
    diagnostico = atoi(cmd + 2) != 0;
    Serial.printf("[ctrl] diagnostico %s\n", diagnostico ? "ligado" : "desligado");
    return;
  }
  if (strcmp(cmd, "VARRER") == 0) {
    varrerPinos();
    return;
  }
  // "M <duty>" (motor) e demais comandos: reservados, ignorados por enquanto.
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
    }
  }
}

void setup() {
  Serial.begin(BAUD);

  for (Botao& b : botoes) {
    pinMode(b.pino, INPUT_PULLUP);
  }

  // O pull-up interno é fraco (uns 45 kΩ). Com a capacitância do fio, a linha
  // leva microssegundos para subir — ler no mesmo instante do pinMode pode dar
  // LOW e gravar o botão como "apertado desde o boot". A partir daí o aperto
  // não muda nada (já está apertado) e o botão parece morto até o primeiro
  // soltar. Esperar a linha assentar custa 5 ms e elimina o caso.
  delay(5);

  for (Botao& b : botoes) {
    b.bruto = b.estavel = (digitalRead(b.pino) == LOW);
    b.desde = millis();
  }

#ifdef PINO_LED
  pinMode(PINO_LED, OUTPUT);
  digitalWrite(PINO_LED, !LED_ACESO);
#endif

  prefs.begin("orbital", false);
  id = prefs.getUChar("id", ID_PADRAO);
  if (id != 1 && id != 2) {
    id = ID_PADRAO;
  }

  imprimirBanner();
  delay(50);
  ola();
}

void loop() {
  uint32_t agora = millis();

  lerBotoes(agora);
  lerSerial();

  if (agora - ultimaPulsacao >= PULSACAO_MS) {
    ultimaPulsacao = agora;
    Serial.printf("K %c%c\n", botoes[0].estavel ? '1' : '0', botoes[1].estavel ? '1' : '0');
  }

  // Diagnóstico: nível CRU do pino ao lado do estado já sem repique. Se o cru
  // não muda quando você aperta, o problema está antes do software — pino
  // errado, fio solto, ou o outro lado do switch fora do GND.
  if (diagnostico && agora - ultimoDiag >= DIAG_MS) {
    ultimoDiag = agora;
    Serial.printf("[ctrl] T=GPIO%u cru=%d %s | A=GPIO%u cru=%d %s\n",
                  botoes[0].pino, digitalRead(botoes[0].pino),
                  botoes[0].estavel ? "APERTADO" : "solto",
                  botoes[1].pino, digitalRead(botoes[1].pino),
                  botoes[1].estavel ? "APERTADO" : "solto");
  }

#ifdef PINO_LED
  bool algum = botoes[0].estavel || botoes[1].estavel;
  digitalWrite(PINO_LED, algum ? LED_ACESO : !LED_ACESO);
#endif
}
