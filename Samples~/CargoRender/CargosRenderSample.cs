using System;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Render;
using UnityEngine;
using Random = UnityEngine.Random;

namespace NonsensicalKit.DigitalTwin.Samples
{
    public class CargosRenderSample : MonoBehaviour
    {
        [SerializeField] private string m_shelvesID = "testShelves";
        [SerializeField] private string m_cargos1ID = "Cargos1";
        [SerializeField] private string m_cargos2ID = "Cargos2";

        [SerializeField] private int m_x = 100, m_y = 10, m_z = 10, m_w = 3;

        private void Start()
        {
            Array4<CargoMapping> map = new Array4<CargoMapping>(m_x, m_y, m_z, m_w);

            for (int x = 0; x < m_x; x++)
            {
                for (int y = 0; y < m_y; y++)
                {
                    for (int z = 0; z < m_z; z++)
                    {
                        for (int w = 0; w < m_w; w++)
                        {
                            int type = Random.Range(0, 3);
                            CargoMapping crt = new CargoMapping()
                            {
                                Pos = new ShelvesMapping()
                                {
                                    MappingPosition = new Int4(x, y, z, w),
                                    PhysicsPosition = new Float3(x * 2, y * 5 + w * 0.7f, z * 2)
                                },
                                CargoType = type switch
                                {
                                    0 => null,
                                    1 => m_cargos1ID,
                                    _ => m_cargos2ID
                                },
                                ExistCargo = type != 0,
                                ShowCargo = type != 0,
                            };
                            map[x, y, z, w] = crt;
                        }
                    }
                }
            }

            IOCC.PublishWithID("InitShelvesCargo", m_shelvesID, map);
        }

        [Button]
        private void RandomUpdateShelves()
        {
            IOCC.PublishWithID<Action<Array4<CargoMapping>>>("WriteShelvesCargo", m_shelvesID, DoRandomUpdateShelves);
        }

        private void DoRandomUpdateShelves(Array4<CargoMapping> map)
        {
            for (int x = 0; x < m_x; x++)
            {
                for (int y = 0; y < m_y; y++)
                {
                    for (int z = 0; z < m_z; z++)
                    {
                        for (int w = 0; w < m_w; w++)
                        {
                            if (map[x, y, z, w].ExistCargo)
                            {
                                map[x, y, z, w].ShowCargo = Random.Range(0, 2) == 0;
                            }
                        }
                    }
                }
            }
        }
    }
}
