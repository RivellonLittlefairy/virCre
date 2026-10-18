using System.Text.Json;
namespace VirCam.Core;

public static class SessionCompletion {
    public static void EnsureClean(int exitCode,string? resultJson) {
        if(exitCode!=0) throw new IOException("Session exited with code "+exitCode);
        if(resultJson==null) throw new IOException("Cleanup result is missing");
        try {
            using var result=JsonDocument.Parse(resultJson);
            var status=result.RootElement.GetProperty("status").GetString();
            var errors=result.RootElement.GetProperty("cleanupErrors");
            if(status is not ("stopped" or "desktop-session-ended" or "input-path-verified") ||
                errors.ValueKind!=JsonValueKind.Array || errors.GetArrayLength()>0)
                throw new IOException("Session cleanup failed");
        } catch(Exception e) when(e is JsonException || e is KeyNotFoundException || e is InvalidOperationException) {
            throw new IOException("Invalid cleanup result",e);
        }
    }
}
