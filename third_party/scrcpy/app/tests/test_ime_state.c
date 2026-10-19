#include "common.h"
#include <assert.h>
#include "ime_state.h"

int main(int argc, char *argv[]) {
    (void) argc;
    (void) argv;
    struct sc_ime_state state;
    sc_ime_state_init(&state);
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_RETURN, true, false, false));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_RETURN, false, false, false));

    const SDL_Scancode candidates[] = {SDL_SCANCODE_1, SDL_SCANCODE_SPACE,
        SDL_SCANCODE_RETURN, SDL_SCANCODE_BACKSPACE, SDL_SCANCODE_ESCAPE,
        SDL_SCANCODE_LEFT, SDL_SCANCODE_RIGHT};
    for (unsigned i = 0; i < ARRAY_LEN(candidates); ++i) {
        sc_ime_state_set_composing(&state, true);
        assert(sc_ime_state_filter_key(&state, candidates[i], true, false, false));
        sc_ime_state_set_composing(&state, false);
        assert(sc_ime_state_filter_key(&state, candidates[i], false, false, false));
        assert(!sc_ime_state_filter_key(&state, candidates[i], true, false, false));
        assert(!sc_ime_state_filter_key(&state, candidates[i], false, false, false));
    }

    sc_ime_state_set_composing(&state, true);
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_C, true, false, true));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_C, false, false, true));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_V, true, false, true));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_V, false, false, true));

    assert(sc_ime_state_filter_key(&state, SDL_SCANCODE_BACKSPACE, true, false, false));
    sc_ime_state_set_composing(&state, false); // cancel, commit, language switch or focus loss
    assert(sc_ime_state_filter_key(&state, SDL_SCANCODE_BACKSPACE, true, true, false));
    assert(sc_ime_state_filter_key(&state, SDL_SCANCODE_BACKSPACE, false, false, false));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_BACKSPACE, true, false, false));
    // A new press after focus returns must not inherit an unreleased old composition key.
    sc_ime_state_set_composing(&state, true);
    assert(sc_ime_state_filter_key(&state, SDL_SCANCODE_RETURN, true, false, false));
    sc_ime_state_set_composing(&state, false);
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_RETURN, true, false, false));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_RETURN, false, false, false));
    assert(!sc_ime_state_filter_key(&state, SDL_SCANCODE_UNKNOWN, true, false, false));
    return 0;
}
