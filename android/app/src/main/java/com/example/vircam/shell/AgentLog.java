package com.example.vircam.shell;

import android.os.SystemClock;
import android.util.Log;
import org.json.JSONObject;

final class AgentLog {
    private AgentLog() {}
    static synchronized void event(String event, Object... pairs) {
        try {
            JSONObject json = new JSONObject().put("event", event).put("elapsedMs", SystemClock.elapsedRealtime());
            for (int i = 0; i < pairs.length; i += 2) json.put(pairs[i].toString(), pairs[i + 1]);
            System.out.println(json);
            System.out.flush();
            Log.i("VirCreAgent", json.toString());
        } catch (Exception e) { throw new IllegalStateException("Cannot encode diagnostic log", e); }
    }
}
