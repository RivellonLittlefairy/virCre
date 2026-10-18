package com.example.vircam.probe

import android.content.Context
import android.util.Log
import org.json.JSONObject
import java.io.File

/** Metadata only, never camera pixels. Vivo builds may suppress application logcat entries. */
object ProbeLog {
    @Synchronized fun write(context: Context, session: String, value: JSONObject) {
        val safeSession = if (session.matches(Regex("[a-f0-9]{32}"))) session else "permission-setup"
        value.put("session", safeSession)
        value.put("elapsedMs",android.os.SystemClock.elapsedRealtime())
        val line = value.toString()
        Log.i("VirCreProbe", line)
        File(context.filesDir, "p0-$safeSession.jsonl").appendText("$line\n", Charsets.UTF_8)
    }
}
