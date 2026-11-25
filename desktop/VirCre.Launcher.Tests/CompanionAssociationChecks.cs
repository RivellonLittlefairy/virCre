using VirCre.Camera;

static class CompanionAssociationChecks {
    public static void Run() {
        const string mac="02:11:22:33:44:55";
        Require(CompanionAssociationProtocol.Create(mac).SequenceEqual(new[]{"shell","cmd","companiondevice","associate","0","com.android.shell",mac,"android.app.role.COMPANION_DEVICE_APP_STREAMING","true"}),"Android 16 and the inspected Android 17 implementation require positional creation parameters, despite Android 17 help advertising flags");
        Require(CompanionAssociationProtocol.Remove(mac).SequenceEqual(new[]{"shell","cmd","companiondevice","disassociate","0","com.android.shell",mac}),"Both inspected implementations clean up using the positional session address");
        Require(CompanionAssociationProtocol.FindId("com.other | 1\n  42 | com.android.shell | "+mac.ToLowerInvariant()+"\r\n",mac)==42,"Legacy association rows accept case-insensitive addresses");
        var record="Association{mId=76, mUserId=0, mPackageName='com.android.shell', mDeviceMacAddress="+mac+", mDeviceProfile='android.app.role.COMPANION_DEVICE_APP_STREAMING', mSelfManaged=true, mRevoked=false, mPending=false, mMetadata=PersistableBundle[{}]}";
        Require(CompanionAssociationProtocol.FindId("Max ID: 999\n"+record,mac)==76,"Modern records use association ID rather than Max ID");
        Require(CompanionAssociationProtocol.FindId(record.Replace("mUserId=0","mUserId=10"),mac)==0,"Another Android user must not match");
        Require(CompanionAssociationProtocol.FindId(record.Replace("com.android.shell","com.other.app"),mac)==0,"Another package must not match");
        Require(CompanionAssociationProtocol.FindId(record.Replace(mac,mac+":66"),mac)==0,"Partial addresses must not match");
        Require(CompanionAssociationProtocol.FindId(record.Replace("mId=76","mId=2147483648"),mac)==0,"Invalid IDs are rejected");
        Require(CompanionAssociationProtocol.FindId("Max ID: 76\n",mac)==0,"An empty association list must not match");
        Console.WriteLine("PASS: Android 16/17 companion association commands and ownership parsing");
    }
    private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
}
