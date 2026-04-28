using System.Collections;
using NUnit.Framework;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine.TestTools;

namespace NonsensicalKit.DigitalTwin.Editor.Tests
{
    class EditorExampleTest
    {
        [Test]
        public void InferDimensions_WithSparseInput_ShouldReturnMaxIndexPlusOne()
        {
            var bins = new[]
            {
                new BinData { Level = 2, Column = 0, Row = 1, Depth = 0 },
                new BinData { Level = 1, Column = 3, Row = 0, Depth = 4 }
            };

            var inferred = BinDataIO.InferDimensions(bins);

            Assert.AreEqual(3, inferred.X);
            Assert.AreEqual(4, inferred.Y);
            Assert.AreEqual(2, inferred.Z);
            Assert.AreEqual(5, inferred.W);
        }

        // A UnityTest behaves like a coroutine in PlayMode
        // and allows you to yield null to skip a frame in EditMode
        [UnityTest]
        public IEnumerator InferDimensions_WithEmptyInput_ShouldReturnZero()
        {
            var inferred = BinDataIO.InferDimensions(new BinData[0]);
            Assert.AreEqual(0, inferred.X);
            Assert.AreEqual(0, inferred.Y);
            Assert.AreEqual(0, inferred.Z);
            Assert.AreEqual(0, inferred.W);
            yield return null;
        }
    }
}
