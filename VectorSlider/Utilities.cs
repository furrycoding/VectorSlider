using System;

using GlmSharp;

namespace VectorSlider
{
    public static class Utils
    {
    }

    public struct DeltaTime
    {
        private bool initialized;
        private long lastTime;

        public float GetDeltaTime()
        {
            var curTime = System.Diagnostics.Stopwatch.GetTimestamp();
            var timeDiff = curTime - lastTime;
            lastTime = curTime;

            if (!initialized) {
                initialized = true;
                return 0;
            }

            var dt = timeDiff / (float)System.Diagnostics.Stopwatch.Frequency;
            return dt;
        }
    }

    public static class Extensions
    {
        public static float Clamp(this float value, float min, float max)
        {
            return Math.Min(Math.Max(min, value), max);
        }
    }
}
