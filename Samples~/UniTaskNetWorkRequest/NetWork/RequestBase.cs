using BJTimer;
using NonsensicalKit.Core;
using UnityEngine;

public class RequestBase : MonoBehaviour, IRequestTool
{
    [SerializeField] protected bool m_log;
    [SerializeField] protected bool m_enable;
    [SerializeField, Tooltip("请求间隔")] private float m_interval;
    [SerializeField, Tooltip("请求次数")] private int m_requestCount;
    [SerializeField, Tooltip("初始调用")] private bool m_initialcall;
    [SerializeField] private bool m_enableOnAwake;

    protected IDPack TiemrID;

    protected virtual void Awake()
    {
        if (m_enable == false) return;
        if (IOCC.TryGet<TimerSystem>("Timer", out var timer) == false)
        {
            Debug.LogError("未配置定时器！");
            return;
        }

        if (m_enableOnAwake)
        {
            TiemrID = IOCC.Get<TimerSystem>("Timer").AddTimerTask(GetData, m_interval, m_requestCount, TimeUnit.Secound, m_initialcall);
        }
    }

    protected virtual void OnDestroy()
    {
        IOCC.Get<TimerSystem>("Timer").DeleteTimeTask(TiemrID.id);
    }

    public virtual void SetEnable(bool _enable)
    {
        if (_enable)
        {
            TiemrID = IOCC.Get<TimerSystem>("Timer").AddTimerTask(GetData, m_interval, m_requestCount, TimeUnit.Secound, m_initialcall);
        }
        else
        {
            IOCC.Get<TimerSystem>("Timer").DeleteTimeTask(TiemrID.id);
        }
    }

    protected virtual void GetData(int _)
    {
    }

    public virtual void SendRequestTest()
    {
        Debug.Log("(若需要测试请重写此方法) 测试接口： " + this.name);
    }
}
