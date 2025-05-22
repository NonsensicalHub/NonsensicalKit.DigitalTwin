using System;
using Newtonsoft.Json;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public enum PointDataType
    {
        //0,1,false,true,False,True
        Bool = 0,

        //short,int,long
        Int = 1,

        //float,double
        Float = 2,

        //string
        String = 3,
    }

    [Serializable]
    public class PointData
    {
        /// <summary>
        /// 点位名称，用于表达点位的意义
        /// </summary>
        [JsonProperty("Name")]
        public string name;

        /// <summary>
        /// 点位id
        /// </summary>
        [JsonProperty("ID")]
        public string pointID;

        /// <summary>
        /// plc点位名称
        /// </summary>
        [JsonProperty("Point")]
        public string point;

        /// <summary>
        /// 采集数据的时间，单位为毫秒，C#的ticks需要除以10000
        /// </summary>
        [JsonProperty("Ticks")]
        public long ticks;

        /// <summary>
        /// 数据类型枚举
        /// </summary>
        [JsonProperty("Type")]
        public PointDataType type;

        /// <summary>
        /// 值
        /// </summary>
        [JsonProperty("Value")]
        public string value;

        #region Constructor

        public PointData()
        {
        }

        public PointData(string name, string id, string point, long ticks, bool value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Bool;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, uint value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Int;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, ulong value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Int;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, int value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Int;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, long value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Int;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, float value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Float;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, double value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.Float;
            this.value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, string value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PointDataType.String;
            this.value = value.ToString();
        }

        #endregion
    }

    public class PartData
    {
        public string name; //部件名称
        public string partID; //部件id
        public PointData[] points;
    }
}
