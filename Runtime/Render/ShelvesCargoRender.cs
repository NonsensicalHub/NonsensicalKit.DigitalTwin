using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Render
{
    public class ShelvesMapping
    {
        public Int4 MappingPosition; //列-层-排-深（从货架正面看，列从左往右数，排从近往远数）
        public Float3 PhysicsPosition; //物理坐标
        public Float3 PhysicsRotation; //物理旋转
        public int SpaceType; //TODO:空间类型，暂时无用，可用作寻路逻辑，如0代表货位，1代表巷道
    }

    public class CargoMapping
    {
        public ShelvesMapping Pos;
        public string CargoType;
        public bool ExistCargo = false;
        public bool ShowCargo = false;
        public Color? Color; //TODO
        public Vector3? PosOffset; //TODO
        public Vector3? ScaleMult; //TODO
    }

    [Serializable]
    public class ShelvesCargoPrefabConfig
    {
        public GameObject m_Prefab;

        public string m_CargoType;
    }

    public class ShelvesCargoRender : NonsensicalMono
    {
        [SerializeField] private string m_shelvesID;
        [SerializeField] private ShelvesCargoPrefabConfig[] m_cargoPrefabConfig;

        private RenderConfig[] _cargoConfigs;
        private Array4<CargoMapping> _cargos;
        private Matrix4x4 _ltwMatrix;

        private readonly object _writeQueueLock = new object();
        private readonly Queue<Action<Array4<CargoMapping>>> _pendingWrites = new();

        private void Awake()
        {
            Subscribe<Array4<CargoMapping>>("InitShelvesCargo", m_shelvesID, InitShelves);
            Subscribe<Action<Array4<CargoMapping>>>("WriteShelvesCargo", m_shelvesID, QueueWrite);
        }

        private void Update()
        {
            if (_cargoConfigs == null) return;

            List<Action<Array4<CargoMapping>>> writes = null;

            lock (_writeQueueLock)
            {
                if (_pendingWrites.Count > 0)
                {
                    writes = new List<Action<Array4<CargoMapping>>>(_pendingWrites);
                    _pendingWrites.Clear();
                }
            }

            if (writes != null)
            {
                foreach (var write in writes)
                {
                    write(_cargos);
                }

                UpdateCargos();
            }

            var flag = false;
            if (transform.localToWorldMatrix != _ltwMatrix)
            {
                _ltwMatrix = transform.localToWorldMatrix;
                flag = true;
            }

            foreach (var config in _cargoConfigs)
            {
                if (!config.Ready) continue;
                if (flag)
                {
                    config.SetLtw(_ltwMatrix, true);
                }

                config.RenderObjects();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var item in _cargoConfigs)
            {
                item.Dispose();
            }
        }

        private void InitShelves(Array4<CargoMapping> map)
        {
            _cargos = map;
            _cargoConfigs = new RenderConfig[m_cargoPrefabConfig.Length];

            _ltwMatrix = transform.localToWorldMatrix;
            for (int i = 0; i < m_cargoPrefabConfig.Length; i++)
            {
                _cargoConfigs[i] = new RenderConfig(map.Size, m_cargoPrefabConfig[i].m_Prefab);
                _cargoConfigs[i].SetLtw(transform.localToWorldMatrix, false);
            }

            UpdateCargos();
        }

        private void QueueWrite(Action<Array4<CargoMapping>> writeAction)
        {
            lock (_writeQueueLock)
            {
                _pendingWrites.Enqueue(writeAction);
            }
        }

        private void UpdateCargos()
        {
            if (_cargoConfigs == null)
            {
                return;
            }

            for (var x = 0; x < _cargos.Length0; x++)
            {
                for (var y = 0; y < _cargos.Length1; y++)
                {
                    for (var z = 0; z < _cargos.Length2; z++)
                    {
                        for (int w = 0; w < _cargos.Length3; w++)
                        {
                            if (_cargos[x, y, z, w] == null)
                            {
                                continue;
                            }

                            for (int i = 0; i < m_cargoPrefabConfig.Length; i++)
                            {
                                bool show = GetMatrix(_cargos[x, y, z, w], out var m4X4) &&
                                            m_cargoPrefabConfig[i].m_CargoType == _cargos[x, y, z,w].CargoType;
                                _cargoConfigs[i].SetNewState(x, y, z, w, m4X4, show, false);
                            }
                        }
                    }
                }
            }

            foreach (var item in _cargoConfigs)
            {
                item.UpdateParts().Forget();
            }
        }

        private bool GetMatrix(CargoMapping mapping, out Matrix4x4 m4X4)
        {
            if (!mapping.ShowCargo || !mapping.ExistCargo ||
                mapping.Pos.PhysicsPosition is { X: 0, Y: 0, Z: 0 })
            {
                m4X4 = default;
                return false;
            }

            m4X4 = Matrix4x4.TRS(mapping.Pos.PhysicsPosition.ToVector3(),
                Quaternion.Euler(mapping.Pos.PhysicsRotation.ToVector3()),
                Vector3.one); //TODO：实现位置颜色、偏移和缩放
            return true;
        }
    }
}
