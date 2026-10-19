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
