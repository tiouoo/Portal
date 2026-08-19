using Irihi.Lingua;
using Portal.Localization;

namespace Portal.Core.Localization;

[LinguaManager("./Localization/zh-CN/Logs.json")]
public partial class CoreLogLanguageManager
{
    static CoreLogLanguageManager()
    {
        LocalizationService.Register(Instance);
    }
}
