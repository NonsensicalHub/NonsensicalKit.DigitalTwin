using System;
using Newtonsoft.Json;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public enum PLCDataType
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
    public class PLCPoint
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
        public PLCDataType type;

        /// <summary>
        /// 值
        /// </summary>
        [JsonProperty("Value")]
        public string value;

        #region Constructor

        public PLCPoint()
        {
        }

        public PLCPoint(string name, string id, string point, long ticks, bool value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Bool;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, uint value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Int;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, ulong value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Int;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, int value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Int;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, long value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Int;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, float value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Float;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, double value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.Float;
            this.value = value.ToString();
        }

        public PLCPoint(string name, string id, string point, long ticks, string value)
        {
            this.name = name;
            this.pointID = id;
            this.point = point;
            this.ticks = ticks;
            this.type = PLCDataType.String;
            this.value = value.ToString();
        }

        #endregion
    }

    public class PLCPart
    {
        public string name; //部件名称
        public string partID; //部件id
        public PLCPoint[] points;
    }
}
