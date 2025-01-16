using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

public class InitScene : NonsensicalMono
{
    [SerializeField] private int m_frameRateLimitation = 60;


    private void Awake()
    {
        Application.targetFrameRate = m_frameRateLimitation;
        ServiceCore.SafeGet<ConfigService>(OnLoadCompleted);
    }

    private void OnLoadCompleted(ConfigService config)
    {
        if (config.TryGetConfig<URLBaseData>(out var v))
        {
            URLS.UrlBase = v.UrlBase;
        }
    }
}
