/*
 *
 * Egg-Printer ArduinoDriver Serial Protocol - Arduino Listener.
 * Version 1.0
 */

#include <Servo.h>

const long BAUD_RATE = 115200;
const unsigned int SYNC_TIMEOUT = 500;
const unsigned int READ_REQUESTPAYLOAD_TIMEOUT = 2000;
const unsigned int SYNC_REQUEST_LENGTH  = 4;
const unsigned int CMD_HANDSHAKE_LENGTH = 3;

const byte START_OF_REQUEST_MARKER    = 0xfb;
const byte ALL_BYTES_WRITTEN          = 0xfa;
const byte START_OF_RESPONSE_MARKER   = 0xf9;
const byte ERROR_MARKER               = 0xef;

const unsigned int PROTOCOL_VERSION_MAJOR = 1;
const unsigned int PROTOCOL_VERSION_MINOR = 0;

const byte CMD_HANDSHAKE_INITIATE     = 0x01;
const byte ACK_HANDSHAKE_INITIATE     = 0x02;

const byte CMD_BEGIN                  = 0x03;
const byte ACK_BEGIN                  = 0x04;
const byte CMD_END                    = 0x05;
const byte ACK_END                    = 0x06;
const byte CMD_PEN                    = 0x07;
const byte ACK_PEN                    = 0x08;
const byte CMD_MOVE                   = 0x09;
const byte ACK_MOVE                   = 0x0a;
const byte CMD_DOT                    = 0x0b;
const byte ACK_DOT                    = 0x0c;
const byte CMD_LINE                   = 0x0d;
const byte ACK_LINE                   = 0x0e;

byte data[64];
byte commandByte, lengthByte, syncByte, fletcherByte1, fletcherByte2;
unsigned int fletcher16, f0, f1, c0, c1;

/* Steppers */
const int PIN_ENABLE_X    = 12;
const int PIN_DIRECTION_X = 4;
const int PIN_STEP_X      = 5;

const int PIN_ENABLE_Y    = 13;
const int PIN_DIRECTION_Y = 6;
const int PIN_STEP_Y      = 7;

/* Pen servo */
Servo penServo;
const int PIN_PEN_SERVO   = 3;
bool penDown = false;

/* Canvas */
const int CANVAS_WIDTH    = 1600;
const int CANVAS_HEIGHT   = 420;
const int CANVAS_ORIGIN_X = 0;
const int CANVAS_ORIGIN_Y = CANVAS_HEIGHT / 2;
int penX = CANVAS_ORIGIN_X;
int penY = CANVAS_ORIGIN_Y;

void setup() {
    pinMode(PIN_ENABLE_X,    OUTPUT);
    pinMode(PIN_DIRECTION_X, OUTPUT);
    pinMode(PIN_STEP_X,      OUTPUT);
    pinMode(PIN_ENABLE_Y,    OUTPUT);
    pinMode(PIN_DIRECTION_Y, OUTPUT);
    pinMode(PIN_STEP_Y,      OUTPUT);

    digitalWrite(PIN_ENABLE_X, HIGH);
    digitalWrite(PIN_ENABLE_Y, HIGH);
    digitalWrite(PIN_DIRECTION_X, LOW);
    digitalWrite(PIN_DIRECTION_Y, LOW);

    penServo.attach(PIN_PEN_SERVO);
    setPen(false);

    Serial.begin(BAUD_RATE);
    while (!Serial) { ; }
}

