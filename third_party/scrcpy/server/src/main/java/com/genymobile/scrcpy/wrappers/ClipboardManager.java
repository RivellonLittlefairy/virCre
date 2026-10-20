// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
package com.genymobile.scrcpy.wrappers;

import com.genymobile.scrcpy.FakeContext;

import android.content.ClipData;
import android.content.Context;
import android.os.PersistableBundle;

public final class ClipboardManager {
    private static final String IME_LABEL = "virCre.ime.private";
    private final android.content.ClipboardManager manager;

    static ClipboardManager create() {
        android.content.ClipboardManager manager = (android.content.ClipboardManager) FakeContext.get().getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null) {
            // Some devices have no clipboard manager
            // <https://github.com/Genymobile/scrcpy/issues/1440>
            // <https://github.com/Genymobile/scrcpy/issues/1556>
            return null;
        }
        return new ClipboardManager(manager);
    }

    private ClipboardManager(android.content.ClipboardManager manager) {
        this.manager = manager;
    }

    public CharSequence getText() {
        ClipData clipData = manager.getPrimaryClip();
        if (clipData == null || clipData.getItemCount() == 0) {
            return null;
        }
        return clipData.getItemAt(0).getText();
    }

    public CharSequence getTextForSync() {
        ClipData clipData = manager.getPrimaryClip();
        if (clipData == null || clipData.getItemCount() == 0 || IME_LABEL.contentEquals(
                clipData.getDescription().getLabel() == null ? "" : clipData.getDescription().getLabel())) {
            return null;
        }
        return clipData.getItemAt(0).getText();
    }

    public void setImeText(String text) {
        ClipData clipData = ClipData.newPlainText(IME_LABEL, text);
        PersistableBundle extras = new PersistableBundle();
        extras.putBoolean("android.content.extra.IS_SENSITIVE", true);
        clipData.getDescription().setExtras(extras);
        manager.setPrimaryClip(clipData);
    }

    public boolean setText(CharSequence text) {
        ClipData clipData = ClipData.newPlainText(null, text);
        manager.setPrimaryClip(clipData);
        return true;
    }

    public void addPrimaryClipChangedListener(android.content.ClipboardManager.OnPrimaryClipChangedListener listener) {
        manager.addPrimaryClipChangedListener(listener);
    }
}
