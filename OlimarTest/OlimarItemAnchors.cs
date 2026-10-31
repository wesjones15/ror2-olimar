using System;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    /// Olimar's body attachment points, read per captain from the native view. The view maps
    /// them per skin (Olimar, Louie, the Pianta, Captain Pikachu), so item displays follow
    /// whichever body is drawn. Parts a rig lacks (Olimar has one joint per limb) fall back to
    /// the nearest joint that exists rather than disappearing.
    internal sealed class OlimarItemAnchors : IItemAnchorSource
    {
        readonly IntPtr view;
        readonly uint captain;
        readonly float[] matrix = new float[16];

        public OlimarItemAnchors(IntPtr view, uint captain)
        {
            this.view = view;
            this.captain = captain;
        }

        public void BeginFrame() { }

        public bool TryLocalPosition(ItemAnchor anchor, out Vector3 local)
        {
            if (Try(anchor, out local)) return true;
            switch (anchor)
            {
                case ItemAnchor.KneeL: return Try(ItemAnchor.LegL, out local);
                case ItemAnchor.KneeR: return Try(ItemAnchor.LegR, out local);
                case ItemAnchor.FootL: return Try(ItemAnchor.LegL, out local);
                case ItemAnchor.FootR: return Try(ItemAnchor.LegR, out local);
                case ItemAnchor.ShoulderL:
                case ItemAnchor.ShoulderR: return Try(ItemAnchor.Chest, out local);
                case ItemAnchor.ElbowL: return Try(ItemAnchor.HandL, out local);
                case ItemAnchor.ElbowR: return Try(ItemAnchor.HandR, out local);
                default: return false;
            }
        }

        bool Try(ItemAnchor anchor, out Vector3 local)
        {
            local = default;
            if (view == IntPtr.Zero) return false;
            try
            {
                if (Native.pik_view_anchor(view, captain, (uint)anchor, matrix) != 1) return false;
            }
            catch (Exception) { return false; }
            local = new Vector3(matrix[12], matrix[13], matrix[14]);
            return true;
        }
    }
}
