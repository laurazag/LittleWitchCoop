using System;
namespace LittleWitchCoop
{
    internal sealed class HoldRepeat
    {
        private double started, next;
        private bool active;
        public int Begin(double now) { active = true; started = now; next = now + 0.4; return 1; }
        public void End() { active = false; }
        public int Poll(double now)
        {
            if (!active || now < next) return 0;
            next = now + 0.12; // No catch-up burst after a slow frame.
            double held = now - started;
            return held >= 4 ? 20 : held >= 2.5 ? 10 : held >= 1.2 ? 5 : 1;
        }
    }
}
