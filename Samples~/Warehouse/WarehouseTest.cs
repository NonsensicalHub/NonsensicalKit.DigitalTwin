    using System.IO;
    using NaughtyAttributes;
    using NonsensicalKit.DigitalTwin.Warehouse;
    using UnityEngine;

public class WarehouseTest : MonoBehaviour
{
    [Button]
    private void Create()
    {
        BinDataIO.SaveSync(BinDataIO.CreateTestWarehouse100x100x100().Bins,Path.Combine(Application.streamingAssetsPath,"Warehouse","Test.dat") ); 
    }
}
