using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    internal static class BigSilkBallPhaseGuard
    {
        private static bool _isActive;

        public static bool IsActive => _isActive;
        public static int Generation { get; private set; }

        public static void Begin(string reason)
        {
            _isActive = true;
            Generation++;
            Log.Info($"[BigSilkBallPhaseGuard] 开启大丝球阶段: {reason}, generation={Generation}");
        }

        public static void End(string reason)
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            Generation++;
            Log.Info($"[BigSilkBallPhaseGuard] 关闭大丝球阶段: {reason}, generation={Generation}");
        }
    }
}
