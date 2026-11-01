using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    internal partial class PikminWorld
    {
        static Hook jumpPadHook;

        // JumpVolume.OnTriggerStay is where a pad writes its velocity and its air-control
        // flag. Reading that flag later is unreliable: CharacterMotor clears
        // disableAirControlUntilCollision on the next movement hit, so by the time BuildInput
        // runs a real pad looks like ordinary knockback and the sim replays it with the disc's
        // gravity (and never queues the squad). Take the pad's own throw here instead.
        static void HookJumpPads()
        {
            if (jumpPadHook != null) return;
            try
            {
                var method = typeof(JumpVolume).GetMethod("OnTriggerStay",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Collider) }, null);
                if (method == null)
                {
                    Plugin.Log.LogWarning("jump pads: JumpVolume.OnTriggerStay not found; the motor's air-control flag is the only pad marker");
                    return;
                }
                jumpPadHook = new Hook(method,
                    (Action<Action<JumpVolume, Collider>, JumpVolume, Collider>)JumpPadStayed);
            }
            catch (Exception e)
            {
                jumpPadHook?.Dispose();
                jumpPadHook = null;
                Plugin.Log.LogWarning($"jump pads: hook failed ({e.GetType().Name}: {e.Message}); the motor's air-control flag is the only pad marker");
            }
        }

        static void JumpPadStayed(Action<JumpVolume, Collider> original, JumpVolume pad, Collider other)
        {
            original(pad, other);
            try
            {
                var body = other ? other.GetComponent<CharacterBody>() : null;
                var driver = body ? body.GetComponent<OlimarDriver>() : null;
                if (driver) driver.NoteJumpPad(pad);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"jump pads: {e}");
            }
        }
    }
}
