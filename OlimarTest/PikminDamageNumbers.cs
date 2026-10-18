using System;
using BepInEx.Configuration;
using Ror2Common;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Keep RoR2's damage particle system, shader, colours, crit styling and motion. Only the
    // digit and suffix cells change to the player's extracted Pikmin bitmap art.
    sealed class PikminDamageNumbers : MonoBehaviour
    {
        ConfigEntry<bool> enabledSetting;
        ParticleSystemRenderer target;
        Material original, replacement;
        Texture2D atlas;
        bool tried;
        float next;
        static bool loggedApplied;

        public static void Install(GameObject host, ConfigFile config)
        {
            var component = host.AddComponent<PikminDamageNumbers>();
            component.enabledSetting = config.Bind("Look", "PikminDamageNumbers", true,
                "Use your disc's Pikmin HUD digits for the floating damage numbers while you play Olimar (every number you " +
                "see then, whoever dealt it; other survivors keep RoR2's font). " +
                "Keeps RoR2's damage colours and crit effects. False restores the original font; can change during play.");
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (enabledSetting == null || !enabledSetting.Value || !PlayingOlimar())
            {
                Restore();
                return;
            }
            if (!Plugin.AssetsSettled) return;
            var manager = DamageNumberManager.instance;
            var renderer = manager ? manager.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer != target)
            {
                Restore();
                target = renderer;
            }
            if (!target || tried) return;
            tried = true;
            try
            {
                original = target.sharedMaterial;
                if (!original || !original.HasProperty("_MainTex"))
                    throw new InvalidOperationException("Damage number material has no number atlas");
                var source = original.GetTexture("_MainTex");
                if (!source || source.width != 512 || source.height != 512)
                    throw new InvalidOperationException("Expected RoR2's 512x512 number atlas");
                var dir = Native.CString(Plugin.AssetsDir);
                var digits = new byte[16][];
                for (uint digit = 0; digit < 10; digit++)
                {
                    if (Native.pik_number_texture(dir, digit, 0, out var info, null, 0) == 0 || info.width != 64 || info.height != 64)
                        throw new InvalidOperationException($"Pikmin HUD digit {digit} missing or invalid");
                    digits[digit] = new byte[64 * 64 * 4];
                    if (Native.pik_number_texture(dir, digit, 0, out info, digits[digit], (uint)digits[digit].Length) == 0)
                        throw new InvalidOperationException($"Could not decode Pikmin HUD digit {digit}");
                }
                for (uint glyph = 0; glyph < 4; glyph++)
                {
                    if (Native.pik_damage_suffix_texture(dir, glyph, out var info, null, 0) == 0 || info.width != 64 || info.height != 64)
                        throw new InvalidOperationException($"Pikmin damage suffix {glyph} missing or invalid");
                    digits[glyph + 12] = new byte[64 * 64 * 4];
                    if (Native.pik_damage_suffix_texture(dir, glyph, out info, digits[glyph + 12], (uint)digits[glyph + 12].Length) == 0)
                        throw new InvalidOperationException($"Could not decode Pikmin damage suffix {glyph}");
                }
                // The bundled texture isn't CPU-readable. Copy it before replacing digit cells.
                atlas = new Texture2D(512, 512, TextureFormat.RGBA32, false)
                    { name = "Pikmin damage number atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var previous = RenderTexture.active;
                var temporary = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32);
                try
                {
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    atlas.ReadPixels(new Rect(0, 0, 512, 512), 0, 0, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
                var colors = atlas.GetPixels32();
                var rgba = new byte[colors.Length * 4];
                for (int i = 0; i < colors.Length; i++)
                {
                    rgba[i * 4] = colors[i].r; rgba[i * 4 + 1] = colors[i].g;
                    rgba[i * 4 + 2] = colors[i].b; rgba[i * 4 + 3] = colors[i].a;
                }
                DamageNumberAtlas.ReplaceGlyphs(rgba, 512, 512, digits, 64, 64);
                atlas.LoadRawTextureData(rgba);
                atlas.Apply(false, true);
                replacement = new Material(original) { name = "Pikmin damage numbers" };
                replacement.SetTexture("_MainTex", atlas);
                target.sharedMaterial = replacement;
                if (!loggedApplied)
                {
                    loggedApplied = true;
                    Plugin.Log.LogInfo("Pikmin HUD digits and bitmap suffixes applied to damage numbers (Look.PikminDamageNumbers)");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Pikmin damage numbers: {e.Message}; keeping RoR2's font");
                if (replacement) Destroy(replacement);
                if (atlas) Destroy(atlas);
                replacement = null;
                atlas = null;
            }
        }

        // Damage numbers are one particle system for everyone on screen, so the font follows who is
        // playing here: Olimar's player sees the Pikmin digits, anyone else RoR2's own.
        static bool PlayingOlimar()
        {
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
                if (user != null && user.cachedBody && user.cachedBody.GetComponent<OlimarDriver>())
                    return true;
            return false;
        }

        void Restore()
        {
            if (target && replacement && target.sharedMaterial == replacement)
                target.sharedMaterial = original;
            if (replacement) Destroy(replacement);
            if (atlas) Destroy(atlas);
            target = null; original = null; replacement = null; atlas = null; tried = false;
        }

        void OnDestroy() => Restore();
    }
}
