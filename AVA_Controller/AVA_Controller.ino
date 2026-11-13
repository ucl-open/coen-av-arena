// AVA_Controller.ino

//┌─────────────────────────────────────────────┐
//│  The controller sketch for the Arduino      │
//└─────────────────────────────────────────────┘
// Load this sketch onto the Arduino MEGA, so that it can properly handle
// the incoming LED messages from Bonsai. It has a bitmask mode, full-frame mode, and single-LED ASCII mode.
// Each mode supports both immediate display and staged (pre-loaded) display.

// @hamid

#include <FastLED.h>

#define LED_PIN     6
#define NUM_LEDS    16
#define LED_TYPE    WS2812B
#define LED_BRIGHT  10       // LED brightness The range here is 0 to 255 (above that, values loop around, i.e. 256 == 0)
#define COLOR_ORDER GRB       // This is how this LED_TYPE transmits color data, but you still send your values ordered as RGB
#define BAUD_RATE   115200

// Baud rate is how fast two devices communicate, in bits per second (i.e. 115200 characters/sec)
// For 400 LEDs, the bitmask packet is:
// 1 header byte (0xFE)
// 50 mask bytes (400 bits / 8)
// 3 RGB bytes
// Total: 54 bytes. Each byte is 10 bits on the wire, so 540 bits total.

// Baud rate	Transmission time
// 115200	    4.7 ms
// 250000	    2.2 ms
// 500000	    1.1 ms
// 10000000	    0.05 ms

// Why not just have the highest Baud rate possible at all times? Higher rates are more sensitive to electrical noise, cable length,
// and connection quality. One corrupted byte and your entire transmission will be wrong. At high rates, the Arduino may struggle processing
// the incoming data at such speed. At higher rates, there are also diminishing returns, because the latency bottleneck in your entire
// closed-loop system may not be the transmission rate, but instead be things like camera frame rate or the speed of your LED_TYPE. Finally,
// at high rates, visualizing the transmission (e.g. in Bonsai) may become difficult, meaning that debugging is made harder.

// ── HEADER BYTES ──
// Binary headers tell the Arduino what kind of message is coming.
// Immediate headers load the data AND call show() right away.
// Staged headers load the data into the leds[] buffer WITHOUT calling show().
// A staged pattern is displayed only when the trigger command 'T' is received.
//
// Header  │ Mode       │ Action
// ────────┼────────────┼──────────────────────────
// 0xFF    │ Full-frame │ Immediate (load + show)
// 0xFE    │ Bitmask    │ Immediate (load + show)
// 0xFD    │ Bitmask    │ Staged (load only)
// 0xFC    │ Full-frame │ Staged (load only)

// BITMASK MODE: header + bitmask + 3 RGB bytes (to be used when all LEDs have the same color)
// Header byte (0xFE or 0xFD) + MASK_BYTES (bitmask) + 3 bytes (R, G, B)
// Any LED with its bit set to 1 gets the shared RGB color
// Any LED with its bit set to 0 gets turned off
#define HEADER_BITMASK          0xFE  // Immediate bitmask
#define HEADER_BITMASK_STAGED   0xFD  // Staged bitmask (load only, don't show)
#define MASK_BYTES ((NUM_LEDS + 7) / 8)
#define BITMASK_PAYLOAD (MASK_BYTES + 3)

// FULL-FRAME MODE: header + 3 bytes per LED (to be used when LEDs have unique colors)
// Each LED gets its own RGB color
#define HEADER_FRAME          0xFF  // Immediate full-frame
#define HEADER_FRAME_STAGED   0xFC  // Staged full-frame (load only, don't show)
#define FRAME_PAYLOAD (NUM_LEDS * 3)

// Single buffer large enough for either mode
#define MAX_PAYLOAD (FRAME_PAYLOAD > BITMASK_PAYLOAD ? FRAME_PAYLOAD : BITMASK_PAYLOAD)

// Initialize LED array for our FastLED type to hold 3 RGB bytes
// When we call 'FastLED.show()', the code reads through this array and transmits the RGBs using the LED_TYPE protocol
CRGB leds[NUM_LEDS];

// Binary receive buffer
uint8_t binBuffer[MAX_PAYLOAD];   // Empty bucket for incoming binary bytes
int binBytesReceived = 0;         // Counter: how many binary bytes so far?
int binBytesExpected = 0;         // How many binary bytes we're waiting for
bool awaitingBinary = false;      // Are we currently collecting binary data?
uint8_t binaryMode = 0;           // Which header byte triggered this binary collection?

// ASCII command buffer
char cmdBuf[64];                  // Empty bucket for incoming ASCII characters
int cmdIdx = 0;                   // Counter: how many ASCII characters so far?


// SETUP
void setup() {
  Serial.begin(BAUD_RATE);          // Open serial at predefined Baud
  FastLED.addLeds<LED_TYPE, LED_PIN, COLOR_ORDER>(leds, NUM_LEDS); // Tell FastLED which pin, LED type, and how many
  FastLED.clear();                  // Clear all LEDs
  FastLED.show();                   // Push the clear-all-leds to the arena to black out everything
  Serial.println("ARDUINO :: Ready!"); // Broadcast a message that we're ready
}

