using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // What Pikmin 2's Purple and White do to RoR2's enemies (the sim's Pounded, Grounded and PoisonedEater events):
    // a Purple that lands beside a flying enemy brings it to the ground for a while, and a White that is eaten
    // poisons what ate it (EnemyBase::eatWhitePikminCallBack).
    internal partial class PikminWorld
    {
        /// The sim id of `captain`'s Dolphin, or -1 (the server's: clients do not know whose ship is whose).
        internal int ShipOfCaptain(int captain) => dolphins.TryGetValue(captain, out var d) ? d.id : -1;

        /// `Grounded`: a thrown Purple's pound reached a flying creature (the sim's `earthquake` reaches the flying
        /// too); it is stunned and pulled down for Combat/Pikmin.PoundGroundSeconds.
        void GroundCreature(in Native.Event e)
        {
            float seconds = Plugin.PoundGroundSeconds.Value;
            if (seconds <= 0f || e.has_creature == 0 || !bodies.TryGetValue(e.creature, out var body) || !body) return;
            if (!body.healthComponent || !body.healthComponent.alive) return;
            PikminGrounded.Apply(body, seconds);
        }

        /// `PoisonedEater`: the eater takes `Pikmin.WhitePoisonDamage` points of Pikmin attack power as one hit from
        /// the White that was eaten (a Pikmin 2 enemy takes 300 by default).
        void PoisonEater(in Native.Event e)
        {
            float damage = Plugin.WhitePoisonDamage.Value;
            if (damage <= 0f || e.has_creature == 0) return;
            Hit(e.creature, damage, -1, Native.SourcePiki, e.a);
        }
    }

    // A flying enemy held to the ground: stunned, and pushed down (a CharacterMotor's velocity, or a rigidbody given weight)
    // while the timer runs, then left as it was. Its own flight takes over again when the stun ends.
    internal sealed class PikminGrounded : MonoBehaviour
    {
        CharacterBody body;
        CharacterMotor motor;
        Rigidbody rigid;
        bool active, hadGravity;
        float until;

        public static void Apply(CharacterBody body, float seconds)
        {
            var held = body.GetComponent<PikminGrounded>();
            if (!held) held = body.gameObject.AddComponent<PikminGrounded>();
            held.Begin(body, seconds);
        }

        void Begin(CharacterBody b, float seconds)
        {
            body = b;
            if (!active)
            {
                motor = b.characterMotor;
                rigid = b.GetComponent<Rigidbody>();
                hadGravity = rigid && rigid.useGravity;
                active = true;
            }
            until = Time.fixedTime + seconds;
            var hurt = b.GetComponent<SetStateOnHurt>();
            if (hurt && hurt.canBeStunned) hurt.SetStun(seconds);
            Hold();
        }

        void FixedUpdate()
        {
            if (!active) return;
            if (!body || !body.healthComponent || !body.healthComponent.alive || Time.fixedTime >= until)
            {
                Release();
                return;
            }
            Hold();
        }

        void Hold()
        {
            // Straight down, a little faster than a fall; the motor stops it at the floor.
            if (motor)
            {
                var v = motor.velocity;
                motor.velocity = new Vector3(v.x * 0.5f, Mathf.Min(v.y, -30f), v.z * 0.5f);
            }
            if (rigid && !rigid.isKinematic)
            {
                rigid.useGravity = true;
                // A hover has no weight to speak of; pull it down like something dropped.
                if (rigid.velocity.y > -12f) rigid.AddForce(Physics.gravity * 2f, ForceMode.Acceleration);
            }
        }

        void Release()
        {
            active = false;
            if (rigid) rigid.useGravity = hadGravity;
        }

        void OnDestroy()
        {
            if (active) Release();
        }
    }
}
