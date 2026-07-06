using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Admin
{
    public class CrossSellAdminVM
    {
        public CrossSellSettings Settings { get; set; } = new CrossSellSettings();
        public List<AprioriRulePreviewVM> PreviewRules { get; set; } = new List<AprioriRulePreviewVM>();
    }

    public class AprioriRulePreviewVM
    {
        public string AntecedentNames { get; set; } = string.Empty;
        public string ConsequentName { get; set; } = string.Empty;
        public int SupportCount { get; set; }
        public decimal SupportPercent { get; set; }
        public decimal Confidence { get; set; }
        public decimal Lift { get; set; }
        public string BundleDiscountPreview { get; set; } = string.Empty;
    }
}