// LOOP
void loop() {
  while (Serial.available()) {
    uint8_t b = Serial.read();

    if (awaitingBinary) {
      binBuffer[binBytesReceived++] = b;

      if (binBytesReceived >= binBytesExpected) {
        // Dispatch based on which header started this collection
        switch (binaryMode) {
          case HEADER_BITMASK:
            loadBitmask();
            FastLED.show();
            Serial.println("ARDUINO :: MSG - Bitmask (immediate)");
            break;

          case HEADER_BITMASK_STAGED:
            loadBitmask();
            Serial.println("ARDUINO :: MSG - Bitmask (staged)");
            break;

          case HEADER_FRAME:
            loadFullFrame();
            FastLED.show();
            Serial.println("ARDUINO :: MSG - Full-Frame (immediate)");
            break;

          case HEADER_FRAME_STAGED:
            loadFullFrame();
            Serial.println("ARDUINO :: MSG - Full-Frame (staged)");
            break;
        }

        awaitingBinary = false;
        binBytesReceived = 0;
      }
    }
    // Check for any of the four binary headers
    else if (b == HEADER_BITMASK || b == HEADER_BITMASK_STAGED) {
      awaitingBinary = true;
      binaryMode = b;
      binBytesExpected = BITMASK_PAYLOAD;
      binBytesReceived = 0;
    }
    else if (b == HEADER_FRAME || b == HEADER_FRAME_STAGED) {
      awaitingBinary = true;
      binaryMode = b;
      binBytesExpected = FRAME_PAYLOAD;
      binBytesReceived = 0;
    }
    else if (b == '\r') {
      // Ignore carriage returns
    }
    else if (b == '\n') {
      cmdBuf[cmdIdx] = '\0';
      applyASCII(cmdBuf);
      cmdIdx = 0;
    }
    else {
      if (cmdIdx < (int)sizeof(cmdBuf) - 1) {
        cmdBuf[cmdIdx++] = (char)b;
      }
    }
  }
}


// ── Local functions ──

// Load bitmask data into the leds[] array (does not call show)
// MASK_BYTES of bitmask followed by 3 bytes shared RGB.
// Bit 0 of byte 0 = LED 0, bit 1 of byte 0 = LED 1, etc.
// bit=1 → set to (R,G,B), bit=0 → set to black.
void loadBitmask() {
  uint8_t r = binBuffer[MASK_BYTES];
  uint8_t g = binBuffer[MASK_BYTES + 1];
  uint8_t b = binBuffer[MASK_BYTES + 2];

  for (int i = 0; i < NUM_LEDS; i++) {
    int byteIndex = i / 8;
    int bitIndex = i % 8;

    if (binBuffer[byteIndex] & (1 << bitIndex)) {
      leds[i] = CRGB(r, g, b);
    } else {
      leds[i] = CRGB::Black;
    }
  }
}

// Load full-frame data into the leds[] array (does not call show)
// 3 bytes per LED in sequence: LED0_R, LED0_G, LED0_B, LED1_R, LED1_G, LED1_B, ...
void loadFullFrame() {
  for (int i = 0; i < NUM_LEDS; i++) {
    leds[i].r = binBuffer[i * 3];
    leds[i].g = binBuffer[i * 3 + 1];
    leds[i].b = binBuffer[i * 3 + 2];
  }
}

// ── ASCII mode ──
// Immediate commands:
//   L,id,r,g,b    — light one LED with a specific color (loads + shows)
//   O,id          — turn off one LED (loads + shows)
//   X             — turn off all LEDs (clears + shows)
//
// Staged commands:
//   S,id,r,g,b    — stage one LED with a specific color (loads only, no show)
//   C             — clear the buffer (sets all LEDs to black, no show)
//
// Trigger:
//   T             — calls show() to display whatever is currently in the leds[] buffer
void applyASCII(const char* cmd) {
  if (cmd[0] == 'L') {
    int id, r, g, b;
    if (sscanf(cmd, "L,%d,%d,%d,%d", &id, &r, &g, &b) == 4) {
      if (id >= 0 && id < NUM_LEDS) {
        leds[id] = CRGB(r, g, b);
        FastLED.show();
        Serial.print("ARDUINO :: MSG - Light LED ");
        Serial.println(id);
      }
    }
  }
  else if (cmd[0] == 'S') {
    int id, r, g, b;
    if (sscanf(cmd, "S,%d,%d,%d,%d", &id, &r, &g, &b) == 4) {
      if (id >= 0 && id < NUM_LEDS) {
        leds[id] = CRGB(r, g, b);
        Serial.print("ARDUINO :: MSG - Stage LED ");
        Serial.println(id);
      }
    }
  }
  else if (cmd[0] == 'O') {
    int id;
    if (sscanf(cmd, "O,%d", &id) == 1) {
      if (id >= 0 && id < NUM_LEDS) {
        leds[id] = CRGB::Black;
        FastLED.show();
        Serial.print("ARDUINO :: MSG - Turn off LED ");
        Serial.println(id);
      }
    }
  }
  else if (cmd[0] == 'X') {
    FastLED.clear();
    FastLED.show();
    Serial.println("ARDUINO :: MSG - Full blackout");
  }
  else if (cmd[0] == 'C') {
    FastLED.clear();
    Serial.println("ARDUINO :: MSG - Buffer cleared (not shown)");
  }
  else if (cmd[0] == 'T') {
    FastLED.show();
    Serial.println("ARDUINO :: MSG - Trigger show");
  }
}