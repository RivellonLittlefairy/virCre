using System.Globalization;
using System.Text.RegularExpressions;

namespace VirCre.Camera;

/// <summary>CDM command arguments and association listings for Android 16 and 17.</summary>
internal static class CompanionAssociationProtocol {
    private const string Package="com.android.shell";
    private const string Profile="android.app.role.COMPANION_DEVICE_APP_STREAMING";
    // Inspected Android 17 services.jar still consumes these values positionally,
    // although its onHelp() advertises --mac-address/--profile/--self-managed.
    internal static string[] Create(string mac)=>["shell","cmd","companiondevice","associate","0",Package,mac,Profile,"true"];

    internal static string[] Remove(string mac)=>["shell","cmd","companiondevice","disassociate","0",Package,mac];

    internal static int FindId(string listing,string mac) {
        foreach(var line in listing.Split('\n')) {
            var legacy=Regex.Match(line,@"^\s*(\d+)\s*\|\s*com\.android\.shell\s*\|\s*"+Regex.Escape(mac)+@"\s*$",RegexOptions.IgnoreCase);
            if(legacy.Success && PositiveId(legacy.Groups[1].Value) is >0 and var legacyId)return legacyId;
            var modern=Regex.Match(line,@"^\s*Association\{(.*)\}\s*$");
            if(!modern.Success)continue;
            var fields=modern.Groups[1].Value;
            if(Field(fields,"mUserId")!="0" || Field(fields,"mPackageName")!="'"+Package+"'" ||
                !string.Equals(Field(fields,"mDeviceMacAddress"),mac,StringComparison.OrdinalIgnoreCase))continue;
            if(PositiveId(Field(fields,"mId")) is >0 and var modernId)return modernId;
        }
        return 0;
    }

    private static string Field(string fields,string name)=>Regex.Match(fields,@"(?:^|,\s*)"+name+@"=([^,}]*)").Groups[1].Value.Trim();
    private static int PositiveId(string value)=>int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id) && id>0 ? id : 0;
}
