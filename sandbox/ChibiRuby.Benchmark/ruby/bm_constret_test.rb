# Constant-returning-method devirt test: 0-arg methods that return an immediate constant,
# called CROSS-OBJECT (cfg.w), including multi-level delegation (total -> w, deep -> total -> w).
class Config
  def w; 256; end          # single-level fixnum constant
  def h; 240; end
  def scale; 2.5; end      # float constant
  def total; w; end        # multi-level: total -> w -> 256
  def deep; total; end     # 3-level: deep -> total -> w -> 256
  def enabled; true; end   # bool constant
end

class Runner
  def run(cfg)
    cfg.w + cfg.h + cfg.total + cfg.deep + (cfg.scale * 4.0) + (cfg.enabled ? 100 : 0)
  end
end

# 256 + 240 + 256 + 256 + 10.0 + 100 = 1118.0
Runner.new.run(Config.new)
