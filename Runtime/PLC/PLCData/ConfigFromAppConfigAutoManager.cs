using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class ConfigFromAppConfigAutoManager : NonsensicalMono
    {
        private void Start()
        {
            ServiceCore.SafeGet<ConfigService>(OnGetManager);
        }

        private void OnGetManager(ConfigService manager)
        {
            ConfigService configManager = manager as ConfigService;
            if (configManager.TryGetConfigs<PartConfig>(out var configs))
            {
                Publish<IEnumerable<PartConfig>>("partConfigInit", configs);
            }
            else
            {
                LogCore.Debug("未配置plc数据");
            }
        }
    }
}
