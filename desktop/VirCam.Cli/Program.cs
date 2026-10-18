using VirCam.Core;
using System.Text.Json;

if(args.Length<3) { Console.Error.WriteLine("Usage: VirCam.Cli connection.json imageA.png imageB.png [cycles=1]"); return 2; }
try {
    var info=ConnectionInfo.Load(args[0]);using var client=new FrameClient(info);
    await client.ConnectAsync(CancellationToken.None);
    var cycles=args.Length>3?int.Parse(args[3]):1;
    if(cycles<1 || cycles>5) throw new ArgumentException("cycles must be 1..5");
    for(int cycle=0;cycle<cycles;cycle++) {
        foreach(var path in args.Skip(1).Take(2)) {
            var png=await File.ReadAllBytesAsync(path);
            for(int i=0;i<10;i++) {
                var seq=await client.SendFrameAsync(png,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),CancellationToken.None);
                Console.WriteLine(JsonSerializer.Serialize(new{kind="sent",cycle,fixture=Path.GetFileName(path),sequence=seq}));
                await Task.Delay(200);
            }
            // Keep the connection authenticated but empty long enough to verify old QR disappearance.
            for(int i=0;i<10;i++) { await client.PauseAsync(CancellationToken.None);await Task.Delay(200); }
        }
    }
    Console.WriteLine("DONE: image switches and pause cycles acknowledged");return 0;
} catch(Exception e) { Console.Error.WriteLine(e.Message);return 1; }
