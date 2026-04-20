using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

public class BeltRenderer : MonoBehaviour
{
    public Mesh Mesh;
    public Material MaterialItem1;

    private RenderObject _renderObject;

    private void Awake()
    {
        Matrix4x4[] itemState = new Matrix4x4[1000 * 1000];
        bool[] itemShow = new bool[1000 * 1000];
        for (int x = 0; x < 1000; x++)
        {
            for (int z = 0; z < 1000; z++)
            {
                Vector3 positionItem = new Vector3(x, 0.3f, z);
                Quaternion rotationItem = Quaternion.Euler(0, 0, 0);
                Vector3 scaleItem = new Vector3(0.5f, 0.5f, 0.5f);

                itemState[x * 1000 + z] = Matrix4x4.TRS(positionItem, rotationItem, scaleItem);
                itemShow[x * 1000 + z] = true;
            }
        }

        _renderObject = new RenderObject(Mesh, MaterialItem1, Matrix4x4.identity);
        _renderObject.UpdateItems(itemState, itemShow).Forget();
        // _renderObject.UpdateItemsStep1(itemState, itemShow);
        // _renderObject.UpdateItemsStep2();
    }

    private void OnDisable()
    {
        _renderObject.Release();
    }

    private void Update()
    {
        _renderObject.Render();
    }
}
