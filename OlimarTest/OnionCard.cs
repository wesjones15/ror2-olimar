using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace OlimarTest
{
    // The Onion is a minion with a Commando body, so RoR2's HUD lists it among the allies with
    // Commando's portrait. It isn't a character: take it out of that list.
    //
    // AllyCardManager.PopulateCharacterDataSet fills the list of allies the cards are built
    // from; the hook lets it, then drops the Onion's entries. Its data type is private, so the
    // set is edited through reflection. If the hook can't be made, a fallback hides the card
    // once it exists.
    sealed class OnionCard : MonoBehaviour
    {
        const string OnionMaster = "OlimarTestOnion";

        static Hook hook;
        static FieldInfo bufferField, countField, masterField;

        delegate void Populate(AllyCardManager self, object set);

        public static void Install(GameObject host)
        {
            try
            {
                var method = typeof(AllyCardManager).GetMethod("PopulateCharacterDataSet",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var set = method.GetParameters()[0].ParameterType;
                bufferField = set.GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                countField = set.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                masterField = bufferField.FieldType.GetElementType().GetField("master", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                hook = new Hook(method, (Action<Populate, AllyCardManager, object>)Filtered);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Onion ally card: hook failed ({e.GetType().Name}: {e.Message}); hiding the card instead");
                host.AddComponent<OnionCard>();
            }
        }

        static bool IsOnion(CharacterMaster master) => master && master.name.StartsWith(OnionMaster, StringComparison.Ordinal);

        static void Filtered(Populate orig, AllyCardManager self, object set)
        {
            orig(self, set);
            try
            {
                var buffer = (Array)bufferField.GetValue(set);
                int count = (int)countField.GetValue(set), kept = 0;
                for (int i = 0; i < count; i++)
                {
                    var entry = buffer.GetValue(i);
                    if (IsOnion((CharacterMaster)masterField.GetValue(entry))) continue;
                    if (kept != i) buffer.SetValue(entry, kept);
                    kept++;
                }
                countField.SetValue(set, kept);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Onion ally card filter: {e.Message}");
            }
        }

        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            foreach (var card in FindObjectsOfType<AllyCardController>())
                if (IsOnion(card.sourceMaster) && card.gameObject.activeSelf) card.gameObject.SetActive(false);
        }
    }
}
