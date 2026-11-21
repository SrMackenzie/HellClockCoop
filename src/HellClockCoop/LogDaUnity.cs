using System.Collections.Generic;
using UnityEngine;

namespace HellClockCoop
{
    /// <summary>
    /// Copia erros e exceções da Unity para o log do BepInEx (LogOutput.log). O listener do próprio
    /// BepInEx não funciona no Unity 6 deste jogo; sem isto, o LogOutput.log de um jogador não mostra
    /// o erro que ele viu e é preciso pedir o Player.log também.
    /// Mensagens repetidas são agrupadas: a 1ª aparece inteira; depois só a cada 100 repetições.
    /// </summary>
    internal static class LogDaUnity
    {
        private static readonly Dictionary<string, int> Contagem = new Dictionary<string, int>();
        private static bool _ativo;

        public static void Ativar()
        {
            if (_ativo) return;
            _ativo = true;
            Application.logMessageReceived += Receber;
        }

        private static void Receber(string mensagem, string pilha, LogType tipo)
        {
            if (tipo != LogType.Error && tipo != LogType.Exception && tipo != LogType.Assert) return;
            if (mensagem != null && mensagem.StartsWith("[")) // não reecoa o que já veio do BepInEx
            {
                if (mensagem.Contains(":Hell Clock Coop]") || mensagem.Contains(":   BepInEx]")) return;
            }
            string chave = mensagem ?? "";
            Contagem.TryGetValue(chave, out int n);
            Contagem[chave] = ++n;
            if (n == 1)
            {
                Plugin.Log.LogError($"[Unity {tipo}] {mensagem}\n{Resumir(pilha)}");
            }
            else if (n % 100 == 0)
            {
                Plugin.Log.LogError($"[Unity {tipo}] (repetido {n}x) {mensagem}");
            }
        }

        private static string Resumir(string pilha)
        {
            if (string.IsNullOrEmpty(pilha)) return "  (sem pilha)";
            string[] linhas = pilha.Split('\n');
            int max = linhas.Length < 8 ? linhas.Length : 8;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++)
            {
                if (linhas[i].Trim().Length > 0) sb.Append("  ").AppendLine(linhas[i].TrimEnd());
            }
            return sb.ToString().TrimEnd();
        }
    }
}
