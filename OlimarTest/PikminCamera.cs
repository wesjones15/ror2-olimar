using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // A stand-in for Pikmin's camera in the Pikmin control scheme: it looks down at Olimar from
    // behind at one of three zoom levels and only turns when told to (L swings it round behind
    // him), so the stick's directions stay put while he walks. In the Orbit mode the right stick
    // tilts it from the top view down to Controls.LookUpDegrees above level (RoR2's own camera
    // looks up 70 degrees). Not a port of Pikmin's own camera (cameraMgr.cpp); its angles and
    // distances are eyeballed.
    //
    // A component on Olimar's body, not a plain object: RoR2's camera modes cast the override
    // camera to UnityEngine.Object for their null checks (CameraModePlayerBasic.
    // CollectLookInputInternal), which throws for anything else and stops the camera rig.
    internal class PikminCamera : MonoBehaviour, ICameraStateProvider
    {
        // (distance m, pitch deg) per zoom level, and the top view.
        static Vector2 Preset(int zoom, bool top)
        {
            Native.pik_camera_preset((uint)zoom, (byte)(top ? 1 : 0), out var preset);
            return new Vector2(preset.x, preset.y);
        }
        const float TurnSmooth = 0.25f;

        Transform target;
        float yaw, yawGoal, yawVel;
        int zoom = 1;
        bool top;
        float dist, pitch, distVel, pitchVel;
        float orbitPitch;

        public void Init(Transform target, float startYaw)
        {
            this.target = target;
            yaw = yawGoal = startYaw;
            dist = Preset(zoom, false).x;
            pitch = orbitPitch = Preset(zoom, false).y;
        }

        /// The camera's facing on the ground plane, for turning the stick into a world direction.
        public float Yaw => yaw;

        public void Recentre(float behindYaw) => yawGoal = yaw + Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, behindYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        public void CycleZoom()
        {
            top = false;
            zoom = (zoom + 1) % 3;
        }

        public void ToggleTop() => top = !top;

        public void Pan(Vector2 stick)
        {
            var a = new Native.Vec3(yawGoal, orbitPitch, 0f);
            // Positive pitch looks down; looking up is the config's angle negated (0: Pikmin's 10 down).
            float up = Mathf.Clamp(Plugin.LookUpDegrees.Value, 0f, 85f);
            Native.pik_camera_pan((uint)Plugin.Camera.Value, ref a, stick.x, stick.y, Time.deltaTime,
                up > 0f ? -up : 10f);
            yawGoal = a.x; orbitPitch = a.y;
        }

        /// World XZ direction for a stick held at `s`, relative to the camera.
        public Vector2 StickToWorld(Vector2 s)
        {
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(fwd.y, -fwd.x);
            return right * s.x + fwd * s.y;
        }

        public void GetCameraState(CameraRigController rig, ref CameraState state)
        {
            if (!target) return;
            float dt = Time.deltaTime;
            yaw = Mathf.SmoothDamp(yaw, yawGoal, ref yawVel, TurnSmooth, Mathf.Infinity, dt);
            var goal = Preset(zoom, top);
            if (Plugin.Camera.Value == CameraMode.Orbit) goal.y = orbitPitch;
            dist = Mathf.SmoothDamp(dist, goal.x, ref distVel, 0.3f, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, goal.y, ref pitchVel, 0.3f, Mathf.Infinity, dt);

            var rot = Quaternion.Euler(pitch, yaw * Mathf.Rad2Deg, 0f);
            var focus = target.position + Vector3.up * 0.5f;
            state.position = focus - rot * Vector3.forward * dist;
            // Keep the camera in front of terrain instead of inside slopes or walls.
            if (Physics.SphereCast(focus, 0.2f, (state.position - focus).normalized,
                out var hit, dist, UnityHost.Mask, QueryTriggerInteraction.Ignore))
                state.position = focus + (state.position - focus).normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
            state.rotation = rot;
            state.fov = 55f;
        }

        public bool IsUserLookAllowed(CameraRigController rig) => false;
        public bool IsUserControlAllowed(CameraRigController rig) => true;
        public bool IsHudAllowed(CameraRigController rig) => true;
    }
}
