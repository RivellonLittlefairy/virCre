using System.Buffers.Binary;
using VirCam.Core;

static class CameraEfficiencyChecks {
    internal const string Session="00112233445566778899aabbccddeeff";
    internal static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    internal static async Task RunAsync(string[] args) {
        HoldFrameChecks.WireChecks();
        await HoldFrameChecks.ClientChecks();
        // Independent v1 wire fixture: Repeat=6, device=9, sequence=7, no payload.
        byte[] repeat=Convert.FromHexString("56435231000100060000000000112233445566778899AABBCCDDEEFF000000090000000000000007000000000000000000000000000000000000000000000000");
        var decoded=FrameProtocol.Decode(repeat,Session,9);
        Check((int)decoded.Type==6 && decoded.Sequence==7 && decoded.Payload.Length==0,"Repeat wire vector rejected");
        Check(FrameProtocol.Encode(decoded).SequenceEqual(repeat),"Repeat cross-language wire mismatch");
        var bad=(byte[])repeat.Clone();BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(48),1);
        Reject(()=>FrameProtocol.Decode(bad,Session,9),"Repeat accepted image dimensions");
        Reject(()=>FrameProtocol.Decode(repeat,Session,10),"Repeat accepted another device");
        Reject(()=>FrameProtocol.Decode(repeat,new string('0',32),9),"Repeat accepted another session");
        var frame=new Packet(MessageType.Frame,Session,9,1,0,1440,2560,0,2,new byte[4194304]);
        Check(FrameProtocol.Decode(FrameProtocol.Encode(frame),Session,9).Payload.Length==4194304,"Exactly 4 MiB must be accepted");
        Reject(()=>FrameProtocol.Encode(frame with {Payload=new byte[4194305]}),"One byte over 4 MiB accepted");
        if(args.Length==2 && args[0]=="--write-repeat")await File.WriteAllBytesAsync(args[1],repeat);
        if(args.Length==2 && args[0]=="--read-repeat")Check((await File.ReadAllBytesAsync(args[1])).SequenceEqual(repeat),"Java/C# Repeat vectors differ");
        Console.WriteLine("PASS: Repeat v1 wire, identity, control fields, exact 4 MiB boundary");
        await CacheChecks();
        HealthChecks();
        await ClientChecks();
    }
    private static async Task CacheChecks() {
        var cache=new FrameEncodingCache();int calls=0;
        Task<byte[]> Encode(float quality,CancellationToken token) {calls++;return Task.FromResult(new byte[]{1,2,3});}
        var first=await cache.GetAsync([1,2,3,4],"left=0,size=1,monitor=A",Encode,default);
        var same=await cache.GetAsync([1,2,3,4],"left=0,size=1,monitor=A",Encode,default);
        Check(calls==1 && ReferenceEquals(first,same),"Static region was encoded twice");
        await cache.GetAsync([1,2,3,4],"left=1,size=1,monitor=A",Encode,default);
        await cache.GetAsync([1,2,3,4],"left=1,size=2,monitor=A",Encode,default);
        await cache.GetAsync([1,2,3,4],"left=1,size=2,monitor=B",Encode,default);
        await cache.GetAsync([1,2,3,5],"left=1,size=2,monitor=B",Encode,default);
        Check(calls==5,"Moved/resized/changed monitor or pixels reused stale content");
        var qualities=new List<float>();
        async Task<byte[]> Oversized(float quality,CancellationToken token) {qualities.Add(quality);await Task.Yield();return new byte[4194305];}
        cache=new();
        for(int i=0;i<2;i++) {
            try {await cache.GetAsync([1],"a",Oversized,default);throw new Exception("Oversized JPEG accepted");}
            catch(JpegPayloadTooLargeException){}
        }
        Check(qualities.SequenceEqual(new[]{0.88f,0.78f,0.65f,0.50f}),"Unchanged oversized content retried or qualities differ");
        try {await cache.GetAsync([2],"a",Oversized,default);}catch(JpegPayloadTooLargeException){}
        Check(qualities.Count==8,"Changed content did not retry bounded JPEG fallback");
        cache=new();qualities.Clear();
        var boundary=await cache.GetAsync([1],"a",(q,t)=>{qualities.Add(q);return Task.FromResult(new byte[4194304]);},default);
        Check(boundary.Length==4194304 && qualities.SequenceEqual(new[]{0.88f}),"Exact boundary should not lower quality");
        cache=new();qualities.Clear();
        var reduced=await cache.GetAsync([1],"a",(q,t)=>{qualities.Add(q);return Task.FromResult(new byte[q>0.8f?4194305:10]);},default);
        Check(reduced.Length==10 && qualities.SequenceEqual(new[]{0.88f,0.78f}),"JPEG fallback did not stop at first fitting quality");
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        try {await cache.GetAsync([1],"a",Encode,cancel.Token);throw new Exception("Canceled cache reuse accepted");}catch(OperationCanceledException){}
        cache=new();using var during=new CancellationTokenSource();
        try {
            await cache.GetAsync([1],"a",(q,t)=>{if(q==0.50f)during.Cancel();throw new JpegPayloadTooLargeException();},during.Token);
            throw new Exception("Cancellation during final quality attempt ignored");
        }catch(OperationCanceledException){}
        int retry=0;
        await cache.GetAsync([1],"a",(q,t)=>{retry++;return Task.FromResult(new byte[]{7});},default);
        Check(retry==1,"Canceled encoder cached a permanent oversize failure");
        Console.WriteLine("PASS: static/geometry cache, bounded JPEG fallback/failure reuse, 4 MiB/cancellation");
    }
    private static void HealthChecks() {
        var health=new CaptureHealth(0);
        Check(!health.Observe(0,true) && !health.Observe(9999,false),"Static desktop restarted too often");
        Check(health.Observe(10000,false),"Silent capture was never probed");
        Check(!health.Observe(14999,false),"Healthy probe grace period missing");
        try {health.Observe(15000,false);throw new Exception("Silent failed probe kept renewing old frames");}catch(IOException){}
        health=new(0);health.Observe(0,true);health.Observe(10000,false);
        Check(!health.Observe(11000,true) && !health.Observe(20000,false),"Fresh probe frame did not renew health");
        health=new(0);
        try {health.Observe(5000,false);throw new Exception("Initial capture wait unbounded");}catch(IOException){}
        Console.WriteLine("PASS: static capture health, bounded probe failure/recovery and initial deadline");
    }
    private static async Task ClientChecks() {
        foreach(bool supportsRepeat in new[]{false,true}) {
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
            int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var received=new List<Packet>();
            var peer=Task.Run(async()=>{
                using var socket=await listener.AcceptTcpClientAsync();var stream=socket.GetStream();
                for(int i=0;i<5;i++) {
                    var packet=await FrameProtocol.ReadAsync(stream,Session,9,default);received.Add(packet);
                    await FrameProtocol.WriteAsync(stream,FrameProtocol.Control(MessageType.Ack,Session,9,packet.Sequence),default);
                }
            });
            try {
                using var client=new FrameClient(new(Session,port,9,1,"phone",1,1,2,supportsRepeat));
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await client.ConnectAsync(timeout.Token);
                await RejectRepeat(client,timeout.Token);
                await client.SendFrameAsync([11,22],100,timeout.Token);
                await client.RepeatFrameAsync(200,timeout.Token);
                await client.PauseAsync(timeout.Token);
                await RejectRepeat(client,timeout.Token);
                await client.SendFrameAsync([33],300,timeout.Token);
                await peer.WaitAsync(timeout.Token);
                Check(received[2].Type==(supportsRepeat?MessageType.Repeat:MessageType.Frame),"Capability gating failed");
                Check(received[2].Payload.SequenceEqual(supportsRepeat?Array.Empty<byte>():new byte[]{11,22}),"Legacy complete-frame fallback lost content");
                Check(received.Select(p=>p.Sequence).SequenceEqual(new long[]{0,1,2,3,4}),"Packet sequence/rejected-repeat ownership broken");
            } finally {listener.Stop();}
        }
        Console.WriteLine("PASS: new/legacy FrameClient, Repeat requires first frame and rejects after pause");
    }
    private static async Task RejectRepeat(FrameClient client,CancellationToken token) {
        try {await client.RepeatFrameAsync(0,token);}catch(InvalidOperationException){return;}
        throw new Exception("Repeat without a complete current frame accepted");
    }
    private static void Reject(Action action,string message) {
        try {action();}catch(InvalidDataException){return;}throw new Exception(message);
    }
}
