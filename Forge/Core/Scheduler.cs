using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Delayed and repeating actions on the main thread, driven by Forge's OnUpdate (no coroutines needed).
    /// Exceptions are caught and logged once per task name.
    /// </summary>
    public static class Scheduler
    {
        sealed class Task { public string Name; public int Frames; public float At, Interval; public Func<bool> Repeat; public Action Once; }

        static readonly List<Task> _tasks = new();
        static readonly List<Task> _add = new();

        /// <summary>Runs <paramref name="a"/> after <paramref name="frames"/> frames (1 = next frame).</summary>
        public static void Frames(int frames, Action a, string name = null) =>
            _add.Add(new Task { Name = name ?? a.Method.Name, Frames = Math.Max(1, frames), Once = a });

        /// <summary>Runs <paramref name="a"/> after <paramref name="seconds"/> of real (unscaled) time.</summary>
        public static void Seconds(float seconds, Action a, string name = null) =>
            _add.Add(new Task { Name = name ?? a.Method.Name, At = Time.unscaledTime + seconds, Once = a });

        /// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> seconds while it returns true.</summary>
        public static void Every(float interval, Func<bool> tick, string name = null) =>
            _add.Add(new Task { Name = name ?? tick.Method.Name, Interval = Math.Max(0f, interval), At = Time.unscaledTime + interval, Repeat = tick });

        internal static void Tick()
        {
            if (_add.Count > 0) { _tasks.AddRange(_add); _add.Clear(); }
            if (_tasks.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _tasks.Count - 1; i >= 0; i--)
            {
                var t = _tasks[i];
                if (t.Frames > 0) { if (--t.Frames > 0) continue; }
                else if (now < t.At) continue;
                bool keep = false;
                try
                {
                    if (t.Repeat != null) { keep = t.Repeat(); t.At = now + t.Interval; }
                    else t.Once();
                }
                catch (Exception e) { ForgeLog.WarnOnce("sched:" + t.Name, $"Scheduled task {t.Name} failed: {e}"); keep = false; }
                if (!keep) _tasks.RemoveAt(i);
            }
        }
    }
}
