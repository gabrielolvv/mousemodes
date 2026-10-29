using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Núcleos certos: quais processadores lógicos ficam para o segundo plano.</summary>
    public class CpuSetsTests
    {
        [Fact]
        public void BackgroundIds_AreTheEfficiencyCoresOfAHybridCpu()
        {
            // i7-13620H: 6 núcleos P com 2 threads (classe 1) e 4 núcleos E (classe 0).
            var sets = new List<CpuSetInfo>();
            for (uint id = 256; id < 268; id++) sets.Add(new CpuSetInfo(id, 1));
            for (uint id = 268; id < 272; id++) sets.Add(new CpuSetInfo(id, 0));

            Assert.Equal(new uint[] { 268, 269, 270, 271 }, CpuSets.BackgroundIds(sets));
        }

        [Fact]
        public void BackgroundIds_KeepEveryClassBelowTheStrongest()
        {
            // Meteor Lake: P (classe 2), E (classe 1) e os LP-E da ilha de baixo consumo (classe 0).
            var sets = new List<CpuSetInfo>
            {
                new CpuSetInfo(1, 2), new CpuSetInfo(2, 2),
                new CpuSetInfo(3, 1), new CpuSetInfo(4, 1),
                new CpuSetInfo(5, 0), new CpuSetInfo(6, 0)
            };

            Assert.Equal(new uint[] { 3, 4, 5, 6 }, CpuSets.BackgroundIds(sets));
        }

        [Fact]
        public void BackgroundIds_IsNullWhenTheCpuIsNotHybrid()
        {
            // Ryzen ou Intel antigo: todos os núcleos na mesma classe.
            var sets = new List<CpuSetInfo>();
            for (uint id = 0; id < 16; id++) sets.Add(new CpuSetInfo(id, 0));

            Assert.Null(CpuSets.BackgroundIds(sets));
            Assert.Null(CpuSets.BackgroundIds(new List<CpuSetInfo>()));
            Assert.Null(CpuSets.BackgroundIds(null));
        }

        [Fact]
        public void BackgroundIds_IsNullWithASingleEfficiencyCore()
        {
            // Apertar todo o segundo plano num núcleo só pioraria.
            var sets = new List<CpuSetInfo> { new CpuSetInfo(1, 1), new CpuSetInfo(2, 1), new CpuSetInfo(3, 0) };

            Assert.Null(CpuSets.BackgroundIds(sets));
        }

        [Fact]
        public void Read_DescribesThisPc()
        {
            // Só lê: qualquer PC com Windows 10 ou mais novo tem ao menos um processador lógico.
            List<CpuSetInfo> sets = CpuSets.Read();
            Assert.NotEmpty(sets);
            Assert.Equal(sets.Count, CpuSets.Total);
        }
    }
}
