using System;
using System.Diagnostics;
using System.Text;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // CPU timings from the actual game. No per-frame strings or heap allocations. Debug.PerformanceLog
    // writes a summary every ten seconds; without it a summary is written only for a ten-second
    // stretch that was slow (under 40 fps, or many long frames), at most 30 times a run, so a weak
    // machine's log explains itself. It does not measure GPU time or network latency.
    internal static class OlimarPerformance
    {
        internal enum Part { Fixed, Gather, Client, Draw, Items }
        static readonly double[] milliseconds = new double[5], maximum = new double[5];
        static readonly int[] calls = new int[5];
        static float since;
        static int frames, slowFrames, mostTicks;
        static double frameSeconds, worstFrame;
        static Run run;

        static bool Always => Plugin.PerformanceLog != null && Plugin.PerformanceLog.Value;
        static bool Enabled => Run.instance && !PauseManager.isPaused;
        static int reported, collections;

        internal readonly struct Timing : IDisposable
        {
            readonly Part part;
            readonly long start;
            internal Timing(Part part, bool enabled)
            {
                this.part = part;
                start = enabled ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (start == 0) return;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                int i = (int)part;
                milliseconds[i] += ms;
                maximum[i] = Math.Max(maximum[i], ms);
                calls[i]++;
            }
        }

        internal static Timing Measure(Part part) => new Timing(part, Enabled);

        /// A physics step advanced the sim by `ticks`: a slow machine batches several.
        internal static void Ticks(uint ticks) { if (Enabled) mostTicks = Math.Max(mostTicks, (int)ticks); }

        static void Reset()
        {
            Array.Clear(milliseconds, 0, milliseconds.Length);
            Array.Clear(maximum, 0, maximum.Length);
            Array.Clear(calls, 0, calls.Length);
            frames = slowFrames = mostTicks = 0;
            frameSeconds = worstFrame = 0;
            since = Time.realtimeSinceStartup;
        }

        /// Once per rendered frame.
        internal static void Report()
        {
            if (!Enabled) { run = null; return; }
            if (run != Run.instance) { run = Run.instance; reported = 0; Reset(); return; }
            double dt = Time.unscaledDeltaTime;
            frames++;
            frameSeconds += dt;
            worstFrame = Math.Max(worstFrame, dt);
            if (dt > 1.0 / 30.0) slowFrames++;
            float seconds = Time.realtimeSinceStartup - since;
            if (seconds < 10f) return;
            double fps = frames / Math.Max(frameSeconds, .001);
            if (!Always && (reported >= 30 || (fps >= 40 && slowFrames * 20 < frames)))
            {
                collections = GC.CollectionCount(0);
                Reset();
                return;
            }
            reported++;
            var world = PikminWorld.Instance;
            var text = new StringBuilder("Olimar performance: ");
            text.Append(NetworkServer.active ? "host" : "client");
            text.Append($" pikmin={(world ? world.PikiCount : 0)}, gcs={GC.CollectionCount(0) - collections}, heap={GC.GetTotalMemory(false) / 1048576}MB, bodies={CharacterBody.readOnlyInstancesList.Count}, fps={fps:F1}, " +
                $"worstFrame={worstFrame * 1000:F1}ms, framesOver33ms={slowFrames}/{frames}, mostTicksInOneStep={mostTicks}; " +
                "CPU ms/s (max call ms): ");
            for (int i = 0; i < calls.Length; i++)
                text.Append($"{(Part)i}={milliseconds[i] / seconds:F2} ({maximum[i]:F2}, n={calls[i]}) ");
            Plugin.Log.LogInfo(text.ToString());
            collections = GC.CollectionCount(0);
            Reset();
        }
    }
}
