#include <mruby.h>
#include <stdlib.h>

void chibiruby_free(void *ptr)
{
    free(ptr);
}

void mrb_chibiruby_compiler_gem_init(mrb_state *mrb)
{
}

void mrb_chibiruby_compiler_gem_final(mrb_state *mrb)
{
}
