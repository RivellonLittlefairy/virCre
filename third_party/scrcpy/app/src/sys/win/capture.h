// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
#ifndef SC_WINDOWS_CAPTURE_H
#define SC_WINDOWS_CAPTURE_H
#include <stdbool.h>
#include <SDL3/SDL_video.h>
bool sc_windows_exclude_from_capture(SDL_Window *window);
#endif
