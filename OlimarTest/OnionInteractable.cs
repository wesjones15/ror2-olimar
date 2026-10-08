using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // An Onion as a RoR2 interactable, so standing under it shows RoR2's own interact prompt
    // and the interact button opens the Onion menu (NaviContainerState, naviState.cpp: the
    // action button within reach of the Onion's 'cont' sphere). It has no collider: RoR2's
    // aim and proximity search never find it. OlimarDriver hands it to his InteractionDriver
    // as its interactableOverride while the sim says he's in reach.
    internal sealed class OnionInteractable : MonoBehaviour, IInteractable
    {
        public int onion;

        public static OnionInteractable Make(int onion, Vector3 at, Transform parent)
        {
            var go = new GameObject($"Onion {onion} (interact)");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            // Opening it isn't a chest: no Fireworks or other on-interact items.
            go.AddComponent<InteractionProcFilter>().shouldAllowOnInteractionBeginProc = false;
            var it = go.AddComponent<OnionInteractable>();
            it.onion = onion;
            return it;
        }

        static OlimarDriver Driver(Interactor activator) => activator ? activator.GetComponent<OlimarDriver>() : null;

        public string GetContextString(Interactor activator) => "Open the Onion";

        public Interactability GetInteractability(Interactor activator)
        {
            var d = Driver(activator);
            return d && d.CanOpenOnion(onion) ? Interactability.Available : Interactability.Disabled;
        }

        public void OnInteractionBegin(Interactor activator)
        {
            var d = Driver(activator);
            if (d) d.OpenOnionMenu(onion);
        }

        public bool ShouldIgnoreSpherecastForInteractibility(Interactor activator) => true;

        public bool ShouldShowOnScanner() => false;
    }
}
