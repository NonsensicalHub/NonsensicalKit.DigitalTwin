using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>按路径进度生成贴在货物表面的膜带，以及喷嘴到膜头的出膜条。</summary>
    public class FilmWrapRibbon
    {
        public FilmWrapPath Path;
        public Transform Host;
        public Transform NozzleTip;
        public float FilmWidth = 0.28f;
        public bool ShowFeedStrip = true;
        public Vector3 FeedStartLocalOffset = new Vector3(-0.12f, 0f, -0.05f);
        public Material Material;

        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Vector3> _normals = new List<Vector3>(1024);
        private readonly List<Vector2> _uvs = new List<Vector2>(1024);
        private readonly List<int> _triangles = new List<int>(2048);
        private readonly List<Vector3> _feedVertices = new List<Vector3>(8);
        private readonly List<Vector3> _feedNormals = new List<Vector3>(8);
        private readonly List<Vector2> _feedUvs = new List<Vector2>(8);
        private readonly List<int> _feedTriangles = new List<int>(12);

        private Mesh _mesh;
        private Mesh _feedMesh;
        private MeshRenderer _renderer;
        private MeshRenderer _feedRenderer;

        public void EnsureMeshes()
        {
            if (Host == null)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FilmRibbon", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
                var filter = Host.GetComponent<MeshFilter>();
                if (filter == null)
                    filter = Host.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;

                _renderer = Host.GetComponent<MeshRenderer>();
                if (_renderer == null)
                    _renderer = Host.gameObject.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }

            ApplyMaterial(_renderer);
            EnsureFeed();
        }

        public void SetVisible(bool visible)
        {
            if (_renderer != null)
                _renderer.enabled = visible;
            if (_feedRenderer != null)
                _feedRenderer.enabled = visible && ShowFeedStrip;
        }

        public void Dispose()
        {
            if (_mesh != null)
                Object.Destroy(_mesh);
            if (_feedMesh != null)
                Object.Destroy(_feedMesh);
            _mesh = null;
            _feedMesh = null;
        }

        public void BuildUntil(float progress)
        {
            EnsureMeshes();
            progress = Mathf.Clamp01(progress);
            if (Path == null || Host == null || progress <= 1e-6f)
            {
                Clear();
                return;
            }

            Path.EnsureBaked();
            if (!Path.IsBaked)
            {
                Clear();
                return;
            }

            int last = Path.GetLastIndexAtOrBefore(progress);
            if (last < 0)
            {
                Clear();
                return;
            }

            FilmWrapSample tip = Path.Evaluate(progress);
            bool atFinal = last >= Path.SampleCount - 1;
            bool onSample = Mathf.Abs(Path.GetSample(last).t - progress) <= 1e-5f;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();

            for (int i = 0; i <= last && i < Path.SampleCount; i++)
            {
                FilmWrapSample sample = Path.GetSample(i);
                SampleRing(sample, out Vector3 v0, out Vector3 v1, out Vector3 n);
                AddRing(v0, v1, n, sample.pathLength);
            }

            if (!atFinal && !onSample)
            {
                SampleRing(tip, out Vector3 v0, out Vector3 v1, out Vector3 n);
                AddRing(v0, v1, n, tip.pathLength);
            }

            Upload(_mesh, _vertices, _normals, _uvs, _triangles);
            if (_renderer != null)
                _renderer.enabled = _vertices.Count >= 2;
            UpdateFeedStrip(tip, show: true);
        }

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
            if (_mesh != null)
                _mesh.Clear();
            if (_feedMesh != null)
                _feedMesh.Clear();
            SetVisible(false);
        }

        private void EnsureFeed()
        {
            Transform feedTf = Host.Find("FilmFeed");
            GameObject feedGo = feedTf != null
                ? feedTf.gameObject
                : new GameObject("FilmFeed");
            if (feedTf == null)
                feedGo.transform.SetParent(Host, false);

            var filter = feedGo.GetComponent<MeshFilter>();
            if (filter == null)
                filter = feedGo.AddComponent<MeshFilter>();
            _feedRenderer = feedGo.GetComponent<MeshRenderer>();
            if (_feedRenderer == null)
                _feedRenderer = feedGo.AddComponent<MeshRenderer>();

            if (_feedMesh == null)
            {
                _feedMesh = new Mesh { name = "FilmFeed", hideFlags = HideFlags.HideAndDontSave };
                _feedMesh.MarkDynamic();
            }

            filter.sharedMesh = _feedMesh;
            _feedRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _feedRenderer.receiveShadows = false;
            ApplyMaterial(_feedRenderer);
        }

        private void ApplyMaterial(MeshRenderer renderer)
        {
            if (renderer == null || Material == null)
                return;
            renderer.sharedMaterial = Material;
        }

        private void SampleRing(FilmWrapSample sample, out Vector3 v0, out Vector3 v1, out Vector3 n)
        {
            float halfW = FilmWidth * 0.5f;
            Path.SampleSurface(sample.radialWorld, sample.localY + halfW, out v0, out Vector3 n0);
            Path.SampleSurface(sample.radialWorld, sample.localY - halfW, out v1, out Vector3 n1);
            n = n0 + n1;
            if (n.sqrMagnitude < 1e-8f)
                n = sample.radialWorld;
            else
                n.Normalize();
        }

        private void AddRing(Vector3 v0, Vector3 v1, Vector3 n, float pathLength)
        {
            int baseIndex = _vertices.Count;
            _vertices.Add(Host.InverseTransformPoint(v0));
            _vertices.Add(Host.InverseTransformPoint(v1));
            Vector3 localN = Host.InverseTransformDirection(n);
            _normals.Add(localN);
            _normals.Add(localN);
            _uvs.Add(new Vector2(pathLength, 1f));
            _uvs.Add(new Vector2(pathLength, 0f));

            if (baseIndex < 2)
                return;

            int i0 = baseIndex - 2;
            int i1 = baseIndex - 1;
            int i2 = baseIndex;
            int i3 = baseIndex + 1;
            _triangles.Add(i0);
            _triangles.Add(i2);
            _triangles.Add(i1);
            _triangles.Add(i1);
            _triangles.Add(i2);
            _triangles.Add(i3);
        }

        private void UpdateFeedStrip(FilmWrapSample tip, bool show)
        {
            show = show && ShowFeedStrip && NozzleTip != null && Path != null;
            if (_feedRenderer != null)
                _feedRenderer.enabled = show;
            if (!show || _feedMesh == null)
                return;

            float halfW = FilmWidth * 0.5f;
            Path.SampleSurface(tip.radialWorld, tip.localY + halfW, out Vector3 top, out Vector3 n0);
            Path.SampleSurface(tip.radialWorld, tip.localY - halfW, out Vector3 bot, out Vector3 n1);
            Vector3 contactMid = (top + bot) * 0.5f;
            Vector3 contactN = n0 + n1;
            if (contactN.sqrMagnitude < 1e-8f)
                contactN = tip.radialWorld;
            contactN.Normalize();

            Vector3 start = NozzleTip.TransformPoint(FeedStartLocalOffset);
            Vector3 up = Vector3.up * halfW;
            Vector3 radial = tip.radialWorld;
            Vector3 mid = (start + contactMid) * 0.5f + radial * 0.12f;
            Vector3 feedSide = Vector3.Cross(contactMid - start, Vector3.up);
            if (feedSide.sqrMagnitude < 1e-8f)
                feedSide = contactN;
            else
                feedSide.Normalize();
            if (Vector3.Dot(feedSide, radial) < 0f)
                feedSide = -feedSide;

            _feedVertices.Clear();
            _feedNormals.Clear();
            _feedUvs.Clear();
            _feedTriangles.Clear();

            AddFeedRing(start, feedSide, up, 0f);
            AddFeedRing(mid, Vector3.Normalize(feedSide + contactN), up, 0.5f);
            AddFeedRing(contactMid, contactN, up, 1f);

            _feedTriangles.Add(0);
            _feedTriangles.Add(2);
            _feedTriangles.Add(1);
            _feedTriangles.Add(1);
            _feedTriangles.Add(2);
            _feedTriangles.Add(3);
            _feedTriangles.Add(2);
            _feedTriangles.Add(4);
            _feedTriangles.Add(3);
            _feedTriangles.Add(3);
            _feedTriangles.Add(4);
            _feedTriangles.Add(5);

            Upload(_feedMesh, _feedVertices, _feedNormals, _feedUvs, _feedTriangles);
        }

        private void AddFeedRing(Vector3 mid, Vector3 normal, Vector3 up, float u)
        {
            _feedVertices.Add(Host.InverseTransformPoint(mid + up));
            _feedVertices.Add(Host.InverseTransformPoint(mid - up));
            Vector3 localN = Host.InverseTransformDirection(normal);
            _feedNormals.Add(localN);
            _feedNormals.Add(localN);
            _feedUvs.Add(new Vector2(u, 1f));
            _feedUvs.Add(new Vector2(u, 0f));
        }

        private static void Upload(
            Mesh mesh, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            if (mesh == null)
                return;
            if (vertices.Count < 2)
            {
                mesh.Clear();
                return;
            }

            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
