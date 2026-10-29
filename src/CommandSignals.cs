using System;
using System.Collections.Generic;
using System.Threading;

namespace MouseModes
{
    /// <summary>
    /// Troca de modo pela linha de comando (MouseModes.exe --performance, --daily ou --toggle), para Stream Deck,
    /// atalho na área de trabalho ou Agendador de Tarefas. Cada comando é um evento com nome: a cópia aberta
    /// espera por ele, e uma segunda cópia só dispara o evento e sai, como no --quit.
    /// </summary>
    public sealed class CommandSignals : IDisposable
    {
        public const string Performance = "--performance";
        public const string Daily = "--daily";
        public const string Toggle = "--toggle";

        private static readonly string[] All = { Performance, Daily, Toggle };

        internal const string DefaultPrefix = "Local\\MouseModes_Command";

        private readonly Dictionary<string, EventWaitHandle> signals = new Dictionary<string, EventWaitHandle>();
        private readonly List<RegisteredWaitHandle> waits = new List<RegisteredWaitHandle>();

        /// <summary>Cria os eventos dos comandos. Só a cópia que ficou com o mutex faz isso.</summary>
        /// <param name="prefix">Começo do nome dos eventos (os testes usam um próprio, para não falar com o app aberto).</param>
        public CommandSignals(string prefix = DefaultPrefix)
        {
            foreach (string command in All)
                signals[command] = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(prefix, command));
        }

        /// <summary>O primeiro comando de modo nos argumentos, ou null se não houver.</summary>
        public static string Find(string[] args)
        {
            foreach (string arg in args)
                foreach (string command in All)
                    if (string.Equals(arg, command, StringComparison.OrdinalIgnoreCase)) return command;
            return null;
        }

        /// <summary>
        /// Manda o comando para a cópia aberta. false se nenhuma cópia espera por ele
        /// (versão antiga, sem esses comandos).
        /// </summary>
        public static bool Send(string command, string prefix = DefaultPrefix)
        {
            EventWaitHandle signal;
            if (!EventWaitHandle.TryOpenExisting(EventName(prefix, command), out signal)) return false;
            using (signal) signal.Set();
            return true;
        }

        /// <summary>Chama <paramref name="received"/> (numa thread do pool) a cada comando que chegar.</summary>
        public void Listen(Action<string> received)
        {
            foreach (KeyValuePair<string, EventWaitHandle> pair in signals)
            {
                string command = pair.Key;
                waits.Add(ThreadPool.RegisterWaitForSingleObject(pair.Value, (state, timedOut) => received(command),
                    null, -1, false));
            }
        }

        public void Dispose()
        {
            foreach (RegisteredWaitHandle wait in waits) wait.Unregister(null);
            waits.Clear();
            foreach (EventWaitHandle signal in signals.Values) signal.Dispose();
            signals.Clear();
        }

        private static string EventName(string prefix, string command)
        {
            return prefix + "_" + command.TrimStart('-');
        }
    }
}
