using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Só leitura: lista os planos do PC, sem ativar nenhum.</summary>
    public class PowerSettingsTests
    {
        [Fact]
        public void Plans_ListsGuidsWithNamesAndSkipsOverlays()
        {
            using (new TempFolder()) // desvia o log
            {
                Dictionary<string, string> plans = PowerSettings.Plans();

                Assert.NotEmpty(plans);
                foreach (KeyValuePair<string, string> plan in plans)
                {
                    Guid parsed;
                    Assert.True(Guid.TryParse(plan.Key, out parsed), plan.Key);
                    Assert.DoesNotContain("Overlay", plan.Value);
                }
            }
        }
    }
}
