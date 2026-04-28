using System.Collections;
using NUnit.Framework;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine.TestTools;

namespace NonsensicalKit.DigitalTwin.Tests
{
    class RuntimeExampleTest
    {
        [Test]
        public void WarehouseDataCreate_ShouldInferDimensions()
        {
            var bins = new[]
            {
                new BinData { Level = 0, Column = 1, Row = 2, Depth = 0 },
                new BinData { Level = 3, Column = 4, Row = 5, Depth = 1 }
            };

            var data = WarehouseData.Create(bins);

            Assert.AreEqual(4, data.Dimensions.X);
            Assert.AreEqual(5, data.Dimensions.Y);
            Assert.AreEqual(6, data.Dimensions.Z);
            Assert.AreEqual(2, data.Dimensions.W);
        }

        // A UnityTest behaves like a coroutine in PlayMode
        // and allows you to yield null to skip a frame in EditMode
        [UnityTest]
        public IEnumerator WarehouseDataCreate_WithNullBins_ShouldYieldEmptyDimensions()
        {
            var data = WarehouseData.Create(null);
            Assert.AreEqual(0, data.Dimensions.X);
            Assert.AreEqual(0, data.Dimensions.Y);
            Assert.AreEqual(0, data.Dimensions.Z);
            Assert.AreEqual(0, data.Dimensions.W);
            yield return null;
        }
    }
}
