using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "URLBaseConfig", menuName = "ScriptableObjects/URLBaseConfig")]
public class URLBaseConfig : ConfigObject
{
    public URLBaseData data;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as URLBaseData;
    }
}

[System.Serializable]
public class URLBaseData : ConfigData
{
    public string UrlBase;
}
