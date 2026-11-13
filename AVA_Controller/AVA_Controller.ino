// AVA_Controller.ino

//┌─────────────────────────────────────────────┐
//│  The controller sketch for the Arduino      │
//└─────────────────────────────────────────────┘
// Load this sketch onto the Arduino MEGA, so that it can properly handle
// the incoming LED messages from Bonsai. It has a bitmask mode, full-frame mode, and single-LED ASCII mode.

// @hamid

#include <FastLED.h>

#define LED_PIN     6
#define NUM_LEDS    400
#define LED_TYPE    WS2812B
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
// 1000000	  0.5 ms

// Why not just have the highest Baud rate possible at all times? Higher rates are more sensitive to electrical noise, cable length,
// and connection quality. One corrupted byte and your entire transmission will be wrong. At high rates, the Arduino may struggle processing
// the incoming data at such speed. At higher rates, there are also diminishing returns, because the latency bottleneck in your entire
// closed-loop system may not be the transmission rate, but instead be things like camera frame rate or the speed of your LED_TYPE. Finally,
// at high rates, visualizing the transmission (e.g. in Bonsai) may become difficult, meaning that debugging is made harder.

// BITMASK MODE: header + bitmask + 3 RGB bytes (to be used when all LEDs have the same color)
// Header byte (0xFE) + MASK_BYTES (bitmask) + 3 bytes (R, G, B)
// Any LED with its bit set to 1 gets the shared RGB color
// Any LED with its bit set to 0 gets turned off
#define HEADER_BITMASK 0xFE  // This tells the Arduino 'The next incoming bytes are a bitmask, not an ASCII command'
#define MASK_BYTES ((NUM_LEDS + 7) / 8)  // Each byte holds 8 bits, and each bit represents 1 LED. So, we need NUM_LEDS / 8. The +7 is to 'round up' with odd numbers of LEDs
#define BITMASK_PAYLOAD (MASK_BYTES + 3)  // The payload is the total number of bytes expected by the Arduino: bitmask + three RGB colors

// FULL-FRAME MODE: header + 3 bytes per LED (to be used when LEDs have unique colors)
// Each LED gets its own RGB color
#define HEADER_FRAME 0xFF
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
uint8_t binaryMode = 0;           // Which binary mode (bitmask or full-frame)?

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
  while (Serial.available()) { // While there are bytes available in the receive buffer..
    uint8_t b = Serial.read(); // ..read the next incoming byte.

    if (awaitingBinary) { // If we're already in the middle of receiving a (binary) message from Bonsai..
      binBuffer[binBytesReceived++] = b; // ..add that incoming byte to the message buffer.

      if (binBytesReceived >= binBytesExpected) { // Once we've received the expected number of bytes for a message..
        if (binaryMode == HEADER_BITMASK) { // ..apply the corresponding control function.
          applyBitmask();
        } else {
          applyFullFrame();
        }
        // Message transmission has ended, reset.
        awaitingBinary = false;
        binBytesReceived = 0;
      }
    }
    else if (b == HEADER_BITMASK) { // If the incoming byte is a bitmask header, we're receiving a new bitmask message
      awaitingBinary = true;
      binaryMode = HEADER_BITMASK;
      binBytesExpected = BITMASK_PAYLOAD;
      binBytesReceived = 0;
    }
    else if (b == HEADER_FRAME) {   // If the incoming byte is a full-frame header, we're receiving a new frame message
      awaitingBinary = true;
      binaryMode = HEADER_FRAME;
      binBytesExpected = FRAME_PAYLOAD;
      binBytesReceived = 0;
    }
    else if (b == '\r') { // Is the byte a carriage return?
      // Some systems send \r\n instead of just \n. We silently ignore the \r, so it doesn’t pollute the ASCII command buffer.
    }
    else if (b == '\n') { // Is the byte a newline?
      cmdBuf[cmdIdx] = '\0'; // Null-terminate the buffer to make it a proper C string, pass to parseCommand, and reset index.
      applyASCII(cmdBuf);
      cmdIdx = 0;
    }
    else { // Anything else is treated as an ASCII command
      if (cmdIdx < (int)sizeof(cmdBuf) - 1) { // If there's still space free in the buffer (prevent overflow), add the character.
        cmdBuf[cmdIdx++] = (char)b;
      }
    }
  }
}


// Local functions

// ── Bitmask mode ──
// MASK_BYTES of bitmask followed by 3 bytes shared RGB.
// Bit 0 of byte 0 = LED 0, bit 1 of byte 0 = LED 1, etc.
// bit=1 → set to (R,G,B), bit=0 → set to black.
void applyBitmask() {
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

  FastLED.show();
  Serial.println("ARDUINO :: MSG - Bitmask"); // Acknowledge message
}

// ── Full-frame mode ──
// 3 bytes per LED in sequence: LED0_R, LED0_G, LED0_B, LED1_R, LED1_G, LED1_B, ...
// Each LED gets its own individual color.
void applyFullFrame() {
  for (int i = 0; i < NUM_LEDS; i++) {
    leds[i].r = binBuffer[i * 3];
    leds[i].g = binBuffer[i * 3 + 1];
    leds[i].b = binBuffer[i * 3 + 2];
  }

  FastLED.show();
  Serial.println("ARDUINO :: MSG - Full-Frame");
}

// ── ASCII mode ──
// L,id,r,g,b    — light one LED with a specific color
// O,id          — turn off one LED
// X             — turn off all LEDs
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
}
