#include <mruby.h>
#include <mruby/presym.h>
#include <mruby/error.h>
#include <mruby/string.h>
#include <mruby/data.h>
#include <mruby.h>
#include <mruby/dump.h>
#include "chibiruby-benchmark-helper.h"

#define MRBCS_OK            0
#define MRBCS_FAILED        -1

mrb_value mrb_get_backtrace(mrb_state*);
int mrb_dump_irep(mrb_state *mrb, const mrb_irep *irep, uint8_t flags, uint8_t **bin, size_t *bin_size);

extern int32_t chibiruby_mrbc_compile(mrb_state *mrb,
                                    const uint8_t *source,
                                     uint32_t source_length,
                                     uint8_t **bin,
                                     int32_t *bin_size,
                                     char** error_message)
{
  int ai = mrb_gc_arena_save(mrb);

  mrb_ccontext *compiler_ctx = mrb_ccontext_new(mrb);
  compiler_ctx->no_exec = TRUE;
  compiler_ctx->capture_errors = TRUE;
  
  mrb_value proc = mrb_load_nstring_cxt(mrb, (const char *)source, (size_t)source_length, compiler_ctx);
  
  if (mrb->exc) {
    mrb_value exc_backtrace = mrb_get_backtrace(mrb);
    mrb_value exc_inspection = mrb_inspect(mrb, mrb_obj_value(mrb->exc));
    if (mrb_test(exc_backtrace)) {
      mrb_value exc_backtrace_lines = mrb_funcall(mrb, exc_backtrace, "join", 1, mrb_str_new_cstr(mrb, "\n"));
      mrb_funcall(mrb, exc_inspection, "<<", 1, exc_backtrace_lines);
    }

    mrb_ccontext_free(mrb, compiler_ctx);
    mrb_gc_arena_restore(mrb, ai);

    *error_message = mrb_str_to_cstr(mrb, exc_inspection);
    return MRBCS_FAILED;
  }
  
  if (!mrb_proc_p(proc)) {
    mrb_ccontext_free(mrb, compiler_ctx);
    mrb_gc_arena_restore(mrb, ai);
    *error_message = (char *)"Failed to create proc";
    return MRBCS_FAILED;
  }
 
  mrb_gc_arena_restore(mrb, ai);

  const mrb_irep *irep = mrb_proc_ptr(proc)->body.irep;
  size_t result_size;
  int result = mrb_dump_irep(mrb, irep, 0, bin, &result_size);
  if (result == MRB_DUMP_OK) {
    *bin_size = (int32_t)result_size;
    return MRBCS_OK;
  } else {
    *error_message = (char *)"Failed to dump irep";
    return MRBCS_FAILED;
  }
}


extern int32_t chibiruby_mrbc_compile_to_proc(mrb_state *mrb,
                                            const uint8_t *source,
                                             uint32_t source_length,
                                             struct RProc **proc,
                                             char** error_message)
{
  int ai = mrb_gc_arena_save(mrb);

  mrb_ccontext *compiler_ctx = mrb_ccontext_new(mrb);
  compiler_ctx->no_exec = TRUE;
  compiler_ctx->capture_errors = TRUE;
  
  mrb_value proc_value = mrb_load_nstring_cxt(mrb, (const char *)source, (size_t)source_length, compiler_ctx);
  
  if (mrb->exc) {
    mrb_value exc_backtrace = mrb_get_backtrace(mrb);
    mrb_value exc_inspection = mrb_inspect(mrb, mrb_obj_value(mrb->exc));
    if (mrb_test(exc_backtrace)) {
      mrb_value exc_backtrace_lines = mrb_funcall(mrb, exc_backtrace, "join", 1, mrb_str_new_cstr(mrb, "\n"));
      mrb_funcall(mrb, exc_inspection, "<<", 1, exc_backtrace_lines);
    }

    mrb_ccontext_free(mrb, compiler_ctx);
    mrb_gc_arena_restore(mrb, ai);

    *error_message = mrb_str_to_cstr(mrb, exc_inspection);
    return MRBCS_FAILED;
  }
  
  if (!mrb_proc_p(proc_value)) {
    mrb_ccontext_free(mrb, compiler_ctx);
    mrb_gc_arena_restore(mrb, ai);
    *error_message = (char *)"Failed to create proc";
    return MRBCS_FAILED;
  }

  mrb_gc_register(mrb, proc_value);
  mrb_gc_arena_restore(mrb, ai);

  *proc = mrb_proc_ptr(proc_value);
  return MRBCS_OK;
}

extern void chibiruby_mrbc_release_proc(mrb_state *mrb, struct RProc *proc)
{
  mrb_value v = mrb_obj_value(proc);
  mrb_gc_unregister(mrb, v);
}


void mrb_chibiruby_benchmark_helper_gem_init(mrb_state *mrb)
{
}

void mrb_chibiruby_benchmark_helper_gem_final(mrb_state *mrb)
{
}
