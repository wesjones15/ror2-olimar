using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Orbs;
using RoR2.Skills;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // RoR2's items on Olimar, his Pikmin and the Onion (pikmin-main/DOCS/
    // olimar_pikmin_ror2_unified_ruleset.md). Most items need nothing here: damage, crit, health,
    // armour and every ordinary on-hit and on-kill item work through RoR2, because Pikmin hits
    // are RoR2 damage from Olimar (PikminWorld.Hit) and their kills are his. What's here:
    //
    // - His actions sit in RoR2's skill slots (OlimarSkills, ror2-common's NativeSkills), so
    //   RoR2 keeps their charges and the slot items work as for any survivor: the primary is
    //   his throw and punch (Shuriken; Visions of Heresy fires on each), the secondary his
    //   whistle (Hooks of Heresy), the utility the Onion's relocation (each lift or landing
    //   spends a charge; Hardlight adds charges and shortens the recharge; Bandolier restocks;
    //   Brainstalks makes it free), the special the Onion's pluckers (spent when one goes out).
    // - Cooldown items (Alien Head, Purity) as a ratio from his primary slot: sprouts grow
    //   faster, the Onion sends seeds out sooner, separated Pikmin regroup sooner, the
    //   plucker and emergency Pikmin come sooner (PikminWorld.Cooldowns).
    // - Attack speed: his punches and plucks and his Pikmin's attacks. Move speed: his run
    //   (PikminWorld) and, scaled, his Pikmin's.
    // - Hopoo Feather: mid-air jumps his squad repeats. Wax Quail: a sprinting jump carries
    //   him and the squad's repeat of it further. H3AD-5T v2: jump in mid-air with no jumps
    //   left to stomp; his landing sets off the item's blast, and the Pikmin that came down
    //   with him one blast between them (one proc roll, damage per Pikmin, capped).
    // - Pikmin healing: his regeneration, scaled, and healing wards they stand in (Bustling
    //   Fungus around a landed Onion or a still Olimar); N'kuhana's Opinion stores what they
    //   heal. Healing Olimar doesn't heal them.
    // - Genesis Loop and Razorwire react to Pikmin being hurt, scaled and throttled.
    // - Backup Magazine: more seeds per delivery. Wake of Vultures: the Pikmin fighting an elite
    //   when it dies take its affix (its on-hit effect) for the item's time.
    //
    // Runs where the sim does (the server).
    internal sealed class OlimarItems
    {
        readonly OlimarDriver driver;
        CharacterBody Body => driver ? driver.Body : null;

        float cooldownHeld = 1f;
        float stomperReadyAt, genesisReadyAt, razorReadyAt, whistleSeenAt = -10f;
        // Pikmin landing from a stomp, gathered into one blast.
        int stompCount;
        Vector3 stompSum;
        float stompFlushAt;
        bool wasGrounded = true;

        /// Cooldown items' boost (1 / cooldown ratio), for the sim's timers.
        public float CooldownBoost { get; private set; } = 1f;

        public OlimarItems(OlimarDriver driver)
        {
            this.driver = driver;
        }

        GenericSkill Slot(SkillSlot s) => Body && Body.skillLocator ? Body.skillLocator.GetSkill(s) : null;

        // ---- each step --------------------------------------------------------------------

        /// A kill with the strike: its charge comes straight back.
        public void StrikeKilled()
        {
            var slot = Slot(SkillSlot.Special);
            if (NativeSkills.Holds(slot, OlimarSkills.Strike) && slot.stock < slot.maxStock) slot.AddOneStock();
        }

        /// The Special slot can fire the strike now: it has a charge (or Brainstalks makes it free), or
        /// another skill overrides the slot and is ready.
        public bool StrikeReady()
        {
            var slot = Slot(SkillSlot.Special);
            if (!slot) return true;
            if (!NativeSkills.Holds(slot, OlimarSkills.Strike)) return slot.CanExecute();
            return NativeSkills.Unlimited(Body) || slot.stock >= 1;
        }

        /// Before the sim steps: his stats into the sim.
        public void BeforeStep(IntPtr sim, uint captain)
        {
            var body = Body;
            if (!body) return;
            var stats = MinionStats.Of(body);
            float attack = Plugin.AttackSpeedFromItems.Value ? Mathf.Clamp(stats.attackSpeed, 0.1f, 10f) : 1f;
            Native.pik_sim_set_captain_attack_rate(sim, captain, attack);
            // Pikmin keep up with his sprint, and take a share of his speed items.
            float move = 1f + (stats.moveSpeed - 1f) * Mathf.Max(0f, Plugin.MinionMoveScale.Value);
            if (body.isSprinting) move *= Mathf.Max(1f, body.sprintingSpeedMultiplier);
            Native.pik_sim_set_squad_speed(sim, captain, Mathf.Max(0.1f, move));
            // Jump power and acceleration items (Faraday Spur, Hex of Leaping, Hex of Acceleration).
            Native.pik_sim_set_captain_motion(sim, captain, NativeBody.JumpScale(body), NativeBody.AccelerationScale(body));
            float baselineJump = body.baseJumpPower + body.levelJumpPower * Mathf.Max(0f, body.level - 1f);
            Native.pik_sim_set_captain_jump_height(sim, captain,
                OlimarJump.Height(body.jumpPower, baselineJump, Physics.gravity.y, UnityHost.Scale));
            Native.pik_sim_set_captain_jumps(sim, captain, (uint)Mathf.Max(0, body.maxJumpCount - 1),
                (byte)(ByName.Count(body, "FallBoots") > 0 ? 1 : 0));
            CooldownBoost = SkillCooldown.Boost(Slot(SkillSlot.Primary), Plugin.CooldownExponent.Value, ref cooldownHeld);

            // His regeneration, as a share of his health, regrows theirs.
            float regen = stats.regenFraction * 100f * Mathf.Max(0f, Plugin.MinionHealScale.Value) * Time.fixedDeltaTime;
            if (regen > 0f) Native.pik_sim_heal_squad(sim, captain, regen);
            FlushStomp();
        }

        /// After a tick, with his state.
        public void Ticked(in Native.Captain c)
        {
            var motor = Body ? Body.characterMotor : null;
            bool grounded = c.on_ground != 0;
            if (motor && grounded && !wasGrounded) motor.jumpCount = 0;
            wasGrounded = grounded;
        }

        // ---- his events -------------------------------------------------------------------

        public void OnEvent(in Native.Event e, PikminWorld world)
        {
            var body = Body;
            if (!body) return;
            switch (e.kind)
            {
                case Native.EventKind.Thrown:
                case Native.EventKind.Punched:
                    // Shuriken's trigger (once per throw, at release; once per punch).
                    NativeSkills.Spend(Slot(SkillSlot.Primary), OlimarSkills.Throw);
                    NativeBody.MarkCombat(body);
                    break;
                case Native.EventKind.Whistle:
                    // Every tick it calls; one use per blow.
                    if (Time.fixedTime - whistleSeenAt > 0.4f)
                        NativeSkills.Spend(Slot(SkillSlot.Secondary), OlimarSkills.Whistle);
                    whistleSeenAt = Time.fixedTime;
                    break;
                case Native.EventKind.CaptainJumped:
                    if (body.characterMotor) body.characterMotor.jumpCount++;
                    // RoR2's jump event, for the items that listen (Faraday Spur, Earthbound
                    // Vessel, Hex of Leaping).
                    NativeBody.Of(body.gameObject)?.Jumped();
                    if (e.c != 0) Feather(body);
                    else if (body.isSprinting) Quail(world, body, (uint)e.a);
                    break;
                case Native.EventKind.CaptainStomped:
                    Stomp(body, UnityHost.ToUnity(e.pos), e.value * UnityHost.Scale);
                    stompFlushAt = Time.fixedTime + 0.3f;
                    break;
                case Native.EventKind.CaptainLanded:
                    if (e.c == 0) FallDamage(body, e.value * UnityHost.Scale);
                    break;
                case Native.EventKind.PikiStomped:
                    stompCount++;
                    stompSum += UnityHost.ToUnity(e.pos);
                    stompFlushAt = Mathf.Max(stompFlushAt, Time.fixedTime + 0.15f);
                    break;
                case Native.EventKind.PikiHurt:
                    PikiHurt(world, e.a, e.value);
                    break;
                case Native.EventKind.StrikeStarted:
                    NativeSkills.Spend(Slot(SkillSlot.Special), OlimarSkills.Strike);
                    NativeBody.MarkCombat(body);
                    break;
                case Native.EventKind.PelletDelivered:
                    BackupMagazine(world, (uint)e.b, (uint)e.c);
                    break;
            }
        }

        // ---- fall damage --------------------------------------------------------------------

        /// He touched down after falling `drop` metres. The sim moves him, so RoR2's character
        /// motor never sees the landing; hand its server hook the speed he'd have hit the ground
        /// at under RoR2's gravity (v = sqrt(2 g h)). The hook turns the excess over
        /// `jumpPower + 20` into a share of max health and applies fall immunity (H3AD-5T),
        /// armour, Frailty and Eclipse's lethal falls as for any survivor.
        static void FallDamage(CharacterBody body, float drop)
        {
            if (!Plugin.FallDamageEnabled.Value || drop <= 0f) return;
            var hc = body.healthComponent;
            var events = GlobalEventManager.instance;
            if (!events || !hc || !hc.alive) return;
            float speed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * drop);
            if (speed <= body.jumpPower + 20f) return;
            events.OnCharacterHitGroundServer(body, new CharacterMotor.HitGroundInfo { velocity = Vector3.down * speed, position = body.footPosition, ownerBodyObject = body.gameObject, isValidForEffect = true });
            if (Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"Olimar landed from {drop:F1} m at {speed:F1} m/s");
        }

        // ---- Hopoo Feather and Wax Quail --------------------------------------------------

        static GameObject featherEffect;

        static void Feather(CharacterBody body)
        {
            if (!featherEffect) featherEffect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/FeatherEffect");
            if (featherEffect) EffectManager.SpawnEffect(featherEffect, new EffectData { origin = body.footPosition }, true);
        }

        /// GenericCharacterMain.ProcessJump's Wax Quail, as a share of a full run: the boost's
        /// speed, `sqrt(10 * stacks * accel * air control)`, over his move speed.
        static void Quail(PikminWorld world, CharacterBody body, uint captain)
        {
            int n = ByName.Count(body, "JumpBoost");
            if (n <= 0) return;
            var motor = body.characterMotor;
            float accel = body.acceleration * (motor ? motor.airControl : 0.25f);
            float speed = Mathf.Max(1f, body.moveSpeed);
            if (accel <= 0f) return;
            float boost = 1f + Mathf.Sqrt(10f * n * accel) / speed;
            Native.pik_sim_boost_captain_jump(world.SimHandle, captain, boost);
        }

        // ---- H3AD-5T v2 -------------------------------------------------------------------

        /// His stomp landed `drop` metres down at `at`: the item's blast, on its cooldown
        /// (HeadstompersFall.DoStompExplosionAuthority's numbers).
        void Stomp(CharacterBody body, Vector3 at, float drop)
        {
            int n = ByName.Count(body, "FallBoots");
            if (n <= 0 || Time.fixedTime < stomperReadyAt) return;
            float t = Mathf.InverseLerp(0f, Stat("HeadstompersFall", "maxDistance", 30f), drop);
            float damage = Mathf.Lerp(Stat("HeadstompersFall", "minimumDamageCoefficient", 10f),
                Stat("HeadstompersFall", "maximumDamageCoefficient", 100f), t);
            float radius = Mathf.Lerp(Stat("HeadstompersFall", "minimumRadius", 5f),
                Stat("HeadstompersFall", "maximumRadius", 100f), t);
            stomperReadyAt = Time.fixedTime + Stat("HeadstompersCooldown", "baseDuration", 10f) / n;
            Blast(body, at, damage, radius, 1f);
        }

        /// The Pikmin that stomped down with him: one blast where they landed, its damage per
        /// Pikmin (capped), so a hundred Pikmin aren't a hundred full-power stomps.
        void FlushStomp()
        {
            if (stompCount == 0 || Time.fixedTime < stompFlushAt) return;
            var body = Body;
            int n = Mathf.Min(stompCount, Mathf.Max(1, Plugin.StompPikminCap.Value));
            var centre = stompSum / stompCount;
            stompCount = 0;
            stompSum = Vector3.zero;
            if (!body || ByName.Count(body, "FallBoots") <= 0) return;
            Blast(body, centre, Plugin.StompPikminDamage.Value * n, 2f + 0.15f * n, 1f);
        }

        static void Blast(CharacterBody body, Vector3 at, float coefficient, float radius, float proc)
        {
            new BlastAttack
            {
                attacker = body.gameObject,
                inflictor = body.gameObject,
                teamIndex = TeamComponent.GetObjectTeam(body.gameObject),
                position = at,
                procCoefficient = proc,
                radius = radius,
                baseForce = 200f * Mathf.Min(coefficient, 20f),
                bonusForce = Vector3.up * 2000f,
                baseDamage = body.damage * coefficient,
                falloffModel = BlastAttack.FalloffModel.SweetSpot,
                crit = body.RollCrit(),
                damageColorIndex = DamageColorIndex.Item,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
            }.Fire();
            var fx = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/BootShockwave");
            if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = at, scale = radius }, true);
        }

        /// A RoR2 entity state's tuning (static, from its config), or `fallback`.
        static float Stat(string state, string field, float fallback)
        {
            var t = ByName.Type("EntityStates.Headstompers." + state);
            var f = t?.GetField(field, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            return f != null && f.GetValue(null) is float v && v > 0f ? v : fallback;
        }

        // ---- a Pikmin hurt: Genesis Loop, Razorwire --------------------------------------

        void PikiHurt(PikminWorld world, uint piki, float damage)
        {
            var body = Body;
            if (piki >= world.PikiCount) return;
            var p = world.Pikis[piki];
            var at = UnityHost.ToUnity(p.pos) + Vector3.up * 0.3f;
            float scale = Mathf.Max(0f, Plugin.ItemEffectScale.Value);

            // Genesis Loop: a Pikmin dropping below a quarter of its health may go off, on the
            // item's recharge (30 s / (1 + stacks)), scaled down from the Vagrant's nova.
            int genesis = ByName.Count(body, "NovaOnLowHealth");
            if (genesis > 0 && p.health < 25f && p.health + damage >= 25f && Time.fixedTime >= genesisReadyAt)
            {
                genesisReadyAt = Time.fixedTime + 30f / (1f + genesis);
                Blast(body, at, 60f * scale, Mathf.Max(4f, 100f * scale * 0.5f), 1f);
                var fx = NovaEffect();
                if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = at, scale = 12f }, true);
            }

            // Razorwire: razors at the nearest enemies, at most once per interval per Olimar.
            int thorns = ByName.Count(body, "Thorns");
            if (thorns > 0 && Time.fixedTime >= razorReadyAt)
            {
                razorReadyAt = Time.fixedTime + Mathf.Max(0.05f, Plugin.RazorwireInterval.Value);
                int targets = Mathf.Min(5 + 2 * (thorns - 1), 3);
                float range = 25f + 10f * (thorns - 1);
                var search = new BullseyeSearch
                {
                    searchOrigin = at,
                    maxDistanceFilter = range,
                    teamMaskFilter = TeamMask.GetUnprotectedTeams(TeamComponent.GetObjectTeam(body.gameObject)),
                    sortMode = BullseyeSearch.SortMode.Distance,
                    filterByLoS = false,
                };
                search.RefreshCandidates();
                int fired = 0;
                foreach (var hb in search.GetResults())
                {
                    if (fired >= targets) break;
                    if (!hb || !hb.healthComponent || hb.healthComponent.body == body) continue;
                    OrbManager.instance.AddOrb(new LightningOrb
                    {
                        attacker = body.gameObject,
                        bouncedObjects = null,
                        bouncesRemaining = 0,
                        damageCoefficientPerBounce = 1f,
                        damageColorIndex = DamageColorIndex.Item,
                        damageValue = body.damage * 1.6f * scale,
                        isCrit = body.RollCrit(),
                        lightningType = LightningOrb.LightningType.RazorWire,
                        origin = at,
                        procChainMask = default,
                        procCoefficient = 0.5f,
                        range = 0f,
                        teamIndex = TeamComponent.GetObjectTeam(body.gameObject),
                        target = hb,
                    });
                    fired++;
                }
            }
        }

        static GameObject novaEffect;
        static bool novaLooked;

        static GameObject NovaEffect()
        {
            if (novaLooked) return novaEffect;
            novaLooked = true;
            var t = ByName.Type("EntityStates.VagrantNovaItem.DetonateState");
            var f = t?.GetField("explosionEffectPrefab", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            novaEffect = f?.GetValue(null) as GameObject;
            return novaEffect;
        }

        // ---- healing ---------------------------------------------------------------------

        static System.Reflection.FieldInfo soulPool;
        static bool soulLooked;

        /// Pikmin healed `pikmin` health (sim units, of 100 each): N'kuhana's Opinion stores it
        /// as if he had healed that much (scaled), for its skulls.
        public void PikminHealed(float pikmin)
        {
            var body = Body;
            if (!body || pikmin <= 0f || ByName.Count(body, "NovaOnHeal") <= 0) return;
            if (!soulLooked)
            {
                soulLooked = true;
                soulPool = ByName.Field(typeof(HealthComponent), "devilOrbHealPool");
                if (soulPool == null) Plugin.Log.LogWarning("No devilOrbHealPool in this RoR2; Pikmin healing doesn't feed N'kuhana's Opinion");
            }
            var hc = body.healthComponent;
            if (soulPool == null || !hc) return;
            float hp = pikmin / 100f * Plugin.PikminHealth.Value * body.maxHealth * Mathf.Max(0f, Plugin.MinionHealScale.Value);
            soulPool.SetValue(hc, (float)soulPool.GetValue(hc) + hp);
        }

        // ---- Backup Magazine --------------------------------------------------------------

        /// A delivery to his Onion: Backup Magazine adds seeds of the same colour.
        void BackupMagazine(PikminWorld world, uint onion, uint colour)
        {
            int n = ByName.Count(Body, "SecondarySkillMagazine");
            int extra = n * Mathf.Max(0, Plugin.BackupSeedsPerStack.Value);
            if (extra > 0 && colour < 3) Native.pik_sim_give_onion_seeds(world.SimHandle, onion, colour, (uint)extra);
        }
    }

    // Wake of Vultures for Pikmin: the ones fighting an elite when it dies carry its affix for
    // the item's time, and their hits land its on-hit effect (only status effects RoR2's own
    // elites put on what they hit: Blazing burns, Glacial slows, Malachite stops healing,
    // Perfected cripples).
    internal sealed class PikminAffixes
    {
        struct Held
        {
            public BuffIndex[] buffs;
            public float until;
        }

        readonly Dictionary<uint, Held> held = new Dictionary<uint, Held>();

        public void Clear() => held.Clear();
        public void Forget(uint id) => held.Remove(id);

        /// `victim` (an elite) died to `killer`: its fighters among `pikis` take its affixes.
        public void EliteDied(CharacterBody victim, CharacterBody killer, Native.Piki[] pikis, int count, ulong victimId)
        {
            int n = ByName.Count(killer, "HeadHunter");
            if (n <= 0 || !victim || !victim.isElite) return;
            var buffs = new List<BuffIndex>();
            for (int i = 0; i < BuffCatalog.buffCount; i++)
            {
                var b = (BuffIndex)i;
                var def = BuffCatalog.GetBuffDef(b);
                if (def && def.isElite && victim.HasBuff(b)) buffs.Add(b);
            }
            if (buffs.Count == 0) return;
            float until = Time.fixedTime + 3f + 5f * n;
            int took = 0;
            for (int i = 0; i < count; i++)
            {
                var p = pikis[i];
                if (p.alive == 0 || p.state != Native.PikiState.Attack || p.has_creature == 0 || p.creature != victimId) continue;
                held[p.id] = new Held { buffs = buffs.ToArray(), until = until };
                took++;
            }
            if (took > 0 && Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"{took} Pikmin took {victim.GetDisplayName()}'s affix for {until - Time.fixedTime:F0} s");
        }

        /// Pikmin `piki`'s hit (one that procs) lands its affix's effect.
        public void OnHit(uint piki, CharacterBody attacker, HealthComponent victim, float procCoefficient)
        {
            if (!held.TryGetValue(piki, out var h)) return;
            if (Time.fixedTime > h.until)
            {
                held.Remove(piki);
                return;
            }
            var vb = victim ? victim.body : null;
            if (!vb || !attacker) return;
            foreach (var b in h.buffs)
            {
                var def = BuffCatalog.GetBuffDef(b);
                string name = def ? def.name : "";
                switch (name)
                {
                    case "AffixRed":
                        DotController.InflictDot(vb.gameObject, attacker.gameObject, vb.mainHurtBox, default(ProcChainMask), DotController.DotIndex.Burn, 4f * procCoefficient, 1f);
                        break;
                    case "AffixWhite":
                        vb.AddTimedBuff(RoR2Content.Buffs.Slow80, 1.5f * procCoefficient);
                        break;
                    case "AffixPoison":
                        vb.AddTimedBuff(RoR2Content.Buffs.HealingDisabled, 8f * procCoefficient);
                        break;
                    case "AffixLunar":
                        vb.AddTimedBuff(RoR2Content.Buffs.Cripple, 3f * procCoefficient);
                        break;
                }
            }
        }
    }

    // Olimar's actions as RoR2 skills (see OlimarItems), made once at content load.
    internal static class OlimarSkills
    {
        public static NativeSkillDef Throw, Whistle, Ride, Strike;
        /// The Onion's pluckers: RoR2's passive slot, with no SkillDef of its own.
        public static Action<Sprite> SetPluckers;

        public static void Add(GameObject body, RoR2.ContentManagement.ContentPack pack)
        {
            var loc = body.GetComponent<SkillLocator>();
            Sprite Icon(SkillSlot s) => loc && loc.GetSkill(s) && loc.GetSkill(s).skillFamily
                ? loc.GetSkill(s).skillFamily.defaultSkillDef?.icon : null;
            Throw = NativeSkills.Make("OlimarThrow", "OLIMARTEST_THROW_NAME", "OLIMARTEST_THROW_DESC",
                Icon(SkillSlot.Primary), 0f, 1, true);
            Whistle = NativeSkills.Make("OlimarWhistle", "OLIMARTEST_WHISTLE_NAME", "OLIMARTEST_WHISTLE_DESC",
                Icon(SkillSlot.Secondary), 0f, 1, false);
            // The Utility slot is Onion Ride's. Raising and lowering the Onion (hold X) costs no charge.
            Ride = NativeSkills.Make("OlimarOnionRide", "OLIMARTEST_RIDE_NAME", "OLIMARTEST_RIDE_DESC",
                Icon(SkillSlot.Utility), Plugin.RideRecharge.Value, 1, false);
            // The dismiss strike: RoR2 keeps its cooldown (the sim's is off), so cooldown items
            // work and the HUD shows it.
            Strike = NativeSkills.Make("OlimarDismissStrike", "OLIMARTEST_STRIKE_NAME", "OLIMARTEST_STRIKE_DESC",
                Icon(SkillSlot.Special), Mathf.Max(0.1f, Plugin.StrikeCooldown.Value), 1, true);
            NativeSkills.SetFamily(body, SkillSlot.Primary, Throw, pack);
            NativeSkills.SetFamily(body, SkillSlot.Secondary, Whistle, pack);
            NativeSkills.SetFamily(body, SkillSlot.Utility, Ride, pack);
            NativeSkills.SetFamily(body, SkillSlot.Special, Strike, pack);
            // The Onion's pluckers have no button: the passive, listed with the four skills.
            if (loc)
            {
                loc.passiveSkill.enabled = true;
                loc.passiveSkill.skillNameToken = "OLIMARTEST_PLUCKERS_NAME";
                loc.passiveSkill.skillDescriptionToken = "OLIMARTEST_PLUCKERS_DESC";
                loc.passiveSkill.keywordToken = "";
                loc.passiveSkill.icon = Icon(SkillSlot.Utility);
                SetPluckers = sprite => loc.passiveSkill.icon = sprite;
            }
            OlimarSkillIcons.RequestAll();
        }

        public static void Tokens(Language lang)
        {
            lang.SetStringByToken("OLIMARTEST_THROW_NAME", "Pikmin Throw");
            lang.SetStringByToken("OLIMARTEST_THROW_DESC",
                "<style=cIsUtility>Press A</style> to throw a Pikmin at the cursor. <style=cIsUtility>Hold LT</style> to lock on; " +
                "<style=cIsUtility>D-pad left/right</style> picks its colour.");
            lang.SetStringByToken("OLIMARTEST_WHISTLE_NAME", "Whistle");
            lang.SetStringByToken("OLIMARTEST_WHISTLE_DESC",
                "<style=cIsUtility>Hold RT and press B</style> to call nearby Pikmin back.");
            lang.SetStringByToken("OLIMARTEST_RIDE_NAME", "Onion Ride");
            lang.SetStringByToken("OLIMARTEST_RIDE_DESC",
                "Stand in the Onion's beam and <style=cIsUtility>press LB</style> to board it (or <style=cIsUtility>RT + LB</style> from anywhere to call the Onion to you first), then steer it with the left stick for " +
                "<style=cIsDamage>10 seconds</style> (after up to 5 seconds of grace to let your Pikmin climb aboard). " +
                "<style=cIsUtility>Click the left stick</style> for a 2 second speed boost. <style=cIsUtility>Press A, B, X or Y</style> " +
                "to get out; your squad is sneezed out with you. The Onion can be hit while you ride. Cooldown: <style=cIsUtility>30 seconds</style>, starting when the ride ends. " +
                "Hold X for 0.6 seconds to raise or lower the Onion: that is free.");
            lang.SetStringByToken("OLIMARTEST_STRIKE_NAME", "Dismiss Strike");
            lang.SetStringByToken("OLIMARTEST_STRIKE_DESC",
                "<style=cIsUtility>Press RB</style> to send your Pikmin charging at nearby enemies.");
            lang.SetStringByToken("OLIMARTEST_PLUCKERS_NAME", "Onion Pluckers");
            lang.SetStringByToken("OLIMARTEST_PLUCKERS_DESC",
                "Unplucked sprouts are eventually pulled by the Onion and join your squad.");
        }
    }
}
