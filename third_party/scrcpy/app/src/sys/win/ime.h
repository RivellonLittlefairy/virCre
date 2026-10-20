// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
#ifndef SC_WINDOWS_IME_H
#define SC_WINDOWS_IME_H
#include <stdbool.h>
#include <SDL3/SDL_video.h>
bool sc_windows_ime_init(SDL_Window *window);
void sc_windows_ime_destroy(SDL_Window *window);
#endif
