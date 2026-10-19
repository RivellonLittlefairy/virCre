package com.genymobile.scrcpy.control;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

public final class ImeProtocolChecks {
    private static byte[] packet(long sequence, byte[] text, int advertisedLength) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        DataOutputStream out = new DataOutputStream(bytes);
        out.writeByte(23); out.writeLong(sequence); out.writeInt(advertisedLength); out.write(text);
        return bytes.toByteArray();
    }
    private static void rejected(byte[] packet) throws IOException {
        try {
            new ControlMessageReader(new ByteArrayInputStream(packet)).read();
            throw new AssertionError("Malformed IME packet was accepted");
        } catch (IOException expected) { }
    }
    public static void main(String[] args) throws Exception {
        byte[] unicode = "你好😀".getBytes(StandardCharsets.UTF_8);
        ControlMessage msg = new ControlMessageReader(new ByteArrayInputStream(packet(42, unicode, unicode.length))).read();
        if (msg.getType() != 23 || msg.getSequence() != 42 || !Arrays.equals(msg.getText().getBytes(StandardCharsets.UTF_8), unicode)) {
            throw new AssertionError("Committed UTF-8 or sequence changed");
        }
        rejected(packet(1, new byte[0], 65537));
        rejected(packet(1, new byte[0], -1));
        rejected(packet(0, unicode, unicode.length));
        rejected(packet(1, new byte[]{(byte) 0xc0, (byte) 0xaf}, 2));
        rejected(packet(1, unicode, unicode.length + 1));
        ByteArrayOutputStream ack = new ByteArrayOutputStream();
        new DeviceMessageWriter(ack).write(DeviceMessage.createAckText(42));
        if (!Arrays.equals(ack.toByteArray(), new byte[]{3, 0,0,0,0,0,0,0,42})) {
            throw new AssertionError("Commit completion ACK changed");
        }
        System.out.println("PASS: real server parser preserves UTF-8 and rejects oversized, invalid, unsequenced or truncated commits");
    }
}
