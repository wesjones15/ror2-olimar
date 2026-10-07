using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // A stand-in for Pikmin's camera in the Pikmin control scheme: it looks down at Olimar from
    // behind at one of three zoom levels and only turns when told to (L swings it round behind
    // him), so the stick's directions stay put while he walks. Not a port of Pikmin's own
    // camera (cameraMgr.cpp); its angles and distances are eyeballed.
    internal class PikminCamera : ICameraStateProvider
    {
        // (distance m, pitch deg) per zoom level, and the top view.
        static readonly Vector2[] Zooms = { new Vector2(9f, 38f), new Vector2(14f, 48f), new Vector2(21f, 58f) };
        static readonly Vector2 TopView = new Vector2(24f, 82f);
        const float Fov = 55f;
        const float TurnSmooth = 0.25f;

        readonly Transform target;
        float yaw, yawGoal, yawVel;
        int zoom = 1;
        bool top;
        float dist, pitch, distVel, pitchVel;

        public PikminCamera(Transform target, float startYaw)
        {
            this.target = target;
            yaw = yawGoal = startYaw;
            dist = Zooms[zoom].x;
            pitch = Zooms[zoom].y;
        }

        /// The camera's facing on the ground plane, for turning the stick into a world direction.
        public float Yaw => yaw;

        public void Recentre(float behindYaw) => yawGoal = yaw + Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, behindYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        public void CycleZoom()
        {
            top = false;
            zoom = (zoom + 1) % Zooms.Length;
        }

        public void ToggleTop() => top = !top;

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
            var goal = top ? TopView : Zooms[zoom];
            dist = Mathf.SmoothDamp(dist, goal.x, ref distVel, 0.3f, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, goal.y, ref pitchVel, 0.3f, Mathf.Infinity, dt);

            var rot = Quaternion.Euler(pitch, yaw * Mathf.Rad2Deg, 0f);
            var focus = target.position + Vector3.up * 0.5f;
            state.position = focus - rot * Vector3.forward * dist;
            state.rotation = rot;
            state.fov = Fov;
        }

        public bool IsUserLookAllowed(CameraRigController rig) => false;
        public bool IsUserControlAllowed(CameraRigController rig) => true;
        public bool IsHudAllowed(CameraRigController rig) => true;
    }
}
