package com.example.vircam.shell;

import android.os.SystemClock;
import android.util.Log;
import org.json.JSONObject;
import java.util.function.Consumer;

final class AgentLog {
    private static Consumer<String> output = line -> { System.out.println(line); System.out.flush(); };
    private AgentLog() {}
    static synchronized void setOutput(Consumer<String> sink) { output = java.util.Objects.requireNonNull(sink); }
    static synchronized void event(String event, Object... pairs) {
        try {
            JSONObject json = new JSONObject().put("event", event).put("elapsedMs", SystemClock.elapsedRealtime());
            for (int i = 0; i < pairs.length; i += 2) json.put(pairs[i].toString(), pairs[i + 1]);
            output.accept(json.toString());
            Log.i("VirCreAgent", json.toString());
        } catch (Exception e) { throw new IllegalStateException("Cannot encode diagnostic log", e); }
    }
}
