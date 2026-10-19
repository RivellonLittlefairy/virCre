#include "ime_state.h"
#include <string.h>

void sc_ime_state_init(struct sc_ime_state *state) {
    memset(state, 0, sizeof(*state));
}
void sc_ime_state_set_composing(struct sc_ime_state *state, bool composing) {
    state->composing = composing;
}
bool sc_ime_state_filter_key(struct sc_ime_state *state, SDL_Scancode scancode,
                             bool down, bool repeat, bool shortcut) {
    if (scancode <= SDL_SCANCODE_UNKNOWN || scancode >= SDL_SCANCODE_COUNT) {
        return false;
    }
    if (down) {
        if (!repeat) {
            state->blocked[scancode] = state->composing && !shortcut;
        }
        return state->blocked[scancode];
    }
    bool blocked = state->blocked[scancode];
    state->blocked[scancode] = false;
    return blocked;
}
