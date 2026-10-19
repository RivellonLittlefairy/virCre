#include "common.h"
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>
#include "controller.h"
#include "keyboard_sdk.h"
#include "input_events.h"

static void ended(struct sc_controller *controller, bool error, void *userdata) {
    (void) controller; (void) error; (void) userdata;
    assert(false); // These checks do not start network threads.
}

int main(int argc, char *argv[]) {
    (void) argc; (void) argv;
    assert(SDL_Init(SDL_INIT_EVENTS));
    struct sc_controller controller;
    const struct sc_controller_callbacks callbacks = {.on_ended = ended};
    assert(sc_controller_init(&controller, SC_SOCKET_NONE, &callbacks, NULL));
    struct sc_keyboard_sdk keyboard;
    sc_keyboard_sdk_init(&keyboard, &controller, SC_KEY_INJECT_MODE_TEXT, true, true);
    struct sc_key_event first_letter = {.action = SC_ACTION_DOWN, .keycode = SC_KEYCODE_n,
        .scancode = sc_scancode_from_sdl(SDL_SCANCODE_N)};
    keyboard.key_processor.ops->process_key(&keyboard.key_processor, &first_letter, 0);
    first_letter.action = SC_ACTION_UP;
    keyboard.key_processor.ops->process_key(&keyboard.key_processor, &first_letter, 0);
    assert(sc_vecdeque_is_empty(&controller.queue)); // No raw first-letter prefix, before composition starts.
    const struct sc_text_event committed = {.text = "\xE4\xBD\xA0\xE5\xA5\xBD"};
    keyboard.key_processor.ops->process_text(&keyboard.key_processor, &committed);
    assert(sc_vecdeque_size(&controller.queue) == 1);
    struct sc_control_msg sent = sc_vecdeque_pop(&controller.queue);
    assert(sent.type == SC_CONTROL_MSG_TYPE_COMMIT_TEXT && sent.commit_text.sequence == 1);
    assert(!strcmp(sent.commit_text.text, committed.text));
    sc_control_msg_destroy(&sent);
    sc_controller_destroy(&controller);
    assert(sc_controller_init(&controller, SC_SOCKET_NONE, &callbacks, NULL));
    for (unsigned i = 0; i < 17; ++i) {
        char *text = malloc(65537);
        memset(text, 'a', 65536); text[65536] = '\0';
        struct sc_control_msg msg = {.type = SC_CONTROL_MSG_TYPE_COMMIT_TEXT,
            .commit_text = {.sequence = i + 1, .text = text}};
        bool accepted = sc_controller_push_msg(&controller, &msg);
        if (i < 16) { assert(accepted); }
        else { assert(!accepted); free(text); }
    }
    sc_controller_destroy(&controller);
    assert(sc_controller_init(&controller, SC_SOCKET_NONE, &callbacks, NULL));
    for (unsigned i = 0; i < 61; ++i) {
        struct sc_control_msg msg = {.type = SC_CONTROL_MSG_TYPE_COMMIT_TEXT,
            .commit_text = {.sequence = i + 1, .text = strdup("x")}};
        bool accepted = sc_controller_push_msg(&controller, &msg);
        if (i < 60) { assert(accepted); }
        else { assert(!accepted); free(msg.commit_text.text); }
    }
    sc_controller_destroy(&controller);
    assert(sc_controller_init(&controller, SC_SOCKET_NONE, &callbacks, NULL));
    sc_controller_stop(&controller);
    struct sc_control_msg late = {.type = SC_CONTROL_MSG_TYPE_COMMIT_TEXT,
        .commit_text = {.sequence = 100, .text = strdup("x")}};
    assert(!sc_controller_push_msg(&controller, &late));
    free(late.commit_text.text);
    sc_controller_destroy(&controller);
    SDL_Quit();
    return 0;
}
