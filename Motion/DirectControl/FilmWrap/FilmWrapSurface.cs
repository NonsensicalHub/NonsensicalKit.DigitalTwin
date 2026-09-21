using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>立方体（可选圆角）水平截面采样，供缠膜路径与膜带共用。</summary>
    public static class FilmWrapSurface
    {
        public static Vector3 GetCenterWorld(Transform cargo, Vector3 centerLocal)
        {
            return cargo != null ? cargo.TransformPoint(centerLocal) : centerLocal;
        }

        public static void SampleCuboid(
            Transform cargo,
            Vector3 cuboidSize,
            Vector3 centerLocal,
            float surfaceOffset,
            float cornerRadius,
            Vector3 worldRadial,
            float localY,
            out Vector3 point,
            out Vector3 normal)
        {
            Vector3 size = cuboidSize;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));

            float hx = size.x * 0.5f + surfaceOffset;
            float hz = size.z * 0.5f + surfaceOffset;
            float radius = Mathf.Min(cornerRadius, surfaceOffset);
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(hx, hz) - 0.001f);

            Vector3 localDir = cargo != null
                ? cargo.InverseTransformDirection(worldRadial)
                : worldRadial;
            localDir.y = 0f;
            if (localDir.sqrMagnitude < 1e-8f)
                localDir = Vector3.right;
            else
                localDir.Normalize();

            Vector2 dir2 = new Vector2(localDir.x, localDir.z);
            Vector2 p2 = PointOnRoundedRect(dir2, hx, hz, radius);
            Vector2 n2 = NormalOnRoundedRect(p2, hx, hz, radius);

            float hy = size.y * 0.5f + surfaceOffset;
            float clampedY = Mathf.Clamp(localY, -hy, hy);

            Vector3 localPoint = centerLocal + new Vector3(p2.x, clampedY, p2.y);
            Vector3 localNormal = new Vector3(n2.x, 0f, n2.y);
            if (localNormal.sqrMagnitude < 1e-8f)
                localNormal = localDir;
            else
                localNormal.Normalize();

            if (cargo != null)
            {
                point = cargo.TransformPoint(localPoint);
                normal = cargo.TransformDirection(localNormal).normalized;
            }
            else
            {
                point = localPoint;
                normal = localNormal;
            }
        }

        /// <summary>0=+X, 1=-X, 2=+Z, 3=-Z（货物本地水平面）。</summary>
        public static int GetCuboidFace(
            Transform cargo,
            Vector3 cuboidSize,
            float surfaceOffset,
            Vector3 worldRadial)
        {
            Vector3 local = cargo != null
                ? cargo.InverseTransformDirection(worldRadial)
                : worldRadial;
            local.y = 0f;
            if (local.sqrMagnitude < 1e-8f)
                return 0;
            local.Normalize();

            float hx = Mathf.Max(0.01f, Mathf.Abs(cuboidSize.x) * 0.5f) + surfaceOffset;
            float hz = Mathf.Max(0.01f, Mathf.Abs(cuboidSize.z) * 0.5f) + surfaceOffset;
            float tx = hx / Mathf.Max(Mathf.Abs(local.x), 1e-8f);
            float tz = hz / Mathf.Max(Mathf.Abs(local.z), 1e-8f);
            if (tx <= tz)
                return local.x >= 0f ? 0 : 1;
            return local.z >= 0f ? 2 : 3;
        }

        public static Vector3 GetCornerRadial(Transform cargo, int faceA, int faceB)
        {
            float sx = 0f;
            float sz = 0f;
            ApplyFaceSign(faceA, ref sx, ref sz);
            ApplyFaceSign(faceB, ref sx, ref sz);
            if (Mathf.Abs(sx) < 0.5f || Mathf.Abs(sz) < 0.5f)
                return Vector3.zero;

            Vector3 local = new Vector3(sx, 0f, sz).normalized;
            return cargo != null ? cargo.TransformDirection(local) : local;
        }

        public static int PickIntermediateFace(int fromFace, int toFace, Vector3 fromRadial, Vector3 toRadial)
        {
            Vector3 cross = Vector3.Cross(fromRadial, toRadial);
            bool ccw = cross.y >= 0f;
            int[] order = { 0, 2, 1, 3 };
            int fi = System.Array.IndexOf(order, fromFace);
            if (fi < 0)
                return toFace;
            return ccw ? order[(fi + 1) % 4] : order[(fi + 3) % 4];
        }

        private static void ApplyFaceSign(int face, ref float sx, ref float sz)
        {
            switch (face)
            {
                case 0: sx = 1f; break;
                case 1: sx = -1f; break;
                case 2: sz = 1f; break;
                case 3: sz = -1f; break;
            }
        }

        public static Vector2 PointOnRoundedRect(Vector2 dir, float hx, float hz, float radius)
        {
            dir.Normalize();
            float ax = Mathf.Abs(dir.x);
            float az = Mathf.Abs(dir.y);

            if (radius <= 1e-6f)
            {
                float t = Mathf.Min(
                    hx / Mathf.Max(ax, 1e-8f),
                    hz / Mathf.Max(az, 1e-8f));
                return dir * t;
            }

            float ix = hx - radius;
            float iz = hz - radius;

            if (ax > 1e-8f)
            {
                float t = hx / ax;
                float z = t * dir.y;
                if (Mathf.Abs(z) <= iz + 1e-5f)
                    return new Vector2(Mathf.Sign(dir.x) * hx, z);
            }

            if (az > 1e-8f)
            {
                float t = hz / az;
                float x = t * dir.x;
                if (Mathf.Abs(x) <= ix + 1e-5f)
                    return new Vector2(x, Mathf.Sign(dir.y) * hz);
            }

            Vector2 corner = new Vector2(Mathf.Sign(dir.x) * ix, Mathf.Sign(dir.y) * iz);
            float b = Vector2.Dot(dir, corner);
            float disc = b * b - (corner.sqrMagnitude - radius * radius);
            float tHit = b + Mathf.Sqrt(Mathf.Max(0f, disc));
            return dir * tHit;
        }

        public static Vector2 NormalOnRoundedRect(Vector2 point, float hx, float hz, float radius)
        {
            if (radius > 1e-6f)
            {
                float ix = hx - radius;
                float iz = hz - radius;
                if (Mathf.Abs(point.x) > ix - 1e-4f && Mathf.Abs(point.y) > iz - 1e-4f)
                {
                    Vector2 c = new Vector2(Mathf.Sign(point.x) * ix, Mathf.Sign(point.y) * iz);
                    Vector2 n = point - c;
                    if (n.sqrMagnitude > 1e-8f)
                        return n.normalized;
                }
            }

            if (Mathf.Abs(point.x) / Mathf.Max(hx, 1e-8f) >= Mathf.Abs(point.y) / Mathf.Max(hz, 1e-8f))
                return new Vector2(Mathf.Sign(point.x), 0f);
            return new Vector2(0f, Mathf.Sign(point.y));
        }
    }
}
