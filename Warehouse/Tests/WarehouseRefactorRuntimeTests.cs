using System;
using System.Collections;
using System.Reflection;
using NonsensicalKit.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace NonsensicalKit.DigitalTwin.Warehouse.Tests
{
    public class WarehouseRefactorRuntimeTests
    {
        [Test]
        public void WarehouseUpdateScheduler_InternalFrameApi_ShouldScheduleAndDispatch()
        {
            Type schedulerType = typeof(WarehouseData).Assembly.GetType(
                "NonsensicalKit.DigitalTwin.Warehouse.WarehouseUpdateScheduler",
                true);
            object scheduler = Activator.CreateInstance(schedulerType, true);

            int executeCount = 0;
            Action execute = () => executeCount++;

            InvokeInstance(
                schedulerType,
                scheduler,
                "RequestAtFrame",
                10,
                2,
                false,
                execute);

            Assert.IsTrue((bool)GetProperty(schedulerType, scheduler, "HasPendingUpdate"));

            InvokeInstance(
                schedulerType,
                scheduler,
                "TryExecuteScheduledAtFrame",
                10,
                2,
                execute);
            Assert.AreEqual(0, executeCount);

            InvokeInstance(
                schedulerType,
                scheduler,
                "TryExecuteScheduledAtFrame",
                11,
                2,
                execute);
            Assert.AreEqual(1, executeCount);

            InvokeInstance(
                schedulerType,
                scheduler,
                "NotifyExecutedAtFrame",
                11,
                2);
            Assert.IsFalse((bool)GetProperty(schedulerType, scheduler, "HasPendingUpdate"));
            Assert.AreEqual(1, (int)InvokeInstance(schedulerType, scheduler, "ConsumeDispatchedCount"));
        }

        [Test]
        public void WarehouseBinDataStore_SetData_ShouldExposeBinAccessors()
        {
            Type storeType = typeof(WarehouseData).Assembly.GetType(
                "NonsensicalKit.DigitalTwin.Warehouse.WarehouseBinDataStore",
                true);
            object store = Activator.CreateInstance(storeType, true);

            var bins = new[]
            {
                new BinData { Level = 0, Column = 1, Row = 0, Depth = 0, PosX = 1f, PosY = 2f, PosZ = 3f }
            };
            var data = new WarehouseData(bins, new Int4(1, 2, 1, 1));

            InvokeInstance(storeType, store, "SetData", data);

            Assert.IsTrue((bool)GetProperty(storeType, store, "IsReady"));
            Assert.IsTrue((bool)InvokeInstance(storeType, store, "IsColumnInRange", 1));
            Assert.IsFalse((bool)InvokeInstance(storeType, store, "IsColumnInRange", 2));
            Assert.IsTrue((bool)InvokeInstance(storeType, store, "IsValidLocation", new Int4(0, 1, 0, 0)));
            Assert.IsFalse((bool)InvokeInstance(storeType, store, "IsValidLocation", new Int4(0, 2, 0, 0)));

            object[] tryGetArgs = { new Int4(0, 1, 0, 0), null };
            bool found = (bool)InvokeInstance(storeType, store, "TryGet", tryGetArgs);
            Assert.IsTrue(found);
            Assert.IsNotNull(tryGetArgs[1]);

            InvokeInstance(storeType, store, "SetData", (object)null);
            Assert.IsFalse((bool)GetProperty(storeType, store, "IsReady"));
        }

        [Test]
        public void WarehouseCargoInitializer_ValidateInputs_ShouldReturnDetailedError()
        {
            Type storeType = typeof(WarehouseData).Assembly.GetType(
                "NonsensicalKit.DigitalTwin.Warehouse.WarehouseBinDataStore",
                true);
            object store = Activator.CreateInstance(storeType, true);

            var bins = new[]
            {
                new BinData { Level = 0, Column = 0, Row = 0, Depth = 0, PosX = 0f, PosY = 0f, PosZ = 0f }
            };
            var data = new WarehouseData(bins, new Int4(1, 1, 1, 1));
            InvokeInstance(storeType, store, "SetData", data);

            Type initializerType = typeof(WarehouseData).Assembly.GetType(
                "NonsensicalKit.DigitalTwin.Warehouse.WarehouseCargoInitializer",
                true);
            object initializer = Activator.CreateInstance(
                initializerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { store, null, WarehouseChunkLevel.Medium, 0f },
                null);

            object[] args = { null };
            bool valid = (bool)InvokeInstance(initializerType, initializer, "ValidateInputs", args);
            Assert.IsFalse(valid);
            StringAssert.Contains("m_cargoPrefabs", args[0]?.ToString());
        }
        
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

        private static object InvokeInstance(Type type, object target, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Method not found: {methodName}");
            return method.Invoke(target, args);
        }

        private static object GetProperty(Type type, object target, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(property, $"Property not found: {propertyName}");
            return property.GetValue(target);
        }
        
    }
}
