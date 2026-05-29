#ifndef MRUBYCS_BENCHMARK_HELPER_H
#define MRUBYCS_BENCHMARK_HELPER_H

#include <mruby.h>
#include <mruby/proc.h>

extern int32_t chibiruby_mrbc_compile(mrb_state *mrb,
                                    const uint8_t *source,
                                    uint32_t source_length,
                                    uint8_t **bin,
                                    int32_t *bin_size,
                                    char **error_message);


extern int32_t chibiruby_mrbc_compile_to_proc(mrb_state *mrb,
                                            const uint8_t *source,
                                            uint32_t source_length,
                                            struct RProc **proc,
                                            char **error_message);

extern void chibiruby_mrbc_release_proc(mrb_state *mrb, struct RProc *proc);
  
#endif
