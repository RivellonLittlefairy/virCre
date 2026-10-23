using System.Buffers.Binary;
using VirCam.Core;

static class HoldFrameChecks {
    internal static void WireChecks() {
        const string session="00112233445566778899aabbccddeeff";
        byte[] vector=Convert.FromHexString("56435231000100070000000000112233445566778899AABBCCDDEEFF000000090000000000000007000000000000000000000000000000000000000000000000");
        var packet=FrameProtocol.Decode(vector,session,9);
        CameraEfficiencyChecks.Check((int)packet.Type==7 && packet.Sequence==7 && packet.Payload.Length==0,"Hold control fixture rejected");
        CameraEfficiencyChecks.Check(FrameProtocol.Encode(packet).SequenceEqual(vector),"Hold literal cross-language vector mismatch");
        foreach(int offset in new[]{8,48,52,56,60}) {var bad=(byte[])vector.Clone();BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(offset),1);Reject(bad,session,9);}
        var zero=(byte[])vector.Clone();BinaryPrimitives.WriteInt64BigEndian(zero.AsSpan(32),0);Reject(zero,session,9);
        Reject(vector,session,10);Reject(vector,new string('0',32),9);
        Console.WriteLine("PASS: Hold v1 literal wire, identity, positive seq and strict empty controls");
    }
    internal static async Task ClientChecks() {
        await CapabilityFallback();await ReconnectChecks();await PauseDuringAwaitChecks();await CancellationChecks();
    }
    const string Session="00112233445566778899aabbccddeeff";
    static void Check(bool ok,string message)=>CameraEfficiencyChecks.Check(ok,message);
    static async Task<Packet> ReadAndAck(System.Net.Sockets.NetworkStream stream,CancellationToken token) {
        var packet=await FrameProtocol.ReadAsync(stream,Session,9,token);
        await FrameProtocol.WriteAsync(stream,FrameProtocol.Control(MessageType.Ack,Session,9,packet.Sequence),token);return packet;
    }
    static async Task CapabilityFallback() {
        foreach(bool hold in new[]{false,true}) {
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var peer=Task.Run(async()=>{using var socket=await listener.AcceptTcpClientAsync(timeout.Token);var stream=socket.GetStream();await ReadAndAck(stream,timeout.Token);return await ReadAndAck(stream,timeout.Token);});
            try {
                using var client=new FrameClient(new(Session,port,9,1,"phone",1,1,2,true,hold));await client.ConnectAsync(timeout.Token);await client.HoldAsync(timeout.Token);
                var packet=await peer;Check(packet.Type==(hold?MessageType.Hold:MessageType.Pause),"Hold capability fallback sent unsupported control");
            } finally {listener.Stop();}
        }
        Console.WriteLine("PASS: Hold capability true/absent legacy Pause fallback over actual TCP");
    }
    static async Task ReconnectChecks() {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var dropped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var packets=new List<Packet>();
        var peer=Task.Run(async()=>{
            using(var first=await listener.AcceptTcpClientAsync(timeout.Token))for(int i=0;i<3;i++)packets.Add(await ReadAndAck(first.GetStream(),timeout.Token));
            dropped.SetResult();
            using var second=await listener.AcceptTcpClientAsync(timeout.Token);for(int i=0;i<4;i++)packets.Add(await ReadAndAck(second.GetStream(),timeout.Token));
        });
        try {
            int notices=0;using var transport=new FrameTransport(new(Session,port,9,1,"phone",1,1,2,true,true),_=>notices++);
            await transport.ConnectAsync(timeout.Token);
            Check(await transport.SendFrameAsync([11],0,timeout.Token),"Initial frame failed");
            Check(await transport.HoldAsync(timeout.Token),"Hold failed");await dropped.Task.WaitAsync(timeout.Token);
            Check(!await transport.RepeatFrameAsync(0,timeout.Token),"Reconnected operation incorrectly reported transferred");
            Check(transport.Generation==1 && notices==1,"Transport recovery was not reported");
            try {await transport.RepeatFrameAsync(0,timeout.Token);throw new Exception("Reconnect retained old PC frame cache");}catch(InvalidOperationException){}
            Check(await transport.SendFrameAsync([22],0,timeout.Token),"Recovered complete frame failed");
            Check(await transport.PauseAsync(timeout.Token),"Manual pause failed");Check(await transport.HoldAsync(timeout.Token),"Hold after Pause failed");
            await peer;
            Check(packets.Select(p=>p.Sequence).SequenceEqual(new long[]{0,1,2,0,1,2,3}),"Reconnect sequence did not reset cleanly");
            Check(packets[4].Type==MessageType.Frame && packets[4].Payload.SequenceEqual(new byte[]{22}),"New connection did not begin with fresh complete frame");
            Check(packets[5].Type==MessageType.Pause && packets[6].Type==MessageType.Hold,"Manual Pause/Hold semantics changed");
        }finally {listener.Stop();}
        Console.WriteLine("PASS: production FrameTransport reconnect discards client/cache; complete-frame recovery and Pause priority");
    }
    static async Task PauseDuringAwaitChecks() {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var holding=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drop=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer=Task.Run(async()=>{
            using(var first=await listener.AcceptTcpClientAsync(timeout.Token)) {
                var stream=first.GetStream();await ReadAndAck(stream,timeout.Token);await ReadAndAck(stream,timeout.Token);
                var hold=await FrameProtocol.ReadAsync(stream,Session,9,timeout.Token);Check(hold.Type==MessageType.Hold,"Expected pending Hold");
                holding.SetResult();await drop.Task.WaitAsync(timeout.Token);
            }
            using var second=await listener.AcceptTcpClientAsync(timeout.Token);var next=second.GetStream();
            var hello=await ReadAndAck(next,timeout.Token);Check(hello.Type==MessageType.Hello,"Reconnect omitted HELLO");
            return await ReadAndAck(next,timeout.Token);
        });
        int manualPause=0;
        try {
            using var channel=new FrameTransport(new(Session,port,9,1,"phone",1,1,2,true,true),isPaused:()=>Volatile.Read(ref manualPause)!=0);
            await channel.ConnectAsync(timeout.Token);await channel.SendFrameAsync([1],0,timeout.Token);
            var pending=channel.HoldAsync(timeout.Token);await holding.Task.WaitAsync(timeout.Token);
            Check(!pending.IsCompleted,"ACK was not pending during pause intent");
            Interlocked.Exchange(ref manualPause,1);drop.SetResult();
            Check(!await pending,"Lost pending transfer reported success");var firstAfterHello=await peer;
            Check(firstAfterHello.Type==MessageType.Pause && firstAfterHello.Sequence==1,"Reconnect sent Frame/Hold before pending manual Pause");
        }finally {listener.Stop();}
        Console.WriteLine("PASS: pause intent during actual pending ACK; reconnect sends Pause before any Frame");
    }
    static async Task CancellationChecks() {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;using var startup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var dropped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer=Task.Run(async()=>{using(var socket=await listener.AcceptTcpClientAsync(startup.Token))await ReadAndAck(socket.GetStream(),startup.Token);listener.Stop();dropped.SetResult();});
        using var channel=new FrameTransport(new(Session,port,9,1,"phone",1,1,2,true,true));
        await channel.ConnectAsync(startup.Token);await dropped.Task.WaitAsync(startup.Token);
        using var cancel=new CancellationTokenSource(150);
        try {await channel.HoldAsync(cancel.Token);throw new Exception("Cancelled reconnect did not terminate");}catch(OperationCanceledException) when(cancel.IsCancellationRequested){}
        await peer;Console.WriteLine("PASS: reconnect delay/attempts are cancellable without device operations");
    }
    static void Reject(byte[] bytes,string session,int device) {try {FrameProtocol.Decode(bytes,session,device);}catch(InvalidDataException){return;}throw new Exception("Invalid Hold fields accepted");}
}
