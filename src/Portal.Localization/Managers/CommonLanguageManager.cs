using Irihi.Lingua;

namespace Portal.Localization;

[LinguaManager("./Localization/zh-CN/Common.json")]
public partial class CommonLanguageManager
{
    static CommonLanguageManager()
    {
        LocalizationService.Register(Instance);
    }
}
