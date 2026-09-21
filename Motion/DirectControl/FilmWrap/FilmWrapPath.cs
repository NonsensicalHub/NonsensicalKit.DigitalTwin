using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>路径上的一个采样点。t 为工艺路径参数 0~1。</summary>
    public struct FilmWrapSample
    {
        public float t;
        public float pathLength;
        public Vector3 nozzleWorld;
        public Vector3 radialWorld;
        public float localY;

        public static FilmWrapSample Lerp(in FilmWrapSample a, in FilmWrapSample b, float f)
        {
            Vector3 radial = Vector3.Slerp(a.radialWorld, b.radialWorld, f);
            if (radial.sqrMagnitude < 1e-8f)
                radial = a.radialWorld;
            else
                radial.Normalize();

            return new FilmWrapSample
            {
                t = Mathf.Lerp(a.t, b.t, f),
                pathLength = Mathf.Lerp(a.pathLength, b.pathLength, f),
                nozzleWorld = Vector3.Lerp(a.nozzleWorld, b.nozzleWorld, f),
                radialWorld = radial,
                localY = Mathf.Lerp(a.localY, b.localY, f)
            };
        }
    }

    /// <summary>
    /// 缠绕路径：绕货物的螺旋采样。点存货物本地空间，求值时再变到世界空间。
    /// </summary>
    public class FilmWrapPath
    {
        private struct StoredSample
        {
            public float t;
            public float pathLength;
            public Vector3 nozzleLocal;
            public Vector3 radialLocal;
            public float localY;
        }

        public Transform Cargo;
        public Vector3 CuboidSize = new Vector3(1f, 1.2f, 1f);
        public Vector3 CuboidCenterLocal;
        public float SurfaceOffset = 0.04f;
        public float CornerRadius = 0.04f;
        public float OrbitRadius = 1.45f;
        public float BottomY = 0.2f;
        public float TopY = 1.55f;
        public float Revolutions = 6f;
        public bool UpThenSlightDown = true;
        public int SamplesPerRevolution = 64;

        private readonly List<StoredSample> _samples = new List<StoredSample>(512);
        private bool _baked;
        private int _bakeVersion;

        public float Orbit => OrbitRadius;
        public int SampleCount => _samples.Count;
        public int BakeVersion => _bakeVersion;
        public bool IsBaked => _baked && _samples.Count >= 2;

        public void Invalidate() => _baked = false;

        public void EnsureBaked()
        {
            if (!_baked || _samples.Count < 2)
                Bake();
        }

        public void Bake()
        {
            _samples.Clear();
            _baked = false;
            if (Cargo == null)
                return;

            int steps = Mathf.Max(8, SamplesPerRevolution) * Mathf.Max(1, Mathf.CeilToInt(Revolutions));
            bool sharpCorners = CornerRadius <= 1e-5f;
            int lastFace = -1;
            Vector3 lastRadialWorld = Vector3.right;
            Vector3 lastMid = Vector3.zero;
            bool hasLastMid = false;
            float pathLength = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 radialLocal = EvaluateRadialLocal(t);
                Vector3 radialWorld = ToWorldDirection(radialLocal);
                int face = FilmWrapSurface.GetCuboidFace(Cargo, CuboidSize, SurfaceOffset, radialWorld);

                if (sharpCorners && i > 0 && lastFace >= 0 && face != lastFace)
                {
                    float tCorner = Mathf.Lerp(_samples[_samples.Count - 1].t, t, 0.5f);
                    AppendCornerSamples(
                        lastRadialWorld, radialWorld, lastFace, face, tCorner,
                        ref pathLength, ref lastMid, ref hasLastMid);
                }

                AppendSample(t, radialLocal, ref pathLength, ref lastMid, ref hasLastMid);
                lastFace = face;
                lastRadialWorld = radialWorld;
            }

            _baked = _samples.Count >= 2;
            _bakeVersion++;
        }

        public FilmWrapSample Evaluate(float t)
        {
            EnsureBaked();
            if (_samples.Count == 0)
                return default;
            if (_samples.Count == 1)
                return ToWorld(_samples[0]);

            t = Mathf.Clamp01(t);
            int i = GetSpanIndex(t);
            StoredSample a = _samples[i];
            StoredSample b = _samples[i + 1];
            float dt = b.t - a.t;
            float f = dt > 1e-8f ? Mathf.Clamp01((t - a.t) / dt) : 1f;
            return FilmWrapSample.Lerp(ToWorld(a), ToWorld(b), f);
        }

        public FilmWrapSample GetSample(int index)
        {
            EnsureBaked();
            if (index < 0 || index >= _samples.Count)
                return default;
            return ToWorld(_samples[index]);
        }

        /// <summary>最后一个 t 不超过 progress 的样本下标；progress 过小返回 -1。</summary>
        public int GetLastIndexAtOrBefore(float t)
        {
            EnsureBaked();
            if (_samples.Count == 0 || t <= 1e-8f)
                return -1;

            t = Mathf.Clamp01(t);
            if (t >= _samples[_samples.Count - 1].t)
                return _samples.Count - 1;

            int span = GetSpanIndex(t);
            if (_samples[span].t <= t)
                return span;
            return Mathf.Max(-1, span - 1);
        }

        public void SampleSurface(Vector3 radialWorld, float localY, out Vector3 point, out Vector3 normal)
        {
            FilmWrapSurface.SampleCuboid(
                Cargo,
                CuboidSize,
                CuboidCenterLocal,
                SurfaceOffset,
                CornerRadius,
                radialWorld,
                localY,
                out point,
                out normal);
        }

        public Vector3 GetWrapCenterWorld()
        {
            return FilmWrapSurface.GetCenterWorld(Cargo, CuboidCenterLocal);
        }

        public void AutoFitFromCargoBounds()
        {
            if (Cargo == null)
                return;

            var renderers = Cargo.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                var cols = Cargo.GetComponentsInChildren<Collider>();
                if (cols == null || cols.Length == 0)
                    return;

                Bounds wb = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++)
                    wb.Encapsulate(cols[i].bounds);
                ApplyWorldBoundsAsCuboid(wb);
                return;
            }

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                worldBounds.Encapsulate(renderers[i].bounds);
            ApplyWorldBoundsAsCuboid(worldBounds);
        }

        private Vector3 EvaluateNozzleLocal(float t)
        {
            float angle = Mathf.Clamp01(t) * Revolutions * Mathf.PI * 2f;
            float y = EvaluateHeight(t);
            return new Vector3(Mathf.Cos(angle) * OrbitRadius, y, Mathf.Sin(angle) * OrbitRadius);
        }

        private Vector3 EvaluateRadialLocal(float t)
        {
            Vector3 fromCenter = EvaluateNozzleLocal(t) - CuboidCenterLocal;
            fromCenter.y = 0f;
            if (fromCenter.sqrMagnitude < 1e-8f)
                return Vector3.right;
            return fromCenter.normalized;
        }

        private float EvaluateHeight(float t)
        {
            t = Mathf.Clamp01(t);
            if (!UpThenSlightDown)
                return Mathf.Lerp(BottomY, TopY, t);

            if (t <= 0.8f)
                return Mathf.Lerp(BottomY, TopY, t / 0.8f);

            float tDown = (t - 0.8f) / 0.2f;
            return Mathf.Lerp(TopY, Mathf.Lerp(TopY, BottomY, 0.25f), tDown);
        }

        private void AppendCornerSamples(
            Vector3 fromRadial,
            Vector3 toRadial,
            int fromFace,
            int toFace,
            float t,
            ref float pathLength,
            ref Vector3 lastMid,
            ref bool hasLastMid)
        {
            Vector3 cornerRadial = FilmWrapSurface.GetCornerRadial(Cargo, fromFace, toFace);
            if (cornerRadial.sqrMagnitude < 1e-8f)
            {
                int midFace = FilmWrapSurface.PickIntermediateFace(fromFace, toFace, fromRadial, toRadial);
                Vector3 c0 = FilmWrapSurface.GetCornerRadial(Cargo, fromFace, midFace);
                Vector3 c1 = FilmWrapSurface.GetCornerRadial(Cargo, midFace, toFace);
                if (c0.sqrMagnitude > 1e-8f)
                    AppendSample(t, ToLocalDirection(c0), ref pathLength, ref lastMid, ref hasLastMid);
                if (c1.sqrMagnitude > 1e-8f)
                    AppendSample(t, ToLocalDirection(c1), ref pathLength, ref lastMid, ref hasLastMid);
                return;
            }

            AppendSample(t, ToLocalDirection(cornerRadial), ref pathLength, ref lastMid, ref hasLastMid);
        }

        private void AppendSample(
            float t,
            Vector3 radialLocal,
            ref float pathLength,
            ref Vector3 lastMid,
            ref bool hasLastMid)
        {
            Vector3 nozzleLocal = EvaluateNozzleLocal(t);
            float localY = nozzleLocal.y - CuboidCenterLocal.y;
            Vector3 radialWorld = ToWorldDirection(radialLocal);
            SampleSurface(radialWorld, localY, out Vector3 mid, out _);
            if (hasLastMid)
                pathLength += Vector3.Distance(mid, lastMid);

            _samples.Add(new StoredSample
            {
                t = t,
                pathLength = pathLength,
                nozzleLocal = nozzleLocal,
                radialLocal = radialLocal,
                localY = localY
            });

            lastMid = mid;
            hasLastMid = true;
        }

        private FilmWrapSample ToWorld(in StoredSample s)
        {
            return new FilmWrapSample
            {
                t = s.t,
                pathLength = s.pathLength,
                nozzleWorld = Cargo != null ? Cargo.TransformPoint(s.nozzleLocal) : s.nozzleLocal,
                radialWorld = ToWorldDirection(s.radialLocal),
                localY = s.localY
            };
        }

        private Vector3 ToWorldDirection(Vector3 local)
        {
            if (Cargo == null)
                return local.sqrMagnitude < 1e-8f ? Vector3.right : local.normalized;
            Vector3 world = Cargo.TransformDirection(local);
            if (world.sqrMagnitude < 1e-8f)
                return Vector3.right;
            return world.normalized;
        }

        private Vector3 ToLocalDirection(Vector3 world)
        {
            Vector3 local = Cargo != null ? Cargo.InverseTransformDirection(world) : world;
            local.y = 0f;
            if (local.sqrMagnitude < 1e-8f)
                return Vector3.right;
            return local.normalized;
        }

        private int GetSpanIndex(float t)
        {
            int n = _samples.Count;
            if (n < 2)
                return 0;
            if (t <= _samples[0].t)
                return 0;
            if (t >= _samples[n - 1].t)
                return n - 2;

            int lo = 0;
            int hi = n - 2;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_samples[mid + 1].t < t)
                    lo = mid + 1;
                else if (_samples[mid].t > t)
                    hi = mid - 1;
                else
                    return mid;
            }

            return Mathf.Clamp(lo, 0, n - 2);
        }

        private void ApplyWorldBoundsAsCuboid(Bounds worldBounds)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;
            Vector3[] corners =
            {
                c + new Vector3(e.x, e.y, e.z),
                c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, e.z),
                c + new Vector3(-e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),
                c + new Vector3(-e.x, -e.y, -e.z)
            };

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 lp = Cargo.InverseTransformPoint(corners[i]);
                min = Vector3.Min(min, lp);
                max = Vector3.Max(max, lp);
            }

            CuboidSize = max - min;
            CuboidCenterLocal = (min + max) * 0.5f;
        }
    }
}
