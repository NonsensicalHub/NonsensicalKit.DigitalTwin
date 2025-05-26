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
        public string Name;

        /// <summary>
        /// 点位id
        /// </summary>
        [JsonProperty("ID")]
        public string PointID;

        /// <summary>
        /// plc点位名称，用于debug和展示
        /// </summary>
        [JsonProperty("Name")]
        public string Point;

        /// <summary>
        /// 采集数据的时间，单位为毫秒，C#的ticks需要除以10000
        /// </summary>
        [JsonProperty("Ticks")]
        public long Ticks;

        /// <summary>
        /// 数据类型枚举
        /// </summary>
        [JsonProperty("Type")]
        public PointDataType Type;

        /// <summary>
        /// 值
        /// </summary>
        [JsonProperty("Value")]
        public string Value;

        #region Constructor

        public PointData()
        {
        }

        public PointData(string name, string id, string point, long ticks, bool value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Bool;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, uint value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Int;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, ulong value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Int;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, int value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Int;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, long value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Int;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, float value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Float;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, double value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.Float;
            this.Value = value.ToString();
        }

        public PointData(string name, string id, string point, long ticks, string value)
        {
            this.Name = name;
            this.PointID = id;
            this.Point = point;
            this.Ticks = ticks;
            this.Type = PointDataType.String;
            this.Value = value.ToString();
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
