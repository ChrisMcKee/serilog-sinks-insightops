#if !NETSTANDARD2_0
using System;
using System.Text;

namespace Serilog.Sinks.InsightOps.Rapid7
{
    internal static class StringBuilderCache
    {
        internal const int MaxBuilderSize = 65360;

        [ThreadStatic]
        private static StringBuilder? _cachedInstance;

        public static StringBuilder Acquire(int capacity = MaxBuilderSize)
        {
            if (capacity <= MaxBuilderSize)
            {
                var sb = _cachedInstance;
                if (sb != null)
                {
                    // Avoid StringBuilder block fragmentation by getting a new StringBuilder
                    // when the requested size is larger than the current capacity
                    if (capacity <= sb.Capacity)
                    {
                        _cachedInstance = null;
                        sb.Clear();
                        return sb;
                    }
                }
            }

            return new StringBuilder(capacity);
        }

        public static string GetStringAndRelease(StringBuilder sb)
        {
            var result = sb.ToString();
            Release(sb);

            return result;
        }

        public static void Release(StringBuilder sb)
        {
            if (sb?.Capacity <= MaxBuilderSize)
            {
                _cachedInstance = sb;
            }
        }
    }
}

#endif
