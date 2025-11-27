using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Render
{
    public class RenderConfig : IDisposable
    {
        private GameObject _prefab;
        private Array4<Matrix4x4> _loadTrans;
        private Array4<bool> _loadStates;
        private Matrix4x4 _ltw; //父节点的localToWorldMatrix

        public bool Ready => _parts != null;

        private List<RenderObject> _parts;

        private bool _disposed = false;

        private readonly object _lock = new object();

        public RenderConfig(Int4 cellCount, GameObject prefab)
        {
            _loadTrans = new Array4<Matrix4x4>(cellCount);
            _loadStates = new Array4<bool>(cellCount);
            _prefab = prefab;
            InitObject(prefab);
        }

        public void RenderObjects(bool render = true)
        {
            foreach (var t in _parts)
            {
                t.Render(render);
            }
        }

        public void SetNewState(int column, int layer, int row, int depth, Matrix4x4 trans, bool show, bool autoUpdate)
        {
            lock (_lock)
            {
                _loadTrans[column, layer, row, depth] = trans;
                _loadStates[column, layer, row, depth] = show;
            }

            if (autoUpdate)
            {
                UpdateParts().Forget();
            }
        }

        public void SetLtw(Matrix4x4 ltw, bool autoUpdate)
        {
            _ltw = ltw;
            if (autoUpdate)
            {
                UpdateParts().Forget();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var t in _parts)
            {
                t.Dispose();
            }

            _disposed = true;
        }

        //在批量修改完后要调用此方法进行数据写入
        public async UniTaskVoid UpdateParts()
        {
            await UniTask.RunOnThreadPool(UpdatePartsStep1);

            UpdatePartsStep2();
        }

        private void InitObject(GameObject prefab)
        {
            _parts = new List<RenderObject>();

            var meshes = prefab.GetComponentsInChildren<MeshFilter>();

            foreach (var item in meshes)
            {
                if (item.gameObject.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    if (item.sharedMesh.subMeshCount < renderer.sharedMaterials.Length)
                    {
                        return;
                    }

                    foreach (var t in renderer.sharedMaterials)
                    {
                        _parts.Add(new RenderObject(item.sharedMesh, t,
                            prefab.transform.worldToLocalMatrix * item.transform.localToWorldMatrix));
                    }
                }
            }
        }

        private void UpdatePartsStep1()
        {
            Matrix4x4[] ts;
            bool[] ss;
            lock (_lock)
            {
                ts = new Matrix4x4[_loadTrans.Length];
                ss = new bool[_loadStates.Length]; 
                Array.Copy(_loadTrans.m_Array, ts, _loadTrans.Length);
                Array.Copy(_loadStates.m_Array, ss, _loadStates.Length);
            }

            Matrix4x4[] trans = new Matrix4x4[ts.Length];


            for (int i = 0; i < ts.Length; i++)
            {
                trans[i] = _ltw * ts[i];
            }

            foreach (var item in _parts)
            {
                item.UpdateItemsStep1(trans, ss);
            }
        }

        private void UpdatePartsStep2()
        {
            foreach (var item in _parts)
            {
                item.UpdateItemsStep2();
            }
        }
    }
}
