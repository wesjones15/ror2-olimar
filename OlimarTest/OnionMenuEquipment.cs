using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;

namespace OlimarTest
{
    // Y belongs to the open Onion page. Keep its press from also spending equipment,
    // including while Y is still held after the return-squad shortcut closes the page.
    internal static class OnionMenuEquipment
    {
        static Hook hook;

        internal static void Install()
        {
            hook = new Hook(typeof(EquipmentSlot).GetMethod("MyFixedUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                (Action<Action<EquipmentSlot, float>, EquipmentSlot, float>)Update);
        }

        static void Update(Action<EquipmentSlot, float> original, EquipmentSlot slot, float dt)
        {
            var body = slot.characterBody;
            var driver = body ? body.GetComponent<OlimarDriver>() : null;
            var input = driver && driver.BlocksEquipment ? body.GetComponent<InputBankTest>() : null;
            if (!input) { original(slot, dt); return; }
            // Suppress the manual press before RoR2 sends its equipment command on clients.
            // Timers, ongoing effects and Gesture's automatic use still run normally.
            var saved = input.activateEquipment;
            input.activateEquipment = default;
            try { original(slot, dt); }
            finally { input.activateEquipment = saved; }
        }
    }
}