void loop() {
    // Reset variables
    memset(data, 0, sizeof(data));

    // Try to acquire SYNC. Try to read up to four bytes, and only advance if the pattern matches FE ED BA BE.
    // Blocking (the client must retry if getting sync fails).
    while (Serial.available() < SYNC_REQUEST_LENGTH) { ; }
    if ((syncByte = Serial.read()) != 0xFE) return;
    if ((syncByte = Serial.read()) != 0xED) return;
    if ((syncByte = Serial.read()) != 0xBA) return;
    if ((syncByte = Serial.read()) != 0xBE) return;

    // Write out SYNC ACK (CA FE F0 0D).
    Serial.write(0xCA);
    Serial.write(0xFE);
    Serial.write(0xF0);
    Serial.write(0x0D);
    Serial.flush();

    // Now expect the START_OF_REQUEST_MARKER (0xfb), followed by our command byte, and a length byte
    // To acknowledge, we will write out the sequence in reverse (length byte, command byte, START_OF_REQUEST_MARKER)
    // We cannot be blocking as the client expects an answer.
    if (!expectNumberOfBytesToArrive(CMD_HANDSHAKE_LENGTH, SYNC_TIMEOUT)) return;

    if (Serial.read() != START_OF_REQUEST_MARKER) return;
    commandByte = Serial.read();
    lengthByte = Serial.read();

    // Write out acknowledgement.
    Serial.write(lengthByte);
    Serial.write(commandByte);
    Serial.write(START_OF_REQUEST_MARKER);
    Serial.flush();

    // Read length bytes + 4 (first two bytes are commandByte + length repeated, last two bytes are fletcher16 checksums)
    if (!expectNumberOfBytesToArrive(lengthByte + 4, READ_REQUESTPAYLOAD_TIMEOUT)) return;
    for (int i = 0; i < lengthByte + 4; i++) { data[i] = Serial.read(); }

    fletcherByte1 = data[lengthByte + 2];
    fletcherByte2 = data[lengthByte + 3];

    // Expect all bytes written package to come in (0xfa), non blocking
    if (!expectNumberOfBytesToArrive(1, READ_REQUESTPAYLOAD_TIMEOUT)) return;
    if (Serial.read() != ALL_BYTES_WRITTEN) return;

    // Packet checks: do fletcher16 checksum!
    fletcher16 = Fletcher16(data, lengthByte + 2);
    f0 = fletcher16 & 0xff;
    f1 = (fletcher16 >> 8) & 0xff;
    c0 = 0xff - (( f0 + f1) % 0xff);
    c1 = 0xff - (( f0 + c0 ) % 0xff);

    // Sanity check of checksum + command and length values, so that we can trust the entire packet.
    if (c0 != fletcherByte1 || c1 != fletcherByte2 || commandByte != data[0] || lengthByte != data[1]) {
        WriteError();
        return;
    }

    switch (commandByte) {
        case CMD_HANDSHAKE_INITIATE: {
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(3);
            Serial.write(ACK_HANDSHAKE_INITIATE);
            Serial.write(PROTOCOL_VERSION_MAJOR);
            Serial.write(PROTOCOL_VERSION_MINOR);
            Serial.flush();
        }
        break;
        case CMD_BEGIN: {
            digitalWrite(PIN_ENABLE_X, LOW);
            digitalWrite(PIN_ENABLE_Y, LOW);
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(1);
            Serial.write(ACK_BEGIN);
            Serial.flush();
        }
        break;
        case CMD_END: {
            setPen(false);
            moveTo(0, 0);
            digitalWrite(PIN_ENABLE_X, HIGH);
            digitalWrite(PIN_ENABLE_Y, HIGH);
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(1);
            Serial.write(ACK_END);
            Serial.flush();
        }
        break;
        case CMD_PEN: {
            setPen(data[2] != 0);
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(2);
            Serial.write(ACK_PEN);
            Serial.write(penDown);
            Serial.flush();
        }
        break;
        case CMD_MOVE: {
            int numberOfPoints = data[1] / 4;
            for (int point = 0; point < numberOfPoints; point++) {
                int index = point * 4 + 2;
                int x = (data[index + 0] << 8) | data[index + 1];
                int y = (data[index + 2] << 8) | data[index + 3];
                moveTo(x, y);
            }
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(2);
            Serial.write(ACK_MOVE);
            Serial.write(numberOfPoints);
            Serial.flush();
        }
        break;
        case CMD_DOT: {
            setPen(false);
            int dotX = (data[2] << 8) | data[3];
            int dotY = (data[4] << 8) | data[5];
            moveTo(dotX, dotY);
            setPen(true);
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(1);
            Serial.write(ACK_DOT);
            Serial.flush();
        }
        break;
        case CMD_LINE: {
            setPen(false);
            int lineFromX = (data[2] << 8) | data[3];
            int lineFromY = (data[4] << 8) | data[5];
            moveTo(lineFromX, lineFromY);
            setPen(true);
            int lineToX = (data[6] << 8) | data[7];
            int lineToY = (data[8] << 8) | data[9];
            moveTo(lineToX, lineToY);
            Serial.write(START_OF_RESPONSE_MARKER);
            Serial.write(1);
            Serial.write(ACK_LINE);
            Serial.flush();
        }
        break;
        default:
            WriteError();
            break;
    }
}

