package com.example.vircam.core;
import org.junit.Test;
import com.google.zxing.*;
import com.google.zxing.qrcode.QRCodeWriter;
import com.google.zxing.common.BitMatrix;
import java.io.*;
import java.nio.ByteBuffer;
import java.util.zip.CRC32;
import java.util.zip.DeflaterOutputStream;
import static org.junit.Assert.*;

/** Generates non-sensitive independent PC input fixtures; these are never packed in the APK. */
public class DesktopQrFixtureTest {
    @Test public void generatesTwoDistinctDesktopQrFixtures() throws Exception {
        File root=new File("build/fixtures");assertTrue(root.isDirectory() || root.mkdirs());
        for(String letter:new String[]{"A","B"}) {
            BitMatrix matrix=new QRCodeWriter().encode("vircre-p1-"+letter,BarcodeFormat.QR_CODE,640,480);
            ByteArrayOutputStream compressed=new ByteArrayOutputStream();
            try(DeflaterOutputStream pixels=new DeflaterOutputStream(compressed)) {
                for(int y=0;y<480;y++) {pixels.write(0);for(int x=0;x<640;x++) pixels.write(matrix.get(x,y)?0:255);}
            }
            try(DataOutputStream png=new DataOutputStream(new FileOutputStream(new File(root,"qr-"+letter+".png")))) {
                png.write(new byte[]{(byte)137,80,78,71,13,10,26,10});
                chunk(png,"IHDR",ByteBuffer.allocate(13).putInt(640).putInt(480).put(new byte[]{8,0,0,0,0}).array());
                chunk(png,"IDAT",compressed.toByteArray());chunk(png,"IEND",new byte[0]);
            }
        }
    }
    private static void chunk(DataOutputStream png,String type,byte[] data) throws IOException {
        byte[] name=type.getBytes(java.nio.charset.StandardCharsets.US_ASCII);
        CRC32 crc=new CRC32();crc.update(name);crc.update(data);
        png.writeInt(data.length);png.write(name);png.write(data);png.writeInt((int)crc.getValue());
    }
}
