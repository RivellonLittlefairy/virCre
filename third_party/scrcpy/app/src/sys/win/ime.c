// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
#include "ime.h"
#include <windows.h>
#include <commctrl.h>
#include <SDL3/SDL.h>
#include "events.h"
#include "util/log.h"

static LRESULT CALLBACK
ime_window_proc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam,
                UINT_PTR subclass_id, DWORD_PTR data) {
    SDL_Window *window = (SDL_Window *) data;
    // Observe close-related categories only, without changing message handling
    // or recording arbitrary keys, pointer coordinates, or window contents.
    if (message == WM_SYSKEYDOWN && wparam == VK_F4
            && (GetKeyState(VK_MENU) & 0x8000)) {
        LOGI("{\"event\":\"client-stop\",\"reason\":\"native-alt-f4\"}");
    } else if (message == WM_NCLBUTTONUP && wparam == HTCLOSE) {
        LOGI("{\"event\":\"client-stop\",\"reason\":\"native-close-button\"}");
    }
    if (message == WM_CLOSE) {
        LOGI("{\"event\":\"client-stop\",\"reason\":\"native-close\"}");
    } else if (message == WM_SYSCOMMAND && (wparam & 0xfff0) == SC_CLOSE) {
        LASTINPUTINFO input = {.cbSize = sizeof(input)};
        const char *idle = "unknown";
        if (GetLastInputInfo(&input)) {
            DWORD elapsed = GetTickCount() - input.dwTime;
            idle = elapsed < 1000 ? "under-one-second"
                 : elapsed < 10000 ? "under-ten-seconds" : "over-ten-seconds";
        }
        // InSendMessageEx distinguishes a cross-thread send from a local or
        // posted message, but does not identify the sending process.
        const char *delivery = InSendMessageEx(NULL) != ISMEX_NOSEND
                             ? "cross-thread-send" : "local-or-posted";
        LOGI("{\"event\":\"client-stop\",\"reason\":\"native-system-close\","
             "\"delivery\":\"%s\",\"idle\":\"%s\",\"foreground\":%s}",
             delivery, idle, GetForegroundWindow() == hwnd ? "true" : "false");
    }
    if (message == WM_IME_STARTCOMPOSITION || message == WM_IME_ENDCOMPOSITION ||
            message == WM_INPUTLANGCHANGE || message == WM_KILLFOCUS) {
        // Native composition UI does not emit SDL_TEXT_EDITING. Queue only its state,
        // preserving event order without capturing any composition text.
        SDL_Event event = {0};
        event.type = SC_EVENT_IME_STATE;
        event.user.windowID = SDL_GetWindowID(window);
        event.user.code = message == WM_IME_STARTCOMPOSITION;
        if (!SDL_PushEvent(&event)) {
            LOGE("Could not route IME state");
        }
    }
    if (message == WM_NCDESTROY) {
        RemoveWindowSubclass(hwnd, ime_window_proc, subclass_id);
    }
    return DefSubclassProc(hwnd, message, wparam, lparam);
}

bool sc_windows_ime_init(SDL_Window *window) {
    HWND hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window),
        SDL_PROP_WINDOW_WIN32_HWND_POINTER, NULL);
    if (!hwnd || !SetWindowSubclass(hwnd, ime_window_proc, 1, (DWORD_PTR) window)) {
        LOGE("Could not observe native Windows IME state");
        return false;
    }
    // A stable, visible anchor. Android caret geometry is not available in the video stream.
    const SDL_Rect anchor = {.x = 12, .y = 12, .w = 1, .h = 24};
    if (!SDL_SetTextInputArea(window, &anchor, 0)) {
        LOGW("Could not position native IME UI");
    }
    return true;
}

void sc_windows_ime_destroy(SDL_Window *window) {
    HWND hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window),
        SDL_PROP_WINDOW_WIN32_HWND_POINTER, NULL);
    if (hwnd) {
        RemoveWindowSubclass(hwnd, ime_window_proc, 1);
    }
}