void setPen(bool state) {
    penDown = state;
    if (penDown) {
        penServo.write(140);
    }
    else {
        penServo.write(170);
    }
    // Wait for servo done it's job.
    delay(200);
}

void moveTo(int x, int y) {
    if (penDown) {
        lineTo(x, y);
    }
    else {
        flyTo(x, y);
    }
}

void lineTo(int x, int y) {
    x = x + CANVAS_ORIGIN_X;
    y = constrain(y + CANVAS_ORIGIN_Y, 0, CANVAS_HEIGHT - 1);

    int dx = x - penX;
    int dy = y - penY;
    setStepperDirection(dx, dy);
    dx = abs(dx);
    dy = abs(dy);

    int longer;
    int shorter;
    int pinLonger;
    int pinShorter;
    if (dx > dy) {
        longer = dx;
        shorter = dy;
        pinLonger = PIN_STEP_X;
        pinShorter = PIN_STEP_Y;
    }
    else {
        longer = dy;
        shorter = dx;
        pinLonger = PIN_STEP_Y;
        pinShorter = PIN_STEP_X;
    }

    int diff = 2 * shorter - longer;
    int s = 0;
    for (int l = 0; l < longer; l++) {
        digitalWrite(pinLonger, HIGH);
        if (diff > 0) {
            digitalWrite(pinShorter, HIGH);
        }
        delay(2);
        digitalWrite(pinLonger, LOW);
        if (diff > 0) {
            digitalWrite(pinShorter, LOW);
            diff -= 2 * longer;
        }
        delay(2);
        diff += 2 * shorter;
    }
    penX = x;
    penY = y;
}

void flyTo(int x, int y) {
    x = x + CANVAS_ORIGIN_X;
    y = constrain(y + CANVAS_ORIGIN_Y, 0, CANVAS_HEIGHT - 1);

    int dx = (x - penX) % CANVAS_WIDTH;
    if (abs(dx) > CANVAS_WIDTH / 2) {
        if (dx > 0) {
            dx -= CANVAS_WIDTH;
        }
        else {
            dx += CANVAS_WIDTH;
        }
    }
    int dy = y - penY;
    setStepperDirection(dx, dy);
    dx = abs(dx);
    dy = abs(dy);
    int steps = max(dx, dy);
    for (int s = 0; s < steps; s++) {
        if (s < dx) {
            digitalWrite(PIN_STEP_X, HIGH);
        }
        if (s < dy) {
            digitalWrite(PIN_STEP_Y, HIGH);
        }
        delay(2);
        if (s < dx) {
            digitalWrite(PIN_STEP_X, LOW);
        }
        if (s < dy) {
            digitalWrite(PIN_STEP_Y, LOW);
        }
        delay(2);
    }
    penX = x;
    penY = y;
}

void setStepperDirection(int dx, int dy) {
    if (dx < 0) {
        digitalWrite(PIN_DIRECTION_X, LOW);
    }
    else {
        digitalWrite(PIN_DIRECTION_X, HIGH);
    }
    if (dy < 0) {
        digitalWrite(PIN_DIRECTION_Y, LOW);
    }
    else {
        digitalWrite(PIN_DIRECTION_Y, HIGH);
    }
}

bool expectNumberOfBytesToArrive(byte numberOfBytes, unsigned long timeout) {
    unsigned long timeoutStartTicks = millis();
    while ((Serial.available() < numberOfBytes) && ((millis() - timeoutStartTicks) < timeout)) { ; }
    if (Serial.available() < numberOfBytes) {
        // Unable to get sufficient bytes, perhaps one was lost in transportation? Write out three error marker bytes.
        WriteError();
        return false;
    }
    return true;
}

void WriteError() {
    for (int i = 0; i < 3; i++) { Serial.write(ERROR_MARKER); }
    Serial.flush();
}

unsigned int Fletcher16(byte data[], int count ) {
    unsigned int sum1 = 0;
    unsigned int sum2 = 0;
    unsigned int idx;
    for (idx = 0; idx < count; ++idx) {
        sum1 = (sum1 + data[idx]) % 255;
        sum2 = (sum2 + sum1) % 255;
    }
    return (sum2 << 8) | sum1;
}
