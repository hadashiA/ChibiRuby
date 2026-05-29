#nullable enable
namespace ChibiRuby.Unity
{
    /// <summary>
    /// Unity-specific convenience extensions for <see cref="MRubyState"/>.
    /// </summary>
    public static class MRubyStateUnityExtensions
    {
        /// <summary>
        /// Install a fresh <see cref="UnityFiberScheduler"/> on <paramref name="state"/>.
        /// Shorthand for <c>state.UseFiberScheduler(new UnityFiberScheduler())</c>.
        /// </summary>
        /// <returns>The installed <see cref="UnityFiberScheduler"/>, for further configuration.</returns>
        public static UnityFiberScheduler UseUnityFiberScheduler(this MRubyState state)
        {
            var scheduler = new UnityFiberScheduler();
            state.UseFiberScheduler(scheduler);
            return scheduler;
        }
    }
}
