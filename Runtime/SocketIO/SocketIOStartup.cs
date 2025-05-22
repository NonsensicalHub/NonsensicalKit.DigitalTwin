    using NonsensicalKit;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

public class SocketIOStartup : MonoBehaviour
{
    private void Awake()
    {
        ServiceCore.SafeGet<ConfigService>(OnGetConfig);
    }

    private void OnGetConfig(ConfigService service)
    {
        if (service.TryGetConfigs<SocketIOConfigData>(out var configs))
        {
            foreach (var config in configs)
            {
                gameObject.AddComponent<SocketIOManager>().Init(config);
            }
        }
    }
}
