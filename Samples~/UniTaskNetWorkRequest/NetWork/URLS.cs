public static class URLS
{
    public static string UrlBase;
    
    #region 查询所有AGV信息
    /// <summary>
    /// 查询货位状态
    /// </summary>
    public static string QueryAllAGVInformation => UrlBase + "/AGV/QueryAGV";

    #endregion
}
