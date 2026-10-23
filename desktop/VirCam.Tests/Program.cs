using VirCam.Core;
using System.Buffers.Binary;

var session = "00112233445566778899aabbccddeeff";
byte[] Header(int type = 1, int length = 0) {
    var value = Convert.FromHexString("56435231000100010000000000112233445566778899AABBCCDDEEFF000000090000000000000000000000000000000000000000000000000000000000000000");
    BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(6), (ushort)type);
    BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(8), length);
    return value;
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Reject(Action action, string message) { try { action(); } catch (InvalidDataException) { return; } throw new Exception(message); }
byte[] FrameHeader(int length) {
    var value=Header(2,length);
    BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(32),1);
    BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(48),1440);
    BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(52),2560);
    BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(60),2);
    return value;
}
var hello = FrameProtocol.Decode(Header(), session, 9);
Check(hello.Sequence == 0 && hello.Type == MessageType.Hello, "Independent HELLO vector");
Check(FrameProtocol.Encode(hello).SequenceEqual(Header()), "Big endian encoding must match the independent vector");
Reject(() => FrameProtocol.Decode(Header(), session, 10), "Wrong device accepted");
Reject(() => FrameProtocol.Decode(Header(), new string('0',32), 9), "Wrong session accepted");
Reject(() => FrameProtocol.Decode(Header()[..63], session, 9), "Truncated header accepted");
Reject(() => FrameProtocol.Decode(Header(2, -1), session, 9), "Negative length accepted");
Check(FrameProtocol.Decode(FrameHeader(1048577),session,9).Payload.Length==1048577,"The former 1 MiB+1 boundary is now legal");
Check(FrameProtocol.Decode(FrameHeader(FrameProtocol.MaxPayload),session,9).Payload.Length==4194304,"Exactly 4 MiB must be legal");
Reject(() => FrameProtocol.Decode(FrameHeader(FrameProtocol.MaxPayload+1),session,9),"Oversized payload accepted");
var badVersion = Header(); badVersion[5] = 2;
Reject(() => FrameProtocol.Decode(badVersion, session, 9), "Wrong version accepted");
Console.WriteLine("PASS: cross-language header, identity, truncation, negative/oversized length, version");
var revision=new InputRevision();var ticket=revision.Current;var published=0;
revision.Change(()=>{});
Check(!revision.TryCommit(ticket,()=>published++),"Paused/changed input must reject old preview publication");
Check(published==0,"Old preview was published after pause");
Check(revision.TryCommit(revision.Current,()=>published++),"Current source cannot publish");
Check(published==1,"Expected exactly one current preview");
Console.WriteLine("PASS: paused/changed source cannot publish an old preview");
SessionCompletion.EnsureClean(0,"{\"status\":\"stopped\",\"cleanupErrors\":[]}");
void Dirty(Action action) {try {action();}catch(IOException){return;}throw new Exception("Dirty/missing result was accepted as a clean stop");}
Dirty(()=>SessionCompletion.EnsureClean(1,"{\"status\":\"stopped\",\"cleanupErrors\":[]}"));
Dirty(()=>SessionCompletion.EnsureClean(0,"{\"status\":\"cleanup-failed\",\"cleanupErrors\":[\"offline\"]}"));
Dirty(()=>SessionCompletion.EnsureClean(0,null));
Console.WriteLine("PASS: stop failures and missing cleanup evidence are surfaced");
if(args is not ["--protocol-only"])await CameraEfficiencyChecks.RunAsync(args);
