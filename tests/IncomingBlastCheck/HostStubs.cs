using System;
using System.Collections.Generic;
using UnityEngine;
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
        public static readonly Setting<int> PikminMeleeMaxVictims = new Setting<int>(2);
        public static readonly Setting<float> PikminMeleeDamageScale = new Setting<float>(.6f);
        public static readonly Setting<float> PikminMeleeIFrames = new Setting<float>(.3f);
        public static readonly Setting<bool> LogEvents = new Setting<bool>(false);
        public static readonly Logger Log = new Logger();
        public class Logger
        {
            public void LogInfo(string text) => Console.WriteLine(text);
            public void LogError(string text) => throw new Exception(text);
            public void LogWarning(string text) => throw new Exception(text);
        }
    }
    sealed class ShipRepairSession
    {
        public static ShipRepairSession Instance;
        public bool Terminal;
        public static implicit operator bool(ShipRepairSession s) => s != null;
    }
    internal partial class PikminWorld
    {
        public static PikminWorld Instance;
        public static implicit operator bool(PikminWorld world) => world != null;
        readonly IntPtr sim;
        Native.Piki[] Pikis = Array.Empty<Native.Piki>();
        int PikiCount;
        Func<Native.Piki[], uint, uint> readPikis;
        public bool stepping;
        readonly List<Action> afterStep = new List<Action>();
        public PikminWorld(IntPtr sim) { this.sim = sim; Instance = this; }
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
        public bool Swing(RoR2.OverlapAttack attack, int reportedVictims = 0) => MobMeleeFired((a,list) =>
        {
            for (int i = 0; i < reportedVictims; i++)
                if (!Ror2Common.OverlapDamage.CoversReport(new RoR2.DamageInfo
                    { attacker=a.attacker, inflictor=a.inflictor, damageType=a.damageType }))
                    throw new Exception("mob melee body report was not covered");
            return reportedVictims > 0;
        }, attack, null);
        public void ResetSwing(RoR2.OverlapAttack attack) => MobMeleeReset(a => a.ResetIgnoredHealthComponents(), attack);
    }
}
namespace MonoMod.RuntimeDetour
{
    public sealed class Hook : IDisposable
    {
        public Hook(System.Reflection.MethodBase method, Delegate callback) { if (method == null) throw new ArgumentNullException(nameof(method)); }
        public void Dispose() { }
    }
}
