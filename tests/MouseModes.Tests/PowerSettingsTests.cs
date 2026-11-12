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

        [Fact]
        public void ActivePlanGuid_ReadsWithoutPowercfg()
        {
            // Só lê o plano ativo deste PC pelo powrprof; não muda nada.
            string guid = null;
            Assert.Null(Record.Exception(() => guid = PowerSettings.ActivePlanGuid()));
            Assert.NotNull(guid);
            Assert.Equal(guid, ModeManager.ValidGuid(guid));
        }

        [Fact]
        public void PlanCopiesToDelete_NeverIncludesTheActivePlan()
        {
            var copies = new List<PlanCopy>
            {
                new PlanCopy { Template = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Guid = "D62DC7A9-E82F-40EB-8BA9-4B6CF1AB4ED1" },
                new PlanCopy { Template = "e9a42b02-d5df-448d-aa00-03f14749eb61", Guid = "232e2748-0959-4183-a9e8-1b5372b4f906" },
                new PlanCopy { Template = "381b4222-f694-41f0-9685-ff5bb260df2e", Guid = "nao-e-guid & calc" },
                null
            };

            List<PlanCopy> delete = PowerSettings.PlanCopiesToDelete(copies, "232E2748-0959-4183-A9E8-1B5372B4F906");

            Assert.Same(copies[0], Assert.Single(delete)); // a ativa fica; GUID inválido não chega ao powercfg
            Assert.Empty(PowerSettings.PlanCopiesToDelete(copies, null)); // sem saber o ativo, não apaga nada
        }
    }
}
