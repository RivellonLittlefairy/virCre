// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
#ifndef SC_IME_STATE_H
#define SC_IME_STATE_H

#include <stdbool.h>
#include <SDL3/SDL_scancode.h>

// Per-window routing metadata only. No composition or committed text is retained.
struct sc_ime_state {
    bool composing;
    bool blocked[SDL_SCANCODE_COUNT];
};

void sc_ime_state_init(struct sc_ime_state *state);
void sc_ime_state_set_composing(struct sc_ime_state *state, bool composing);
bool sc_ime_state_filter_key(struct sc_ime_state *state, SDL_Scancode scancode,
                             bool down, bool repeat, bool shortcut);
#endif
