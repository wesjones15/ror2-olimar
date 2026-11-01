using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b, a.y*b, a.z*b);
        public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static bool Blocked;
        public static bool Linecast(Vector3 start, Vector3 end, int mask, QueryTriggerInteraction trigger) => Blocked;
    }
}
namespace OlimarTest
{
    static class UnityHost
    {
        public const float Scale = .04f;
        public const int Mask = 1;
        public static Vector3 ToUnity(Native.Vec3 p) => new Vector3(p.x*Scale, p.y*Scale, p.z*Scale);
    }
    sealed class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    static class Plugin
    {
        public static readonly Setting<float> PikminHealth = new Setting<float>(.5f);
        public static readonly Setting<bool> LogEvents = new Setting<bool>(false);
        public static readonly Logger Log = new Logger();
        public class Logger { public void LogInfo(string text) => Console.WriteLine(text); }
    }
    sealed class ShipRepairSession
    {
        public static ShipRepairSession Instance;
        public bool Terminal;
        public static implicit operator bool(ShipRepairSession s) => s != null;
    }
    internal partial class PikminWorld
    {
        readonly IntPtr sim;
        Native.Piki[] Pikis = Array.Empty<Native.Piki>();
        int PikiCount;
        Func<Native.Piki[], uint, uint> readPikis;
        public bool stepping;
        readonly List<Action> afterStep = new List<Action>();
        public PikminWorld(IntPtr sim) { this.sim = sim; }
        float NearestOlimarHealth(Vector3 _) => 220f;
        void Freeze(Vector3 at, float radius, float proc) { }
        static int Read<T>(ref T[] buffer, Func<T[], uint, uint> fetch)
        {
            uint count = fetch(buffer, (uint)buffer.Length);
            if (count > buffer.Length) { buffer = new T[count]; count = fetch(buffer, (uint)buffer.Length); }
            return (int)count;
        }
        public void Apply(float damage, RoR2.BlastAttack.FalloffModel falloff = RoR2.BlastAttack.FalloffModel.None, bool los = false) =>
            EnemyBlast(new Vector3(), 3f, damage, falloff, los, false, 1f);
        public void Drain() { stepping = false; foreach (var callback in afterStep) callback(); afterStep.Clear(); }
    }
}
