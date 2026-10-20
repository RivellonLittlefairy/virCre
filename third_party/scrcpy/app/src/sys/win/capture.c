// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
#include "capture.h"
#include <windows.h>
#include "util/log.h"

#ifndef WDA_EXCLUDEFROMCAPTURE
#define WDA_EXCLUDEFROMCAPTURE 0x00000011
#endif

bool sc_windows_exclude_from_capture(SDL_Window *window) {
    // scrcpy still targets Vista headers. Resolve newer APIs without raising
    // the platform floor for ordinary scrcpy windows.
    typedef BOOL (WINAPI *set_affinity_fn)(HWND, DWORD);
    typedef BOOL (WINAPI *get_affinity_fn)(HWND, DWORD *);
    HMODULE user32 = GetModuleHandleW(L"user32.dll");
    set_affinity_fn set_affinity = user32
        ? (set_affinity_fn) (void *) GetProcAddress(user32, "SetWindowDisplayAffinity") : NULL;
    get_affinity_fn get_affinity = user32
        ? (get_affinity_fn) (void *) GetProcAddress(user32, "GetWindowDisplayAffinity") : NULL;
    HWND hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window),
        SDL_PROP_WINDOW_WIN32_HWND_POINTER, NULL);
    DWORD affinity = 0;
    if (!hwnd || !set_affinity || !get_affinity
            || !set_affinity(hwnd, WDA_EXCLUDEFROMCAPTURE)
            || !get_affinity(hwnd, &affinity)
            || affinity != WDA_EXCLUDEFROMCAPTURE) {
        LOGE("Could not exclude this window from desktop capture (Windows error %lu)", GetLastError());
        return false;
    }
    LOGI("Desktop camera window excluded from capture");
    return true;
}
