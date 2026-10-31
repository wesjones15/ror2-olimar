using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;

namespace OlimarTest
{
    // Y belongs to the open Onion page. Keep its press from also spending equipment,
    // including while Y is still held after the return-squad shortcut closes the page.
    // RT + X and RT + Y (the remote dump and fill) and a ride likewise keep RoR2's interact and
    // equipment presses to themselves.
    internal static class OnionMenuEquipment
    {
        static Hook hook, interactHook;

        internal static void Install()
        {
            hook = new Hook(typeof(EquipmentSlot).GetMethod("MyFixedUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                (Action<Action<EquipmentSlot, float>, EquipmentSlot, float>)Update);
            var interact = typeof(InteractionDriver).GetMethod("FixedUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (interact == null) { Plugin.Log.LogWarning("InteractionDriver.FixedUpdate not found: RT + X may also interact."); return; }
            interactHook = new Hook(interact, (Action<Action<InteractionDriver>, InteractionDriver>)Interact);
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

        // RoR2 interacts on the press of its interact button; while it isn't his, Olimar's driver says so.
        static void Interact(Action<InteractionDriver> original, InteractionDriver driver)
        {
            var olimar = driver ? driver.GetComponent<OlimarDriver>() : null;
            var input = olimar && olimar.BlocksInteract ? driver.GetComponent<InputBankTest>() : null;
            if (!input) { original(driver); return; }
            var saved = input.interact;
            input.interact = default;
            try { original(driver); }
            finally { input.interact = saved; }
        }
    }
}
