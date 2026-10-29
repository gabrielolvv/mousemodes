using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Um processador lógico como o Windows descreve em GetSystemCpuSetInformation.</summary>
    public struct CpuSetInfo
    {
        public uint Id;
        /// <summary>Maior = mais forte. Nos Intel híbridos, os núcleos P têm classe maior que os E.</summary>
        public byte EfficiencyClass;

        public CpuSetInfo(uint id, byte efficiencyClass)
        {
            Id = id;
            EfficiencyClass = efficiencyClass;
        }
    }

    /// <summary>
    /// Núcleos certos: nos processadores híbridos (Intel 12ª geração ou mais nova), o segundo plano calmo põe os outros
    /// programas nos núcleos de eficiência e deixa os de desempenho para o jogo. Usa os CPU sets padrão do processo,
    /// o jeito brando do Windows (não é afinidade: o programa pode pedir outros núcleos para as próprias threads).
    /// Não passa para os processos filhos, então um jogo aberto por um launcher acalmado não nasce preso nos núcleos E.
    /// </summary>
    public static class CpuSets
    {
        // SYSTEM_CPU_SET_INFORMATION: Size (4), Type (4), Id (4), Group (2), LogicalProcessorIndex, CoreIndex,
        // LastLevelCacheIndex, NumaNodeIndex, EfficiencyClass (1 byte cada)...
        private const int IdOffset = 8;
        private const int EfficiencyClassOffset = 18;
        private const int CpuSetInformation = 0;
        private const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemCpuSetInformation(IntPtr information, int bufferLength, out int returnedLength,
                                                              IntPtr process, int flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessDefaultCpuSets(IntPtr process, uint[] cpuSetIds, int cpuSetIdCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessDefaultCpuSets(IntPtr process, uint[] cpuSetIds, int cpuSetIdCount, out int requiredIdCount);

        // Os núcleos não mudam com o PC ligado: lê uma vez só.
        private static readonly Lazy<List<CpuSetInfo>> system = new Lazy<List<CpuSetInfo>>(Read);
        private static readonly Lazy<uint[]> background = new Lazy<uint[]>(() => BackgroundIds(system.Value));

        /// <summary>Os núcleos para o segundo plano neste PC; null se o processador não for híbrido.</summary>
        public static uint[] Background { get { return background.Value; } }

        /// <summary>Total de processadores lógicos (para o registro: "4 de 16").</summary>
        public static int Total { get { return system.Value.Count; } }

        /// <summary>
        /// Todos os núcleos abaixo da classe mais forte (no Meteor Lake, os E e os LP-E). null se o processador não for
        /// híbrido ou se sobrarem menos de 2 para o segundo plano: aí apertar tudo num núcleo só pioraria.
        /// </summary>
        internal static uint[] BackgroundIds(IList<CpuSetInfo> sets)
        {
            if (sets == null || sets.Count == 0) return null;
            byte strongest = 0;
            foreach (CpuSetInfo set in sets)
                if (set.EfficiencyClass > strongest) strongest = set.EfficiencyClass;

            var ids = new List<uint>();
            foreach (CpuSetInfo set in sets)
                if (set.EfficiencyClass < strongest) ids.Add(set.Id);
            return ids.Count >= 2 ? ids.ToArray() : null;
        }

        /// <summary>Lê os processadores do PC. Lista vazia no Windows 7 (sem CPU sets) ou se a leitura falhar.</summary>
        internal static List<CpuSetInfo> Read()
        {
            var sets = new List<CpuSetInfo>();
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int length;
                GetSystemCpuSetInformation(IntPtr.Zero, 0, out length, IntPtr.Zero, 0);
                if (length <= 0) return sets;
                buffer = Marshal.AllocHGlobal(length);
                if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0)) return sets;

                for (int offset = 0; offset + EfficiencyClassOffset < length;)
                {
                    int size = Marshal.ReadInt32(buffer, offset);
                    if (size <= EfficiencyClassOffset) break;
                    if (Marshal.ReadInt32(buffer, offset + 4) == CpuSetInformation)
                        sets.Add(new CpuSetInfo((uint)Marshal.ReadInt32(buffer, offset + IdOffset),
                                                Marshal.ReadByte(buffer, offset + EfficiencyClassOffset)));
                    offset += size;
                }
            }
            catch (Exception)
            {
                // Windows 7 (não tem CPU sets) ou leitura que falhou: trata como processador não híbrido.
                sets.Clear();
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
            return sets;
        }

        /// <summary>
        /// Põe o processo nos núcleos de eficiência, a não ser que ele já tenha os próprios CPU sets.
        /// false se não pôs: processador não híbrido, acesso negado ou programa que já cuida disso.
        /// </summary>
        public static bool MoveToBackground(IntPtr process)
        {
            uint[] ids = Background;
            if (ids == null || CountOf(process) != 0) return false;
            try { return SetProcessDefaultCpuSets(process, ids, ids.Length); }
            catch { return false; }
        }

        /// <summary>Quantos CPU sets padrão o processo tem (0 = o Windows escolhe); -1 se não der para ler.</summary>
        internal static int CountOf(IntPtr process)
        {
            try
            {
                int count;
                if (GetProcessDefaultCpuSets(process, null, 0, out count)) return 0;
                return Marshal.GetLastWin32Error() == ErrorInsufficientBuffer ? count : -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Devolve a escolha dos núcleos ao Windows (sem CPU sets padrão, como todo processo nasce).</summary>
        public static bool Clear(IntPtr process)
        {
            try { return SetProcessDefaultCpuSets(process, null, 0); }
            catch { return false; }
        }
    }
}
