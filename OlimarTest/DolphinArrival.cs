using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // A survivor spawn state, independent of any future ship-repair artifact. RoR2 uses
    // preferredInitialStateType when no preferredPodPrefab is supplied.
    public sealed class DolphinArrival : BaseState
    {
        OlimarDriver driver;
        Vector3 anchor;
        bool protectedBody;

        public override void OnEnter()
        {
            base.OnEnter();
            driver = GetComponent<OlimarDriver>();
            anchor = transform.position;
            if (driver) driver.Arriving = true;
            if (NetworkServer.active && characterBody)
            {
                characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
                protectedBody = true;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (characterMotor) characterMotor.velocity = Vector3.zero;
            if (isAuthority) transform.position = anchor;
            // The host finishes when the Dolphin lands. Clients follow the replicated
            // state transition; they don't need a second native simulation.
            if (isAuthority && ((!driver || !driver.Arriving) || fixedAge > 20f))
            {
                if (driver && driver.Arriving)
                    Plugin.Log.LogWarning("Dolphin arrival timed out waiting for the native sim; releasing Olimar.");
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            Ror2Common.SpawnMusic.PlayOnce(characterBody);
            if (driver) driver.Arriving = false;
            if (protectedBody && characterBody) characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Death;
    }
}
