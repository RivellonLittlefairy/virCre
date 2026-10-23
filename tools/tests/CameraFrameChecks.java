import com.example.vircam.core.FrameProtocol;
import com.example.vircam.core.LatestFrame;
import com.example.vircam.core.TextureRevision;
import com.example.vircam.core.DesktopFrameRenderer;
import java.io.*;
import java.nio.file.*;
import java.util.Arrays;

public final class CameraFrameChecks {
    static final String SESSION="00112233445566778899aabbccddeeff";
    static void check(boolean value,String message) {if(!value)throw new AssertionError(message);}
    public static void main(String[] args) throws Exception {
        timestampRaceChecks();
        holdWireChecks();
        holdStateChecks();
        ByteArrayOutputStream output=new ByteArrayOutputStream();
        FrameProtocol.write(output,FrameProtocol.control(6,SESSION,9,7));
        byte[] repeat=output.toByteArray();
        check(repeat.length==64 && repeat[7]==6 && repeat[39]==7,"Repeat header layout");
        FrameProtocol.Packet packet=FrameProtocol.read(new ByteArrayInputStream(repeat),SESSION,9);
        check(packet.type==6 && packet.sequence==7 && packet.payload.length==0,"Repeat wire roundtrip");
        if(args.length==2 && args[0].equals("--read-repeat"))check(Arrays.equals(Files.readAllBytes(Paths.get(args[1])),repeat),"C#/Java Repeat mismatch");
        if(args.length==2 && args[0].equals("--write-repeat"))Files.write(Paths.get(args[1]),repeat);
        System.out.println("PASS: Java Repeat v1 wire vector");
        ownershipChecks();
        blockedPresentationChecks();
        TextureRevision texture=new TextureRevision();
        check(texture.update(1,1440,2560)==2,"First frame must allocate texture");
        check(texture.update(1,1440,2560)==0,"Repeat uploaded texture again");
        check(texture.update(2,1440,2560)==1,"Reused Bitmap with new revision must update storage");
        check(texture.update(3,720,1280)==2,"Changed dimensions must reallocate");
        System.out.println("PASS: texture storage/revision reuse");
    }
    static void timestampRaceChecks() throws Exception {
        LatestFrame<String> frames=new LatestFrame<>();frames.offer(1,9900,"pixels");
        java.util.concurrent.CountDownLatch begin=new java.util.concurrent.CountDownLatch(1),updated=new java.util.concurrent.CountDownLatch(1);
        java.util.concurrent.atomic.AtomicBoolean accepted=new java.util.concurrent.atomic.AtomicBoolean();
        Thread receiver=new Thread(()->{try {begin.await();accepted.set(frames.repeat(2,10001));}catch(InterruptedException e){throw new RuntimeException(e);}finally {updated.countDown();}});
        receiver.start();
        java.util.concurrent.atomic.AtomicBoolean visible=new java.util.concurrent.atomic.AtomicBoolean();
        DesktopFrameRenderer.Target<String> target=new DesktopFrameRenderer.Target<String>() {
            public void beginDesktop() {begin.countDown();try {check(updated.await(2,java.util.concurrent.TimeUnit.SECONDS),"Repeat stalled");}catch(InterruptedException e){throw new RuntimeException(e);}}
            public void uploadDesktop(String image,long revision) {visible.set("pixels".equals(image) && revision==1);}
            public void presentDesktop() {}
        };
        DesktopFrameRenderer.render(frames,10000,1500,target,input->{});receiver.join(2000);
        check(accepted.get() && visible.get(),"Old render timestamp rejected a newly refreshed frame");
        check(frames.snapshot(10000,1500)!=null,"Freshness query falsely rejected frame received after caller sampled now");
        check(frames.snapshot(11502,1500)==null,"Freshness still must expire independently of holding policy");
        frames.close();System.out.println("PASS: production render concurrent timestamp race independently of hold");
    }
    static void holdWireChecks() throws Exception {
        byte[] vector=hex("56435231000100070000000000112233445566778899aabbccddeeff000000090000000000000007000000000000000000000000000000000000000000000000");
        FrameProtocol.Packet held=FrameProtocol.read(new ByteArrayInputStream(vector),SESSION,9);
        check(held.type==7 && held.sequence==7 && held.payload.length==0,"Hold fixture rejected");
        ByteArrayOutputStream output=new ByteArrayOutputStream();FrameProtocol.write(output,held);
        check(Arrays.equals(vector,output.toByteArray()),"Hold v1 cross-language control vector changed");
        for(int offset:new int[]{8,48,52,56,60}) {byte[] bad=vector.clone();bad[offset+3]=1;try {FrameProtocol.read(new ByteArrayInputStream(bad),SESSION,9);throw new AssertionError("Hold accepted invalid control field "+offset);}catch(IOException expected){}}
        byte[] zero=vector.clone();zero[39]=0;try {FrameProtocol.read(new ByteArrayInputStream(zero),SESSION,9);throw new AssertionError("Zero Hold seq accepted");}catch(IOException expected){}
        System.out.println("PASS: Hold v1 literal wire and strict empty control fields");
    }
    static byte[] hex(String value) {byte[] bytes=new byte[value.length()/2];for(int i=0;i<bytes.length;i++)bytes[i]=(byte)Integer.parseInt(value.substring(2*i,2*i+2),16);return bytes;}
    static void holdStateChecks() throws Exception {
        LatestFrame<String> frames=new LatestFrame<>();
        final String[] uploaded={"unset"};
        DesktopFrameRenderer.Target<String> target=new DesktopFrameRenderer.Target<String>() {
            public void beginDesktop() {}
            public void uploadDesktop(String image,long revision) {uploaded[0]=image;}
            public void presentDesktop() {}
        };
        frames.hold(1);DesktopFrameRenderer.renderHolding(frames,50000,target,input->{});
        check(uploaded[0]==null,"Hold before first frame invented an image");
        frames.offer(2,100,"A");DesktopFrameRenderer.renderHolding(frames,50000,target,input->{});
        check("A".equals(uploaded[0]),"Automatic input gap lost last frame");
        check(frames.snapshot(50000,1500)==null,"Holding bypassed independent freshness expiry");
        frames.beginConnection();
        check(!frames.repeat(1,50001),"Reconnect granted Repeat before complete frame");
        check(frames.hold(1),"Reconnect Hold rejected");
        DesktopFrameRenderer.renderHolding(frames,50001,target,input->{});
        check("A".equals(uploaded[0]),"Input reconnect cleared last frame");
        check(frames.offer(2,50002,"B") && frames.repeat(3,50003),"Complete Frame failed to enable Repeat after reconnect");
        DesktopFrameRenderer.renderHolding(frames,50003,target,input->{});check("B".equals(uploaded[0]),"Recovered full frame not visible");
        frames.pause();frames.hold(4);DesktopFrameRenderer.renderHolding(frames,50004,target,input->{});
        check(uploaded[0]==null && !frames.repeat(5,50005),"Hold restored manually paused/spare image");
        frames.beginConnection();frames.hold(1);DesktopFrameRenderer.renderHolding(frames,50006,target,input->{});
        check(uploaded[0]==null,"Reconnect revived paused image");
        LatestFrame<String> other=new LatestFrame<>();other.hold(1);DesktopFrameRenderer.renderHolding(other,50000,target,input->{});
        check(uploaded[0]==null,"New session inherited old image");
        other.close();frames.close();
        System.out.println("PASS: automatic hold/expiry/reconnect/full-frame gate/manual pause/stop/new session/first frame");
    }
    static void ownershipChecks() throws Exception {
        java.util.List<String> disposed=new java.util.ArrayList<>();
        LatestFrame<String> frames=new LatestFrame<>(disposed::add);
        check(!frames.repeat(1,0),"Repeat before first frame accepted");
        check(frames.offer(1,100,"A"),"First frame rejected");
        check(frames.repeat(2,1500),"Static Repeat rejected");
        check(frames.snapshot(3000,1500)!=null && frames.snapshot(3001,1500)==null,"Repeat freshness boundary incorrect");
        check(frames.snapshot(1500,1500).revision==1,"Repeat changed content revision");
        check(!frames.repeat(2,1500),"Replay Repeat accepted");
        check(frames.offer(3,1600,"B"),"Second frame rejected");
        check("A".equals(frames.takeReusable()),"Old frame not reusable");
        java.util.concurrent.CountDownLatch entered=new java.util.concurrent.CountDownLatch(1),release=new java.util.concurrent.CountDownLatch(1),offered=new java.util.concurrent.CountDownLatch(1);
        Thread reader=new Thread(()->frames.use(1600,1500,s->{entered.countDown();try {release.await();}catch(InterruptedException e){throw new RuntimeException(e);}}));
        reader.setDaemon(true);reader.start();check(entered.await(2,java.util.concurrent.TimeUnit.SECONDS),"Reader did not acquire current frame");
        Thread writer=new Thread(()->{frames.offer(4,1700,"A");offered.countDown();});writer.start();
        try {check(!offered.await(50,java.util.concurrent.TimeUnit.MILLISECONDS),"Writer replaced a GL-owned frame");}
        finally {release.countDown();reader.join(2000);writer.join(2000);}
        check(offered.getCount()==0,"Frame writer did not resume");
        long revision=frames.snapshot(1700,1500).revision;
        frames.pause();check(!frames.repeat(5,1800),"Repeat after pause accepted");
        frames.resetConnection();check(!frames.repeat(1,1900),"Repeat survived reconnect");
        check(frames.offer(1,2000,"C") && frames.snapshot(2000,1500).revision>revision,"Reconnect reused old texture revision");
        frames.close();check(!frames.offer(2,2100,"D"),"Closed receiver accepted frame");
        check(disposed.contains("B") && disposed.contains("C") && disposed.contains("D"),"Buffer cleanup/rejected offer leaked ownership");
        System.out.println("PASS: reusable ownership, GL exclusion, Repeat TTL/replay/pause/reset/close");
    }
    static void blockedPresentationChecks() throws Exception {
        blockedStage("present");blockedStage("log");
        System.out.println("PASS: production render orchestration releases pixels before blocked presentation/logging");
    }
    static void blockedStage(String blocked) throws Exception {
        LatestFrame<String> frames=new LatestFrame<>();frames.offer(1,0,"pixels");
        java.util.concurrent.CountDownLatch presenting=new java.util.concurrent.CountDownLatch(1),release=new java.util.concurrent.CountDownLatch(1),closed=new java.util.concurrent.CountDownLatch(1);
        Runnable block=()->{
            presenting.countDown();try {release.await();}catch(InterruptedException e){throw new RuntimeException(e);}
        };
        java.util.List<String> stages=new java.util.ArrayList<>();
        DesktopFrameRenderer.Target<String> target=new DesktopFrameRenderer.Target<String>() {
            public void beginDesktop() {stages.add("begin");}
            public void uploadDesktop(String pixels,long revision) {check("pixels".equals(pixels) && revision==1,"Upload lost owned pixels/revision");stages.add("upload");}
            public void presentDesktop() {stages.add("present");if(blocked.equals("present"))block.run();}
        };
        Thread renderer=new Thread(()->DesktopFrameRenderer.render(frames,0,1500,target,hasInput->{
            check(hasInput,"Production renderer lost first-frame input state");stages.add("log");if(blocked.equals("log"))block.run();
        }));
        renderer.setDaemon(true);renderer.start();
        check(presenting.await(2,java.util.concurrent.TimeUnit.SECONDS),"Presentation did not start");
        Thread cleanup=new Thread(()->{frames.pause();frames.close();closed.countDown();});cleanup.setDaemon(true);cleanup.start();
        try {check(closed.await(500,java.util.concurrent.TimeUnit.MILLISECONDS),"Blocked "+blocked+" prevented pause/close");}
        finally {release.countDown();renderer.join(2000);cleanup.join(2000);}
        check(stages.equals(Arrays.asList("begin","upload","present","log")),"Production render stages changed order");
    }
}
