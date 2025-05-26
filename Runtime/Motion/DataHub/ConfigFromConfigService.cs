using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class ConfigFromConfigService : NonsensicalMono
    {
        private List<PartConfig> _configs;

        private void Start()
        {
            ServiceCore.SafeGet<ConfigService>(OnGetConfig);
        }

        private void OnGetConfig(ConfigService manager)
        {
            ConfigService configManager = manager;
            if (configManager.TryGetConfigs<PartConfig>(out _configs))
            {
                ServiceCore.SafeGet<DataHub>(OnGetDataHub);
            }
            else
            {
                LogCore.Debug("未配置数据点位数据");
            }
        }

        private void OnGetDataHub(DataHub dataHub)
        {
            dataHub.InitPartConfig(_configs);
        }
    }
}
